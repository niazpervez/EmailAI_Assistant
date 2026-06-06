using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace EmailAI.WPF.ViewModels;

public partial class EmailListViewModel : ObservableObject
{
    private readonly IEmailRepository _emails;
    private readonly ISearchService _search;
    private readonly EmailAIService _emailAI;
    private readonly IMailService _mail;
    private readonly IAttachmentExtractor _extractor;
    private readonly IAttachmentRepository _attachments;

    [ObservableProperty] private string _title = "Inbox";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedSearchMode = "Hybrid";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private EmailListItem? _selectedEmail;
    [ObservableProperty] private bool _hasSelectedEmail;
    [ObservableProperty] private bool _showReplyPanel;
    [ObservableProperty] private string _replyText = "";
    [ObservableProperty] private bool _isGeneratingReply;
    [ObservableProperty] private bool _isProfessional = true;
    [ObservableProperty] private bool _isFriendly;
    [ObservableProperty] private bool _isFormal;
    [ObservableProperty] private bool _isShort;
    [ObservableProperty] private bool _canSendReply;
    [ObservableProperty] private bool _hasAttachments;
    [ObservableProperty] private bool _isLoadingAttachments;
    [ObservableProperty] private bool _showAttachmentPreview;
    [ObservableProperty] private string _attachmentPreviewTitle = "";
    [ObservableProperty] private string _attachmentPreviewText = "";
    [ObservableProperty] private bool _isLoadingAttachmentContent;
    [ObservableProperty] private string _displayBodyText = "";

    public List<string> SearchModes { get; } = ["Hybrid", "Semantic", "Keyword"];
    public ObservableCollection<EmailListItem> Emails { get; } = new();
    public ObservableCollection<AttachmentListItem> EmailAttachments { get; } = new();

    private string _currentFolder = "Inbox";
    private bool _isSearchMode;
    private CancellationTokenSource? _attachmentLoadCts;

    public EmailListViewModel(
        IEmailRepository emails,
        ISearchService search,
        EmailAIService emailAI,
        IMailService mail,
        IAttachmentExtractor extractor,
        IAttachmentRepository attachments)
    {
        _emails = emails;
        _search = search;
        _emailAI = emailAI;
        _mail = mail;
        _extractor = extractor;
        _attachments = attachments;
    }

    public void LoadFolder(string folder)
    {
        _currentFolder = folder;
        Title = folder;
        _isSearchMode = false;
        _ = LoadEmailsAsync();
    }

    public void SetSearchMode()
    {
        _isSearchMode = true;
        Title = "Search";
        Subtitle = "Keyword + semantic search powered by AI";
    }

    partial void OnSelectedEmailChanged(EmailListItem? value)
    {
        _attachmentLoadCts?.Cancel();
        _attachmentLoadCts?.Dispose();
        _attachmentLoadCts = new CancellationTokenSource();

        HasSelectedEmail = value is not null;
        ShowReplyPanel = false;
        ShowAttachmentPreview = false;
        ReplyText = "";
        EmailAttachments.Clear();
        HasAttachments = false;
        DisplayBodyText = value?.BodyText ?? "";

        if (value?.HasAttachments == true)
            _ = LoadAndAutoOpenAttachmentsAsync(value, _attachmentLoadCts.Token);
    }

    partial void OnReplyTextChanged(string value) =>
        CanSendReply = !string.IsNullOrWhiteSpace(value);

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) && !_isSearchMode)
        {
            await LoadEmailsAsync();
            return;
        }

        IsLoading = true;
        Emails.Clear();
        try
        {
            var mode = SelectedSearchMode switch
            {
                "Semantic" => SearchMode.Semantic,
                "Keyword"  => SearchMode.Keyword,
                _          => SearchMode.Hybrid
            };

            var results = await _search.SearchAsync(SearchQuery, mode, 50);
            foreach (var e in results)
                Emails.Add(MapEmail(e));

            Subtitle = $"{Emails.Count} result(s) for \"{SearchQuery}\"";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Search error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenReply()
    {
        if (SelectedEmail is null) return;
        ShowReplyPanel = true;
        ReplyText = "";
    }

    [RelayCommand]
    private void CloseReply() => ShowReplyPanel = false;

    [RelayCommand]
    private void CloseAttachmentPreview() => ShowAttachmentPreview = false;

    [RelayCommand]
    private async Task AiReply()
    {
        if (SelectedEmail is null) return;
        ShowReplyPanel = true;
        await GenerateReplyAsync();
    }

    [RelayCommand]
    private async Task GenerateReply()
    {
        await GenerateReplyAsync();
    }

    [RelayCommand]
    private async Task ReadAttachment(AttachmentListItem? item)
    {
        if (SelectedEmail is null || item is null) return;
        await ProcessAttachmentAsync(SelectedEmail, item, openExternallyIfNotText: true, showErrors: true);
    }

    [RelayCommand]
    private async Task OpenAttachment(AttachmentListItem? item)
    {
        if (SelectedEmail is null || item is null) return;
        await ProcessAttachmentAsync(SelectedEmail, item, openExternallyIfNotText: true, forceExternal: true, showErrors: true);
    }

    private async Task GenerateReplyAsync()
    {
        if (SelectedEmail is null) return;
        IsGeneratingReply = true;
        try
        {
            var tone = IsProfessional ? "professional"
                     : IsFriendly    ? "friendly"
                     : IsFormal      ? "formal"
                     : "short";
            ReplyText = await _emailAI.GenerateReplyAsync(SelectedEmail.EmailId, tone);
        }
        catch (Exception ex)
        {
            ReplyText = $"Error generating reply: {ex.Message}";
        }
        finally { IsGeneratingReply = false; }
    }

    [RelayCommand]
    private async Task SendReply()
    {
        if (SelectedEmail is null || string.IsNullOrWhiteSpace(ReplyText)) return;

        var confirm = MessageBox.Show(
            $"Send this reply to {SelectedEmail.SenderDisplay}?\n\nPreview:\n{ReplyText[..Math.Min(200, ReplyText.Length)]}…",
            "Confirm Send", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _emailAI.SendReplyAsync(SelectedEmail.EmailId, ReplyText);
            MessageBox.Show("Reply sent successfully!", "Sent", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowReplyPanel = false;
            ReplyText = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to send reply: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task Summarize()
    {
        if (SelectedEmail is null) return;
        try
        {
            var summary = await _emailAI.GetCustomerSummaryAsync(SelectedEmail.Email);
            MessageBox.Show(summary, $"Summary: {SelectedEmail.SenderDisplay}", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not generate summary: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task LoadAndAutoOpenAttachmentsAsync(EmailListItem email, CancellationToken ct)
    {
        IsLoadingAttachments = true;
        IsLoadingAttachmentContent = true;
        EmailAttachments.Clear();
        DisplayBodyText = email.BodyText + "\n\n⏳ Loading attachments…";

        try
        {
            var list = (await _mail.GetAttachmentsAsync(email.EmailId, ct)).ToList();
            if (ct.IsCancellationRequested || SelectedEmail?.EmailId != email.EmailId) return;

            foreach (var a in list)
            {
                EmailAttachments.Add(new AttachmentListItem
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeDisplay = FormatSize(a.SizeBytes),
                    CanReadText = _extractor.CanExtract(a.ContentType, a.FileName)
                });
            }

            HasAttachments = EmailAttachments.Count > 0;
            if (!HasAttachments)
            {
                DisplayBodyText = email.BodyText;
                return;
            }

            var body = new StringBuilder(email.BodyText);
            var attachmentSections = new StringBuilder();
            var openedExternally = new List<string>();
            var failed = new List<string>();

            foreach (var item in EmailAttachments.ToList())
            {
                if (ct.IsCancellationRequested || SelectedEmail?.EmailId != email.EmailId) return;

                var result = await ProcessAttachmentAsync(
                    email, item,
                    openExternallyIfNotText: true,
                    showErrors: false,
                    ct: ct);

                switch (result.Kind)
                {
                    case AttachmentProcessResultKind.Text:
                        attachmentSections.AppendLine();
                        attachmentSections.AppendLine($"── {item.FileName} ──");
                        attachmentSections.AppendLine(result.ExtractedText);
                        item.IsLoaded = true;
                        break;
                    case AttachmentProcessResultKind.OpenedExternally:
                        openedExternally.Add(item.FileName);
                        item.IsLoaded = true;
                        break;
                    case AttachmentProcessResultKind.Failed:
                        failed.Add($"{item.FileName}: {result.Error}");
                        break;
                }
            }

            if (attachmentSections.Length > 0)
            {
                body.AppendLine();
                body.AppendLine();
                body.AppendLine("📎 ATTACHMENTS");
                body.Append(attachmentSections);
            }

            if (openedExternally.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("Opened in your default app: " + string.Join(", ", openedExternally));
            }

            if (failed.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("Could not load: " + string.Join("; ", failed));
            }

            DisplayBodyText = body.ToString().TrimEnd();

            if (attachmentSections.Length > 0)
            {
                AttachmentPreviewTitle = EmailAttachments.Count == 1
                    ? EmailAttachments[0].FileName
                    : $"{EmailAttachments.Count} attachments";
                AttachmentPreviewText = attachmentSections.ToString().TrimEnd();
                ShowAttachmentPreview = true;
            }
        }
        catch (OperationCanceledException)
        {
            // user switched emails
        }
        catch (Exception ex)
        {
            if (SelectedEmail?.EmailId == email.EmailId)
                DisplayBodyText = email.BodyText + $"\n\n⚠️ Could not load attachments: {ex.Message}";
        }
        finally
        {
            IsLoadingAttachments = false;
            IsLoadingAttachmentContent = false;
        }
    }

    private async Task<AttachmentProcessResult> ProcessAttachmentAsync(
        EmailListItem email,
        AttachmentListItem item,
        bool openExternallyIfNotText,
        bool showErrors = false,
        bool forceExternal = false,
        CancellationToken ct = default)
    {
        try
        {
            byte[]? bytes = null;
            var stored = await _attachments.GetByAttachmentIdAsync($"{email.EmailId}|{item.Id}", ct);
            if (stored?.IsTextExtracted == true && !string.IsNullOrWhiteSpace(stored.ExtractedText) && !forceExternal)
            {
                return AttachmentProcessResult.FromText(stored.ExtractedText);
            }

            bytes = await _mail.GetAttachmentContentAsync(email.EmailId, item.Id, ct);
            if (bytes is null || bytes.Length == 0)
            {
                const string err = "download failed";
                if (showErrors)
                    MessageBox.Show("Could not download this attachment from the mail server.", "Attachment",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return AttachmentProcessResult.Fail(err);
            }

            if (!forceExternal && _extractor.CanExtract(item.ContentType, item.FileName))
            {
                var text = await _extractor.ExtractTextAsync(bytes, item.ContentType, item.FileName, ct);
                if (!string.IsNullOrWhiteSpace(text))
                    return AttachmentProcessResult.FromText(text);

                if (openExternallyIfNotText)
                {
                    await OpenAttachmentFileAsync(item, bytes);
                    return AttachmentProcessResult.OpenedExternal();
                }

                if (showErrors)
                    MessageBox.Show("This attachment has no readable text content.", "Attachment",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return AttachmentProcessResult.Fail("no readable text");
            }

            await OpenAttachmentFileAsync(item, bytes);
            return AttachmentProcessResult.OpenedExternal();
        }
        catch (Exception ex)
        {
            if (showErrors)
                MessageBox.Show($"Could not read attachment: {ex.Message}", "Attachment",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            return AttachmentProcessResult.Fail(ex.Message);
        }
    }

    private static async Task OpenAttachmentFileAsync(AttachmentListItem item, byte[] bytes)
    {
        var safeName = string.Join("_", item.FileName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(Path.GetTempPath(), "EmailAI", safeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task LoadEmailsAsync()
    {
        IsLoading = true;
        Emails.Clear();
        try
        {
            var result = await _emails.GetByFolderAsync(_currentFolder, pageSize: 100);
            foreach (var e in result)
                Emails.Add(MapEmail(e));
            Subtitle = $"{Emails.Count} emails";
        }
        catch (Exception ex)
        {
            Subtitle = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private static EmailListItem MapEmail(Email e) => new()
    {
        EmailId = e.EmailId,
        Email = e.Sender,
        Subject = e.Subject,
        SenderDisplay = string.IsNullOrEmpty(e.SenderName) ? e.Sender : $"{e.SenderName}",
        Preview = e.BodyText.Length > 80 ? e.BodyText[..80] + "…" : e.BodyText,
        DateDisplay = FormatDate(e.ReceivedDate),
        DateFull = e.ReceivedDate.ToString("dddd, MMMM d yyyy HH:mm"),
        FolderName = e.FolderName,
        BodyText = e.BodyText,
        IsRead = e.IsRead,
        IsImportant = e.IsImportant,
        HasAttachments = e.HasAttachments,
        SubjectWeight = e.IsRead ? "Normal" : "SemiBold"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    private static string FormatDate(DateTime dt)
    {
        var now = DateTime.Now;
        if (dt.Date == now.Date) return dt.ToString("HH:mm");
        if (dt.Date == now.Date.AddDays(-1)) return "Yesterday";
        if ((now - dt).TotalDays < 7) return dt.ToString("ddd");
        return dt.ToString("MMM d");
    }
}

public class EmailListItem
{
    public string EmailId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SenderDisplay { get; set; } = "";
    public string Preview { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string DateFull { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string BodyText { get; set; } = "";
    public bool IsRead { get; set; }
    public bool IsImportant { get; set; }
    public bool HasAttachments { get; set; }
    public string SubjectWeight { get; set; } = "Normal";
}

public class AttachmentListItem : ObservableObject
{
    private bool _isLoaded;
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string SizeDisplay { get; set; } = "";
    public bool CanReadText { get; set; }
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }
    public string ActionLabel => CanReadText ? "Read again" : "Open again";
    public string StatusIcon => IsLoaded ? "✓" : "";
}

internal enum AttachmentProcessResultKind { Text, OpenedExternally, Failed }

internal readonly record struct AttachmentProcessResult(
    AttachmentProcessResultKind Kind, string ExtractedText, string Error)
{
    public static AttachmentProcessResult FromText(string text) =>
        new(AttachmentProcessResultKind.Text, text, "");

    public static AttachmentProcessResult OpenedExternal() =>
        new(AttachmentProcessResultKind.OpenedExternally, "", "");

    public static AttachmentProcessResult Fail(string error) =>
        new(AttachmentProcessResultKind.Failed, "", error);
}

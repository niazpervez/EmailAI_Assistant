using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Core.Models;
using EmailAI.UI.Shared.Helpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using EmailAI.UI.Shared.Abstractions;

namespace EmailAI.UI.Shared.ViewModels;

public partial class EmailListViewModel : ObservableObject
{
    private readonly IEmailRepository _emails;
    private readonly ISyncStateRepository _syncStates;
    private readonly ISearchService _search;
    private readonly EmailAIService _emailAI;
    private readonly IMailService _mail;
    private readonly IAttachmentExtractor _extractor;
    private readonly IAttachmentRepository _attachments;
    private readonly IUiDispatcher _ui;
    private readonly IMessageService _messages;

    [ObservableProperty] private string _title = "Inbox";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _folderFilter = "";
    [ObservableProperty] private string _selectedSearchMode = "Hybrid";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private bool _showFolderPanel = true;
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
    public ObservableCollection<FolderListItem> Folders { get; } = new();
    public ObservableCollection<FolderListItem> FilteredFolders { get; } = new();
    public ObservableCollection<AttachmentListItem> EmailAttachments { get; } = new();

    private string _currentFolder = "Inbox";
    private CancellationTokenSource? _attachmentLoadCts;

    public EmailListViewModel(
        IEmailRepository emails,
        ISyncStateRepository syncStates,
        ISearchService search,
        EmailAIService emailAI,
        IMailService mail,
        IAttachmentExtractor extractor,
        IAttachmentRepository attachments,
        IUiDispatcher ui,
        IMessageService messages)
    {
        _emails = emails;
        _syncStates = syncStates;
        _search = search;
        _emailAI = emailAI;
        _mail = mail;
        _extractor = extractor;
        _attachments = attachments;
        _ui = ui;
        _messages = messages;
    }

    public void LoadFolder(string folder) => _ = InitializeMailViewAsync(folder);

    public async Task InitializeMailViewAsync(string startFolder = "Inbox")
    {
        IsSearchMode = false;
        ShowFolderPanel = true;
        await LoadFoldersAsync();

        var target = Folders.FirstOrDefault(f =>
                         f.Name.Equals(startFolder, StringComparison.OrdinalIgnoreCase))
                     ?? Folders.FirstOrDefault(f =>
                         f.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
                     ?? Folders.FirstOrDefault();

        if (target is not null)
            await SelectFolderInternalAsync(target);
    }

    public void SetSearchMode()
    {
        IsSearchMode = true;
        ShowFolderPanel = false;
        Title = "Search";
        Subtitle = "Keyword + semantic search across all mail";
        Emails.Clear();
        SelectedEmail = null;
        ClearFolderSelection();
    }

    partial void OnFolderFilterChanged(string value) => ApplyFolderFilter();

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
        DisplayBodyText = FormatBody(value?.BodyText ?? "");

        if (value?.HasAttachments == true)
            _ = LoadAndAutoOpenAttachmentsAsync(value, _attachmentLoadCts.Token);
    }

    partial void OnReplyTextChanged(string value) =>
        CanSendReply = !string.IsNullOrWhiteSpace(value);

    [RelayCommand]
    private async Task SelectFolder(FolderListItem? folder)
    {
        if (folder is null || IsSearchMode) return;
        await SelectFolderInternalAsync(folder);
    }

    [RelayCommand]
    private async Task RefreshMail()
    {
        if (IsSearchMode)
        {
            await Search();
            return;
        }

        await LoadFoldersAsync();
        await LoadEmailsAsync();
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) && !IsSearchMode)
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
            await _messages.ShowAlertAsync($"Search error: {ex.Message}", "Error", MessageIcon.Warning);
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

        var confirm = await _messages.ShowConfirmAsync(
            $"Send this reply to {SelectedEmail.SenderDisplay}?\n\nPreview:\n{ReplyText[..Math.Min(200, ReplyText.Length)]}…",
            "Confirm Send",
            MessageIcon.Question);

        if (!confirm) return;

        try
        {
            await _emailAI.SendReplyAsync(SelectedEmail.EmailId, ReplyText);
            await _messages.ShowAlertAsync("Reply sent successfully!", "Sent", MessageIcon.Information);
            ShowReplyPanel = false;
            ReplyText = "";
        }
        catch (Exception ex)
        {
            await _messages.ShowAlertAsync($"Failed to send reply: {ex.Message}", "Error", MessageIcon.Error);
        }
    }

    [RelayCommand]
    private async Task Summarize()
    {
        if (SelectedEmail is null) return;
        try
        {
            var summary = await _emailAI.GetCustomerSummaryAsync(SelectedEmail.Email);
            await _messages.ShowAlertAsync(summary, $"Summary: {SelectedEmail.SenderDisplay}");
        }
        catch (Exception ex)
        {
            await _messages.ShowAlertAsync($"Could not generate summary: {ex.Message}", "Error", MessageIcon.Warning);
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
                DisplayBodyText = FormatBody(email.BodyText);
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

            DisplayBodyText = FormatBody(body.ToString().TrimEnd());

            if (attachmentSections.Length > 0)
            {
                AttachmentPreviewTitle = EmailAttachments.Count == 1
                    ? EmailAttachments[0].FileName
                    : $"{EmailAttachments.Count} attachments";
                AttachmentPreviewText = FormatBody(attachmentSections.ToString().TrimEnd());
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
                    await _messages.ShowAlertAsync("Could not download this attachment from the mail server.", "Attachment", MessageIcon.Warning);
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
                    await _messages.ShowAlertAsync("This attachment has no readable text content.", "Attachment", MessageIcon.Information);
                return AttachmentProcessResult.Fail("no readable text");
            }

            await OpenAttachmentFileAsync(item, bytes);
            return AttachmentProcessResult.OpenedExternal();
        }
        catch (Exception ex)
        {
            if (showErrors)
                await _messages.ShowAlertAsync($"Could not read attachment: {ex.Message}", "Attachment", MessageIcon.Warning);
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

    public async Task SelectEmailByIdAsync(string emailId)
    {
        if (string.IsNullOrWhiteSpace(emailId)) return;

        IsLoading = true;
        try
        {
            var email = await _emails.GetByEmailIdAsync(emailId);
            if (email is null)
            {
                Subtitle = "Email not found in local database — try Sync first.";
                return;
            }

            IsSearchMode = false;
            ShowFolderPanel = true;
            _currentFolder = email.FolderName;
            Title = email.FolderName;
            Subtitle = "Opened from AI Chat";

            await LoadFoldersAsync();
            var folderItem = Folders.FirstOrDefault(f =>
                f.Name.Equals(email.FolderName, StringComparison.OrdinalIgnoreCase));
            if (folderItem is not null)
                MarkFolderSelected(folderItem);

            var item = Emails.FirstOrDefault(e => e.EmailId == emailId);
            if (item is null)
            {
                Emails.Clear();
                item = MapEmail(email);
                Emails.Add(item);

                var folderPeers = await _emails.GetByFolderAsync(email.FolderName, pageSize: 200);
                foreach (var peer in folderPeers.Where(p => p.EmailId != emailId))
                    Emails.Add(MapEmail(peer));
            }

            SelectedEmail = item;
            HasSelectedEmail = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SelectFolderInternalAsync(FolderListItem folder)
    {
        IsSearchMode = false;
        ShowFolderPanel = true;
        MarkFolderSelected(folder);
        _currentFolder = folder.Name;
        Title = folder.Name;
        SelectedEmail = null;
        await LoadEmailsAsync();
    }

    private async Task LoadFoldersAsync()
    {
        try
        {
            var summaries = (await _emails.GetFolderSummariesAsync()).ToDictionary(
                s => s.FolderName, StringComparer.OrdinalIgnoreCase);

            foreach (var sync in await _syncStates.GetAllAsync())
            {
                if (string.IsNullOrWhiteSpace(sync.FolderName)) continue;
                if (!summaries.ContainsKey(sync.FolderName))
                {
                    summaries[sync.FolderName] = new FolderSummary
                    {
                        FolderName = sync.FolderName,
                        TotalCount = sync.TotalSynced,
                        UnreadCount = 0
                    };
                }
            }

            var selectedName = Folders.FirstOrDefault(f => f.IsSelected)?.Name ?? _currentFolder;
            Folders.Clear();

            foreach (var summary in summaries.Values.OrderBy(GetFolderSortKey).ThenBy(s => s.FolderName))
            {
                Folders.Add(new FolderListItem
                {
                    Name = summary.FolderName,
                    Icon = GetFolderIcon(summary.FolderName),
                    TotalCount = summary.TotalCount,
                    UnreadCount = summary.UnreadCount,
                    IsSelected = summary.FolderName.Equals(selectedName, StringComparison.OrdinalIgnoreCase)
                });
            }

            ApplyFolderFilter();
        }
        catch (Exception ex)
        {
            Subtitle = $"Could not load folders: {ex.Message}";
        }
    }

    private void ApplyFolderFilter()
    {
        FilteredFolders.Clear();
        var query = FolderFilter.Trim();

        foreach (var folder in Folders)
        {
            if (query.Length == 0 ||
                folder.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFolders.Add(folder);
            }
        }
    }

    private void MarkFolderSelected(FolderListItem selected)
    {
        foreach (var folder in Folders)
            folder.IsSelected = folder == selected;
    }

    private void ClearFolderSelection()
    {
        foreach (var folder in Folders)
            folder.IsSelected = false;
    }

    private async Task LoadEmailsAsync()
    {
        IsLoading = true;
        Emails.Clear();
        try
        {
            var result = await _emails.GetByFolderAsync(_currentFolder, pageSize: 200);
            foreach (var e in result)
                Emails.Add(MapEmail(e));

            var unread = Emails.Count(e => e.IsUnread);
            Subtitle = unread > 0
                ? $"{Emails.Count} emails · {unread} unread"
                : $"{Emails.Count} emails";
        }
        catch (Exception ex)
        {
            Subtitle = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private static string FormatBody(string text) =>
        EmailBodyFormatter.EnhanceForMarkdown(text);

    private static int GetFolderSortKey(FolderSummary summary) =>
        GetFolderSortKey(summary.FolderName);

    private static int GetFolderSortKey(string name)
    {
        var key = name.ToLowerInvariant();
        return key switch
        {
            "inbox" => 0,
            "sent items" or "sent" or "sent mail" => 1,
            "drafts" => 2,
            "archive" or "archives" => 3,
            "deleted items" or "trash" => 4,
            "junk email" or "junk" or "spam" => 5,
            _ when key.StartsWith("inbox/") => 10,
            _ => 20
        };
    }

    private static string GetFolderIcon(string name)
    {
        var key = name.ToLowerInvariant();
        if (key is "inbox") return "📥";
        if (key is "sent items" or "sent" or "sent mail") return "📤";
        if (key is "drafts") return "📝";
        if (key is "deleted items" or "trash") return "🗑️";
        if (key is "junk email" or "junk" or "spam") return "⚠️";
        if (key is "archive" or "archives") return "📦";
        if (key is "outbox") return "📮";
        if (key.Contains("calendar", StringComparison.OrdinalIgnoreCase)) return "📅";
        if (key.Contains("contact", StringComparison.OrdinalIgnoreCase)) return "👤";
        return "📁";
    }

    private static EmailListItem MapEmail(Email e) => new()
    {
        EmailId = e.EmailId,
        Email = e.Sender,
        Subject = e.Subject,
        SenderDisplay = string.IsNullOrEmpty(e.SenderName) ? e.Sender : e.SenderName,
        SenderInitial = GetSenderInitial(e.SenderName, e.Sender),
        Preview = e.BodyText.Length > 100 ? e.BodyText[..100].Trim() + "…" : e.BodyText.Trim(),
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

    private static string GetSenderInitial(string? name, string email)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return char.ToUpperInvariant(name.Trim()[0]).ToString();
        if (!string.IsNullOrEmpty(email))
            return char.ToUpperInvariant(email.Trim()[0]).ToString();
        return "?";
    }
}

public class EmailListItem
{
    public string EmailId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SenderDisplay { get; set; } = "";
    public string SenderInitial { get; set; } = "?";
    public string Preview { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string DateFull { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string BodyText { get; set; } = "";
    public bool IsRead { get; set; }
    public bool IsUnread => !IsRead;
    public bool IsImportant { get; set; }
    public bool HasAttachments { get; set; }
    public string SubjectWeight { get; set; } = "Normal";
}

public partial class FolderListItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public bool HasUnread => UnreadCount > 0;
    public string CountDisplay => TotalCount > 0 ? TotalCount.ToString() : "";
    public string UnreadDisplay => UnreadCount > 0 ? UnreadCount.ToString() : "";

    [ObservableProperty]
    private bool _isSelected;
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

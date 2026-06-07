using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;
using EmailAI.UI.Shared.Abstractions;
using EmailAI.UI.Shared.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace EmailAI.UI.Shared.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly ChatService _chat;
    private readonly IEmailRepository _emails;
    private readonly EmailNavigationService _emailNavigation;
    private readonly IUiDispatcher _ui;

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private string _contextInfo = "";
    [ObservableProperty] private SessionItem? _selectedSession;

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<SessionItem> Sessions { get; } = new();

    private string _sessionId;

    public bool CanSend => !string.IsNullOrWhiteSpace(InputText) && !IsThinking;

    public ChatViewModel(
        ChatService chat,
        IEmailRepository emails,
        EmailNavigationService emailNavigation,
        IUiDispatcher ui)
    {
        _chat = chat;
        _emails = emails;
        _emailNavigation = emailNavigation;
        _ui = ui;
        _sessionId = chat.NewSession();
        _ = LoadSessionsAsync();
    }

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(CanSend));
    partial void OnIsThinkingChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    [RelayCommand]
    private async Task Send()
    {
        if (!CanSend) return;

        var message = InputText.Trim();
        InputText = "";

        AddMessage(new ChatMessageItem
        {
            Role = "user",
            Content = message,
            CreatedAt = DateTime.Now
        });

        IsThinking = true;
        HasMessages = true;

        try
        {
            var response = await _chat.SendMessageAsync(new ChatRequest(message, _sessionId));

            var assistant = new ChatMessageItem
            {
                Role = "assistant",
                Content = response.Message,
                CreatedAt = DateTime.Now,
                SourceEmailIds = response.SourceEmailIds.ToList()
            };
            await PopulateSourceLinksAsync(assistant, response.SourceEmailIds);
            AddMessage(assistant);

            ContextInfo = assistant.SourceLinks.Count > 0
                ? $"📎 {assistant.SourceLinks.Count} reference email(s)"
                : $"{response.SourceEmailIds.Count()} email(s) referenced";
        }
        catch (Exception ex)
        {
            var msg = ex is OperationCanceledException
                ? "Request timed out. Try a more specific question or run Sync first."
                : ex.Message;
            AddMessage(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"⚠️ **Error:** {msg}\n\nPlease check your DeepSeek API key in Settings.",
                CreatedAt = DateTime.Now
            });
        }
        finally
        {
            IsThinking = false;
        }
    }

    [RelayCommand]
    private void OpenSourceEmail(EmailSourceLink? link)
    {
        if (link is null) return;
        _emailNavigation.OpenEmail(link.EmailId);
    }

    [RelayCommand]
    private async Task Suggestion(string prompt)
    {
        InputText = prompt;
        await Send();
    }

    [RelayCommand]
    private void NewSession()
    {
        _sessionId = _chat.NewSession();
        Messages.Clear();
        HasMessages = false;
        ContextInfo = "";
    }

    [RelayCommand]
    private void ClearSession()
    {
        Messages.Clear();
        HasMessages = false;
        ContextInfo = "";
        _sessionId = _chat.NewSession();
    }

    private async Task PopulateSourceLinksAsync(ChatMessageItem item, IEnumerable<string> emailIds)
    {
        item.SourceLinks.Clear();
        foreach (var id in emailIds.Distinct().Take(12))
        {
            var email = await _emails.GetByEmailIdAsync(id);
            if (email is null) continue;

            item.SourceLinks.Add(new EmailSourceLink
            {
                EmailId = email.EmailId,
                Subject = string.IsNullOrWhiteSpace(email.Subject) ? "(No subject)" : email.Subject,
                SenderDisplay = string.IsNullOrWhiteSpace(email.SenderName) ? email.Sender : email.SenderName,
                FolderName = email.FolderName,
                DateDisplay = email.ReceivedDate.ToString("MMM d, yyyy")
            });
        }
    }

    private void AddMessage(ChatMessageItem item)
    {
        _ui.Invoke(() =>
        {
            Messages.Add(item);
            HasMessages = true;
        });
    }

    private async Task LoadSessionsAsync()
    {
        try
        {
            var sessions = await _chat.GetSessionsAsync();
            Sessions.Clear();
            int i = 1;
            foreach (var s in sessions)
                Sessions.Add(new SessionItem { Id = s, Label = $"💬 Chat {i++}" });
        }
        catch { /* ignore */ }
    }

    partial void OnSelectedSessionChanged(SessionItem? value)
    {
        if (value is null) return;
        _sessionId = value.Id;
        _ = LoadSessionHistoryAsync(value.Id);
    }

    private async Task LoadSessionHistoryAsync(string sessionId)
    {
        var history = await _chat.GetHistoryAsync(sessionId);
        Messages.Clear();
        foreach (var msg in history)
        {
            var item = new ChatMessageItem
            {
                Role = msg.Role,
                Content = msg.Content,
                CreatedAt = msg.CreatedAt
            };

            if (!string.IsNullOrWhiteSpace(msg.RelevantEmailIds))
            {
                try
                {
                    var ids = JsonSerializer.Deserialize<List<string>>(msg.RelevantEmailIds) ?? [];
                    item.SourceEmailIds = ids;
                    await PopulateSourceLinksAsync(item, ids);
                }
                catch { /* ignore */ }
            }

            Messages.Add(item);
        }
        HasMessages = Messages.Count > 0;
    }
}

public partial class ChatMessageItem : ObservableObject
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<string> SourceEmailIds { get; set; } = new();
    public ObservableCollection<EmailSourceLink> SourceLinks { get; } = new();

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public string TimeText => CreatedAt.ToString("HH:mm");
    public bool HasSources => SourceLinks.Count > 0;
}

public class EmailSourceLink
{
    public string EmailId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SenderDisplay { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string DateDisplay { get; set; } = "";
    public string Detail => $"{SenderDisplay} · {FolderName} · {DateDisplay}";
}

public class SessionItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

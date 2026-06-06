using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using EmailAI.Core.DTOs;
using System.Collections.ObjectModel;

namespace EmailAI.WPF.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly ChatService _chat;

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private string _contextInfo = "";
    [ObservableProperty] private SessionItem? _selectedSession;

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<SessionItem> Sessions { get; } = new();

    private string _sessionId;

    public bool CanSend => !string.IsNullOrWhiteSpace(InputText) && !IsThinking;

    public ChatViewModel(ChatService chat)
    {
        _chat = chat;
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

        // Add user bubble
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

            AddMessage(new ChatMessageItem
            {
                Role = "assistant",
                Content = response.Message,
                CreatedAt = DateTime.Now,
                SourceEmailIds = response.SourceEmailIds.ToList()
            });

            ContextInfo = $"{response.SourceEmailIds.Count()} email(s) referenced";
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessageItem
            {
                Role = "assistant",
                Content = $"⚠️ Error: {ex.Message}\n\nPlease check your DeepSeek API key in Settings.",
                CreatedAt = DateTime.Now
            });
        }
        finally
        {
            IsThinking = false;
        }
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

    private void AddMessage(ChatMessageItem item)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                Sessions.Add(new SessionItem { Id = s, Label = $"Chat {i++}" });
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
            Messages.Add(new ChatMessageItem
            {
                Role = msg.Role,
                Content = msg.Content,
                CreatedAt = msg.CreatedAt
            });
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

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public string TimeText => CreatedAt.ToString("HH:mm");
    public bool HasSources => SourceEmailIds.Count > 0;
    public string SourcesText => HasSources
        ? $"📎 Based on {SourceEmailIds.Count} email(s)"
        : "";
}

public class SessionItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

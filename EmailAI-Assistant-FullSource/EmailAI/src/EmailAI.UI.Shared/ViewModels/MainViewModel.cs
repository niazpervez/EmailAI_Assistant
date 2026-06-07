using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Core.Interfaces;
using EmailAI.UI.Shared.Abstractions;
using EmailAI.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EmailAI.UI.Shared.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IMailService _mail;
    private readonly IEmailRepository _emails;
    private readonly ISyncService _sync;
    private readonly EmailNavigationService _emailNavigation;
    private readonly IUiDispatcher _ui;
    private readonly INavigationViewFactory? _viewFactory;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _userName = "Not signed in";
    [ObservableProperty] private string _userEmail = "";
    [ObservableProperty] private string _userInitial = "?";
    [ObservableProperty] private string _syncStatusText = "Not synced";
    [ObservableProperty] private string _lastSyncText = "";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _hasUnread;
    [ObservableProperty] private string _syncStatusColor = "#808080";
    [ObservableProperty] private string _currentPage = "Dashboard";

    public string? PendingEmailId { get; private set; }

    public event Action<string>? PageChanged;

    public MainViewModel(
        IServiceProvider services,
        IMailService mail,
        IEmailRepository emails,
        ISyncService sync,
        EmailNavigationService emailNavigation,
        IUiDispatcher ui,
        INavigationViewFactory? viewFactory = null)
    {
        _services = services;
        _mail = mail;
        _emails = emails;
        _sync = sync;
        _emailNavigation = emailNavigation;
        _ui = ui;
        _viewFactory = viewFactory;

        _sync.SyncProgressChanged += OnSyncProgress;
        _emailNavigation.OpenEmailRequested += emailId =>
            _ui.Invoke(() => NavigateToEmail(emailId));
    }

    public async Task RefreshUserInfoAsync()
    {
        if (await _mail.IsAuthenticatedAsync())
        {
            UserName = await _mail.GetUserDisplayNameAsync();
            UserEmail = await _mail.GetUserEmailAsync();
            UserInitial = UserName.Length > 0 ? UserName[0].ToString().ToUpper() : "?";
        }
        else
        {
            UserName = "Not signed in";
            UserEmail = "";
            UserInitial = "?";
        }
        await RefreshCountsAsync();
    }

    public async Task InitializeAsync()
    {
        await RefreshUserInfoAsync();
        Navigate("Dashboard");
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page;
        PageChanged?.Invoke(page);

        if (_viewFactory is not null)
        {
            CurrentView = page switch
            {
                "Dashboard" => _viewFactory.CreateView(page),
                "Chat" => _viewFactory.CreateView(page),
                "Inbox" => _viewFactory.CreateView(page, vm =>
                {
                    if (vm is EmailListViewModel el) _ = el.InitializeMailViewAsync();
                }),
                "Search" => _viewFactory.CreateView(page, vm =>
                {
                    if (vm is EmailListViewModel el) el.SetSearchMode();
                }),
                "Sync" => _viewFactory.CreateView(page),
                "Settings" => _viewFactory.CreateView(page),
                _ => CurrentView
            };
        }
    }

    private void NavigateToEmail(string emailId)
    {
        CurrentPage = "Inbox";
        PendingEmailId = emailId;
        PageChanged?.Invoke("Inbox");

        if (_viewFactory is not null)
        {
            CurrentView = _viewFactory.CreateView("Inbox", vm =>
            {
                if (vm is EmailListViewModel el) _ = el.SelectEmailByIdAsync(emailId);
            });
            PendingEmailId = null;
        }
    }

    private void OnSyncProgress(object? sender, SyncProgress progress)
    {
        _ui.Invoke(() =>
        {
            SyncStatusText = progress.Status switch
            {
                "syncing" => $"Syncing {progress.FolderName}…",
                "error"   => "Sync error",
                _         => "Synced"
            };
            SyncStatusColor = progress.Status switch
            {
                "syncing" => "#F5A623",
                "error"   => "#F24C4C",
                _         => "#3ECF8E"
            };
            LastSyncText = $"Last: {DateTime.Now:HH:mm}";
        });
    }

    private async Task RefreshCountsAsync()
    {
        try
        {
            UnreadCount = await _emails.GetUnreadCountAsync();
            HasUnread = UnreadCount > 0;
        }
        catch { /* not initialized yet */ }
    }

    public void ClearPendingEmail() => PendingEmailId = null;
}

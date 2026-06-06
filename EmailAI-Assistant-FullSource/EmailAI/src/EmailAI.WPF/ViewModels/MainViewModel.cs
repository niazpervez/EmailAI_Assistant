using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Core.Interfaces;
using EmailAI.WPF.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;

namespace EmailAI.WPF.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IMailService _mail;
    private readonly IEmailRepository _emails;
    private readonly ISyncService _sync;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _userName = "Not signed in";
    [ObservableProperty] private string _userEmail = "";
    [ObservableProperty] private string _userInitial = "?";
    [ObservableProperty] private string _syncStatusText = "Not synced";
    [ObservableProperty] private string _lastSyncText = "";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _hasUnread;
    [ObservableProperty] private Brush _syncStatusColor = Brushes.Gray;
    [ObservableProperty] private string _currentPage = "Dashboard";

    public MainViewModel(
        IServiceProvider services,
        IMailService mail,
        IEmailRepository emails,
        ISyncService sync)
    {
        _services = services;
        _mail = mail;
        _emails = emails;
        _sync = sync;

        _sync.SyncProgressChanged += OnSyncProgress;
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
        CurrentView = page switch
        {
            "Dashboard" => CreateView<DashboardView, DashboardViewModel>(),
            "Chat"      => CreateView<ChatView, ChatViewModel>(),
            "Inbox"     => CreateView<EmailListView, EmailListViewModel>(vm => vm.LoadFolder("Inbox")),
            "Search"    => CreateView<EmailListView, EmailListViewModel>(vm => vm.SetSearchMode()),
            "Sync"      => CreateView<SyncView, SyncViewModel>(),
            "Settings"  => CreateView<SettingsView, SettingsViewModel>(),
            _           => CurrentView
        };
    }

    private TView CreateView<TView, TViewModel>(Action<TViewModel>? configure = null)
        where TView : System.Windows.FrameworkElement, new()
        where TViewModel : class
    {
        var vm = _services.GetRequiredService<TViewModel>();
        configure?.Invoke(vm);
        var view = new TView { DataContext = vm };
        return view;
    }

    private void OnSyncProgress(object? sender, Core.Interfaces.SyncProgress progress)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SyncStatusText = progress.Status switch
            {
                "syncing" => $"Syncing {progress.FolderName}…",
                "error"   => "Sync error",
                _         => "Synced"
            };
            SyncStatusColor = progress.Status switch
            {
                "syncing" => new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                "error"   => new SolidColorBrush(Color.FromRgb(242, 76, 76)),
                _         => new SolidColorBrush(Color.FromRgb(62, 207, 142))
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
}

using EmailAI.UI.Shared.Abstractions;
using EmailAI.UI.Shared.ViewModels;
using EmailAI.WPF.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EmailAI.WPF.Services;

public sealed class WpfNavigationViewFactory : INavigationViewFactory
{
    private readonly IServiceProvider _services;

    public WpfNavigationViewFactory(IServiceProvider services) => _services = services;

    public object? CreateView(string page, Action<object>? configure = null)
    {
        return page switch
        {
            "Dashboard" => CreateView<DashboardView, DashboardViewModel>(configure),
            "Chat" => CreateView<ChatView, ChatViewModel>(configure),
            "Inbox" => CreateView<EmailListView, EmailListViewModel>(configure),
            "Search" => CreateView<EmailListView, EmailListViewModel>(configure),
            "Sync" => CreateView<SyncView, SyncViewModel>(configure),
            "Settings" => CreateView<SettingsView, SettingsViewModel>(configure),
            _ => null
        };
    }

    private object CreateView<TView, TViewModel>(Action<object>? configure = null)
        where TView : System.Windows.FrameworkElement, new()
        where TViewModel : class
    {
        var vm = _services.GetRequiredService<TViewModel>();
        configure?.Invoke(vm);
        return new TView { DataContext = vm };
    }
}

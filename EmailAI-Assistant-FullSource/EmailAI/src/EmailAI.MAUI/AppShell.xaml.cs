using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI;

public partial class AppShell : Shell
{
    private readonly MainViewModel _main;

    public AppShell(MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        BindingContext = main;
        _main.PageChanged += OnPageChanged;
    }

    private async void OnPageChanged(string page)
    {
        var route = page switch
        {
            "Dashboard" => "//dashboard",
            "Chat" => "//chat",
            "Inbox" => "//inbox",
            "Search" => "//search",
            "Sync" => "//sync",
            "Settings" => "//settings",
            _ => "//dashboard"
        };

        await Dispatcher.DispatchAsync(async () =>
        {
            await GoToAsync(route);
            FlyoutIsPresented = false;
        });
    }
}

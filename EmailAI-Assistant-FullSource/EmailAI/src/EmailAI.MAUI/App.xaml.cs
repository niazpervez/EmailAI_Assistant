using EmailAI.Core;
using EmailAI.Infrastructure.Data;
using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly DatabaseInitializer _dbInit;
    private readonly MainViewModel _main;
    private readonly AppShell _shell;

    public App(DatabaseInitializer dbInit, MainViewModel main, AppShell shell)
    {
        InitializeComponent();
        _dbInit = dbInit;
        _main = main;
        _shell = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = InitializeAsync();
        return new Window(_shell);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _dbInit.InitializeAsync();
            await _main.InitializeAsync();
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppPaths.GetAppDataDirectory(), "startup-error.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                await File.WriteAllTextAsync(logPath, ex.ToString());
            }
            catch { /* best effort */ }

            if (MainPage is not null)
            {
                await MainPage.DisplayAlert(
                    "Startup Error",
                    $"EmailAI Assistant failed to start:\n\n{ex.Message}",
                    "OK");
            }
        }
    }
}

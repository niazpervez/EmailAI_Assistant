using EmailAI.Composition;
using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.UI.Shared.Abstractions;
using EmailAI.WPF.Services;
using EmailAI.WPF.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;

namespace EmailAI.WPF;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private string _dbPath = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _ = StartupAsync();
    }

    private async Task StartupAsync()
    {
        try
        {
            _host = CreateHost();
            await _host.StartAsync();

            var dbInit = _host.Services.GetRequiredService<DatabaseInitializer>();
            await InitializeDatabaseAsync(dbInit);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
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

            MessageBox.Show(
                $"EmailAI Assistant failed to start:\n\n{ex.Message}\n\nDetails saved to:\n{logPath}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task InitializeDatabaseAsync(DatabaseInitializer dbInit)
    {
        try
        {
            await dbInit.InitializeAsync();
        }
        catch when (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
            await dbInit.InitializeAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private IHost CreateHost()
    {
        var appData = AppPaths.GetAppDataDirectory();
        Directory.CreateDirectory(appData);
        _dbPath = AppPaths.GetDatabasePath();

        return Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddEmailAIInfrastructure(_dbPath, platform =>
                {
                    platform.AddSingleton<IOAuthParentWindowProvider, WpfOAuthParentWindowProvider>();
                    platform.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
                    platform.AddSingleton<IMessageService, WpfMessageService>();
                    platform.AddSingleton<IFilePickerService, WpfFilePickerService>();
                    platform.AddSingleton<INavigationViewFactory, WpfNavigationViewFactory>();
                });
                services.AddEmailAIViewModels();
                services.AddSingleton<MainWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();
    }
}

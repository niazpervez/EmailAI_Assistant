using EmailAI.Application.Services;
using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.Infrastructure.Data.Repositories;
using EmailAI.Infrastructure.Security;
using EmailAI.Infrastructure.Services;
using EmailAI.Infrastructure.Services.AI;
using EmailAI.Infrastructure.Services.Mail;
using EmailAI.Infrastructure.Services.Sync;
using EmailAI.WPF.Services;
using EmailAI.WPF.ViewModels;
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
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName, "startup-error.log");
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
            // Recover from a partially created database (e.g. prior crash during DDL)
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
        // Determine app data path
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName);
        Directory.CreateDirectory(appData);

        _dbPath = Path.Combine(appData, "emailai.db");

        return Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // ── Infrastructure ──────────────────────────────────────────

                services.AddSingleton(_ => new DatabaseConnectionFactory(_dbPath));
                services.AddSingleton(sp => new DatabaseInitializer(
                    _dbPath, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));

                services.AddSingleton<IEncryptionService, DpapiEncryptionService>();

                // Repositories
                services.AddSingleton<IEmailRepository, EmailRepository>();
                services.AddSingleton<IAttachmentRepository, AttachmentRepository>();
                services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
                services.AddSingleton<ISyncStateRepository, SyncStateRepository>();
                services.AddSingleton<IChatRepository, ChatRepository>();
                services.AddSingleton<ISettingsRepository, SettingsRepository>();

                services.AddSingleton<IOAuthParentWindowProvider, WpfOAuthParentWindowProvider>();
                services.AddSingleton<IMailService, ImapMailService>();
                services.AddHttpClient();
                services.AddSingleton<MailOAuthService>();

                // HTTP clients for AI services — auth handler injects API key from settings at runtime
                services.AddTransient<DeepSeekAuthHandler>();

                services.AddHttpClient<DeepSeekAIService>((_, http) =>
                {
                    http.DefaultRequestHeaders.Add("Accept", "application/json");
                }).AddHttpMessageHandler<DeepSeekAuthHandler>();

                services.AddHttpClient<DeepSeekEmbeddingService>((_, http) =>
                {
                    http.DefaultRequestHeaders.Add("Accept", "application/json");
                }).AddHttpMessageHandler<DeepSeekAuthHandler>();

                // Forward interfaces to typed HttpClients so the auth handler is always used.
                services.AddTransient<IAIService>(sp => sp.GetRequiredService<DeepSeekAIService>());
                services.AddTransient<IEmbeddingService>(sp => sp.GetRequiredService<DeepSeekEmbeddingService>());
                services.AddSingleton<IAttachmentExtractor, AttachmentExtractor>();

                services.AddSingleton<ISyncService, SyncService>();
                services.AddSingleton<ISearchService, SearchService>();
                services.AddHostedService<AutoSyncHostedService>();

                // ── Application Services ────────────────────────────────────

                services.AddSingleton<DashboardService>();
                services.AddSingleton<ChatService>();
                services.AddSingleton<EmailAIService>();

                // ── ViewModels ──────────────────────────────────────────────

                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ChatViewModel>();
                services.AddTransient<EmailListViewModel>();
                services.AddTransient<SyncViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ReplyViewModel>();

                // ── Views ───────────────────────────────────────────────────

                services.AddSingleton<MainWindow>();

                // ── Logging ─────────────────────────────────────────────────

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

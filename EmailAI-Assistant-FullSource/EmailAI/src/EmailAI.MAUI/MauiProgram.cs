using EmailAI.Composition;
using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.MAUI.Pages;
using EmailAI.MAUI.Services;
using EmailAI.UI.Shared.Abstractions;
using EmailAI.UI.Shared.ViewModels;
using Microsoft.Extensions.Logging;

namespace EmailAI.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var appData = AppPaths.GetAppDataDirectory();
        Directory.CreateDirectory(appData);
        var dbPath = AppPaths.GetDatabasePath();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddEmailAIInfrastructure(dbPath, platform =>
        {
            platform.AddSingleton<IOAuthParentWindowProvider, MauiOAuthParentWindowProvider>();
            platform.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
            platform.AddSingleton<IMessageService, MauiMessageService>();
            platform.AddSingleton<IFilePickerService, MauiFilePickerService>();
        });
        builder.Services.AddEmailAIViewModels();

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<EmailListPage>();
        builder.Services.AddTransient<SyncPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

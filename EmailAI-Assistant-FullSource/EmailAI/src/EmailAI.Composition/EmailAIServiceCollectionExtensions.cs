using EmailAI.Application.Services;
using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.Infrastructure.Data.Repositories;
using EmailAI.Infrastructure.Security;
using EmailAI.Infrastructure.Services;
using EmailAI.Infrastructure.Services.AI;
using EmailAI.Infrastructure.Services.Import;
using EmailAI.Infrastructure.Services.Mail;
using EmailAI.Infrastructure.Services.Sync;
using EmailAI.UI.Shared.Services;
using EmailAI.UI.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EmailAI.Composition;

public static class EmailAIServiceCollectionExtensions
{
    public static IServiceCollection AddEmailAIInfrastructure(
        this IServiceCollection services,
        string dbPath,
        Action<IServiceCollection>? configurePlatform = null)
    {
        services.AddSingleton(_ => new DatabaseConnectionFactory(dbPath));
        services.AddSingleton(sp => new DatabaseInitializer(
            dbPath, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DatabaseInitializer>>()));

        services.AddSingleton<IEncryptionService, PlatformEncryptionService>();

        services.AddSingleton<IEmailRepository, EmailRepository>();
        services.AddSingleton<IAttachmentRepository, AttachmentRepository>();
        services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
        services.AddSingleton<IChunkRepository, ChunkRepository>();
        services.AddSingleton<ISyncStateRepository, SyncStateRepository>();
        services.AddSingleton<IChatRepository, ChatRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        services.AddSingleton<EmailNavigationService>();
        services.AddSingleton<IMailService, ImapMailService>();
        services.AddSingleton<IExternalMailImportService, ExternalMailImportService>();
        services.AddHttpClient();
        services.AddSingleton<MailOAuthService>();

        services.AddTransient<DeepSeekAuthHandler>();

        services.AddHttpClient<DeepSeekAIService>((_, http) =>
        {
            http.Timeout = TimeSpan.FromSeconds(AppConstants.ChatTimeoutSeconds);
            http.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddHttpMessageHandler<DeepSeekAuthHandler>();

        services.AddHttpClient<DeepSeekEmbeddingService>((_, http) =>
        {
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.Add("Accept", "application/json");
        }).AddHttpMessageHandler<DeepSeekAuthHandler>();

        services.AddTransient<IAIService>(sp => sp.GetRequiredService<DeepSeekAIService>());
        services.AddTransient<IEmbeddingService>(sp => sp.GetRequiredService<DeepSeekEmbeddingService>());
        services.AddSingleton<IAttachmentExtractor, AttachmentExtractor>();
        services.AddSingleton<IAttachmentContextService, AttachmentContextService>();
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<IChunkIndexingService, ChunkIndexingService>();
        services.AddSingleton<IRagSearchService, RagSearchService>();

        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddHostedService<AutoSyncHostedService>();

        services.AddSingleton<SentFollowUpService>();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<EmailAIService>();

        configurePlatform?.Invoke(services);

        return services;
    }

    public static IServiceCollection AddEmailAIViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<EmailListViewModel>();
        services.AddTransient<SyncViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ReplyViewModel>();
        return services;
    }
}

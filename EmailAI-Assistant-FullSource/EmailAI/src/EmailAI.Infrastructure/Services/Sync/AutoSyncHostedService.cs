using EmailAI.Core;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services.Sync;

/// <summary>
/// Background service that triggers automatic email sync on a configurable interval.
/// Runs as a hosted service within the .NET Generic Host.
/// </summary>
public sealed class AutoSyncHostedService : BackgroundService
{
    private readonly ISyncService _sync;
    private readonly ISettingsRepository _settings;
    private readonly IMailService _mail;
    private readonly ILogger<AutoSyncHostedService> _logger;

    public AutoSyncHostedService(
        ISyncService sync,
        ISettingsRepository settings,
        IMailService mail,
        ILogger<AutoSyncHostedService> logger)
    {
        _sync = sync; _settings = settings;
        _mail = mail; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to fully initialize before first sync
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = await GetIntervalAsync(stoppingToken);

            if (intervalMinutes > 0 && await _mail.IsAuthenticatedAsync(stoppingToken))
            {
                _logger.LogInformation("Auto-sync triggered");
                try
                {
                    await _sync.SyncAllFoldersAsync(progress: null, ct: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-sync failed");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes > 0 ? intervalMinutes : 15), stoppingToken);
        }
    }

    private async Task<int> GetIntervalAsync(CancellationToken ct)
    {
        var val = await _settings.GetAsync(SettingsKeys.SyncIntervalMinutes, ct);
        return int.TryParse(val, out var minutes) ? minutes : 15;
    }
}

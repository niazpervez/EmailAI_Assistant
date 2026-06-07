using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services.Sync;

public sealed class SyncService : ISyncService
{
    private readonly IMailService _mail;
    private readonly IEmailRepository _emails;
    private readonly ISyncStateRepository _syncStates;
    private readonly IAttachmentContextService _attachmentContext;
    private readonly IChunkIndexingService _chunkIndexing;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<SyncService> _logger;

    private bool _isRunning;

    public event EventHandler<SyncProgress>? SyncProgressChanged;

    public SyncService(
        IMailService mail,
        IEmailRepository emails,
        ISyncStateRepository syncStates,
        IAttachmentContextService attachmentContext,
        IChunkIndexingService chunkIndexing,
        ISettingsRepository settings,
        ILogger<SyncService> logger)
    {
        _mail = mail; _emails = emails; _syncStates = syncStates;
        _attachmentContext = attachmentContext;
        _chunkIndexing = chunkIndexing; _settings = settings; _logger = logger;
    }

    public Task<bool> IsRunningAsync() => Task.FromResult(_isRunning);

    public async Task SyncAllFoldersAsync(IProgress<SyncProgress>? progress = null, SyncOptions? options = null, CancellationToken ct = default)
    {
        if (_isRunning) return;
        _isRunning = true;

        try
        {
            var syncOptions = options ?? await LoadSyncOptionsAsync(ct);
            await _syncStates.DeleteInvalidAsync(ct);
            Report(progress, "—", 0, 0, $"Sync period: {syncOptions.PeriodLabel}");

            var folders = (await _mail.GetMailFoldersAsync(ct)).ToList();
            var syncAll = await ShouldSyncAllFoldersAsync(ct);

            List<Core.Interfaces.MailFolder> targetFolders;
            if (syncAll)
            {
                targetFolders = folders;
                Report(progress, "—", 0, 0, $"Syncing all {folders.Count} folder(s) on server…");
            }
            else
            {
                var selectedFolders = await GetSelectedFolderNamesAsync(ct);
                targetFolders = folders
                    .Where(f => selectedFolders.Contains(f.DisplayName, StringComparer.OrdinalIgnoreCase)
                             || selectedFolders.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (targetFolders.Count == 0 && folders.Count > 0)
                {
                    _logger.LogWarning(
                        "No IMAP folders matched settings {Selected}. Available: {Available}. Syncing Inbox.",
                        string.Join(", ", selectedFolders),
                        string.Join(", ", folders.Select(f => f.DisplayName)));

                    targetFolders = folders
                        .Where(f => f.DisplayName.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (targetFolders.Count == 0)
                        targetFolders = [folders[0]];
                }
            }

            if (targetFolders.Count == 0)
            {
                var available = folders.Count > 0
                    ? string.Join(", ", folders.Select(f => f.DisplayName).Take(12))
                    : "(none)";
                var message = folders.Count == 0
                    ? "No mail folders found on server — check IMAP is enabled for your Hotmail account"
                    : $"No folders matched your Settings selection. Available on server: {available}";
                Report(progress, "—", 0, 0, message);
                return;
            }

            foreach (var folder in targetFolders)
            {
                if (ct.IsCancellationRequested) break;
                await SyncFolderAsync(folder.Id, folder.DisplayName, progress, syncOptions, ct);
            }

            await BackfillAttachmentsAsync(progress, ct);
            await BackfillChunkIndexAsync(progress, ct);
        }
        finally
        {
            _isRunning = false;
        }
    }

    public async Task SyncFolderAsync(
        string folderId, string folderName,
        IProgress<SyncProgress>? progress = null, SyncOptions? options = null, CancellationToken ct = default)
    {
        var state = await _syncStates.GetByFolderIdAsync(folderId, ct)
                 ?? new SyncState { FolderId = folderId, FolderName = folderName };

        state.Status = "syncing";
        await _syncStates.UpsertAsync(state, ct);
        Report(progress, folderName, 0, 0, "Starting sync...");

        try
        {
            var syncOptions = options ?? await LoadSyncOptionsAsync(ct);
            string? deltaLink = state.DeltaLink;
            bool hasMore = true;
            int totalProcessed = 0;

            while (hasMore && !ct.IsCancellationRequested)
            {
                var result = await _mail.SyncFolderAsync(folderId, folderName, deltaLink, syncOptions.SinceUtc, ct);

                // Filter out already-synced emails
                var newEmails = new List<Email>();
                foreach (var email in result.Emails)
                {
                    if (!await _emails.ExistsAsync(email.EmailId, ct))
                        newEmails.Add(email);
                }

                if (newEmails.Count > 0)
                {
                    // Bulk upsert emails
                    await _emails.UpsertBatchAsync(newEmails, ct);
                    _logger.LogInformation("Saved {Count} emails from {Folder}", newEmails.Count, folderName);

                    // Process attachments before embeddings (do not use sync ct — it may be cancelled when sync ends)
                    await _attachmentContext.ProcessBatchAsync(newEmails, CancellationToken.None);

                    await IndexChunksAsync(newEmails, folderName, progress, totalProcessed, ct);
                }

                totalProcessed += result.Emails.Count;
                state.TotalSynced += newEmails.Count;
                state.LastSyncedAt = DateTime.UtcNow;
                state.DeltaLink = result.DeltaLink ?? deltaLink;
                await _syncStates.UpsertAsync(state, ct);

                deltaLink = result.DeltaLink;
                hasMore = result.HasMore && result.NextLink is not null;

                Report(progress, folderName, totalProcessed, totalProcessed, $"Synced {totalProcessed} emails");
            }

            state.Status = "idle";
            state.LastError = null;
            await _syncStates.UpsertAsync(state, ct);
            Report(progress, folderName, totalProcessed, totalProcessed, "Sync complete");
            _logger.LogInformation("Sync complete for {Folder}: {Total} processed", folderName, totalProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for {Folder}", folderName);
            await _syncStates.UpdateStatusAsync(folderId, "error", ex.Message, ct);
            Report(progress, folderName, 0, 0, "Sync failed", ex.Message);
        }
    }

    private async Task IndexChunksAsync(
        IEnumerable<Email> emails, string folderName,
        IProgress<SyncProgress>? progress, int baseCount, CancellationToken ct)
    {
        var list = emails.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            await _chunkIndexing.IndexEmailAsync(list[i].EmailId, ct);
            Report(progress, folderName, baseCount + i + 1, list.Count, $"Indexing {i + 1}/{list.Count}");
        }
    }

    private async Task BackfillChunkIndexAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        Report(progress, "—", 0, 0, "Building search index for emails…");
        await _chunkIndexing.BackfillAsync(AppConstants.ChunkBackfillBatchSize, ct);
    }

    private async Task BackfillAttachmentsAsync(IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        const int batchSize = 25;
        var pending = (await _emails.GetWithUnprocessedAttachmentsAsync(batchSize, ct)).ToList();
        if (pending.Count == 0) return;

        Report(progress, "—", 0, pending.Count, $"Processing {pending.Count} attachment(s) from earlier sync…");
        await _attachmentContext.ProcessBatchAsync(pending, CancellationToken.None);
        _logger.LogInformation("Backfilled attachments for {Count} email(s)", pending.Count);

        // Re-index emails that now have attachment text
        await _chunkIndexing.BackfillAsync(Math.Min(pending.Count, AppConstants.ChunkBackfillBatchSize), ct);
    }

    private async Task<SyncOptions> LoadSyncOptionsAsync(CancellationToken ct)
    {
        var key = await _settings.GetAsync(SettingsKeys.SyncPeriodDays, ct);
        return SyncPeriodOptions.ToOptions(string.IsNullOrWhiteSpace(key) ? SyncPeriodOptions.DefaultKey : key);
    }

    private async Task<bool> ShouldSyncAllFoldersAsync(CancellationToken ct)
    {
        var val = await _settings.GetAsync(SettingsKeys.SyncAllFolders, ct);
        if (string.IsNullOrWhiteSpace(val)) return true;
        return val is "1" or "true" or "True";
    }

    private async Task<HashSet<string>> GetSelectedFolderNamesAsync(CancellationToken ct)
    {
        var json = await _settings.GetAsync(SettingsKeys.SyncFolders, ct);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var folders = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
                if (folders is { Length: > 0 })
                    return MailProviderPresets.NormalizeFolderSelection(folders);
            }
            catch { /* use defaults */ }
        }
        return MailProviderPresets.NormalizeFolderSelection(AppConstants.DefaultFolders);
    }

    private void Report(IProgress<SyncProgress>? p, string folder, int done, int total, string status, string? error = null)
    {
        var prog = new SyncProgress(folder, done, total, status, error);
        p?.Report(prog);
        SyncProgressChanged?.Invoke(this, prog);
    }
}

internal static class EmailExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max];
}

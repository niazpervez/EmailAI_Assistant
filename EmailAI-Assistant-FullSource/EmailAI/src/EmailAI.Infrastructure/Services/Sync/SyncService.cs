using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services.Sync;

public sealed class SyncService : ISyncService
{
    private readonly IMailService _mail;
    private readonly IEmailRepository _emails;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISyncStateRepository _syncStates;
    private readonly IAttachmentRepository _attachments;
    private readonly IAttachmentExtractor _extractor;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<SyncService> _logger;

    private bool _isRunning;

    public event EventHandler<SyncProgress>? SyncProgressChanged;

    public SyncService(
        IMailService mail,
        IEmailRepository emails,
        IEmbeddingRepository embeddings,
        IEmbeddingService embeddingService,
        ISyncStateRepository syncStates,
        IAttachmentRepository attachments,
        IAttachmentExtractor extractor,
        ISettingsRepository settings,
        ILogger<SyncService> logger)
    {
        _mail = mail; _emails = emails; _embeddings = embeddings;
        _embeddingService = embeddingService; _syncStates = syncStates;
        _attachments = attachments; _extractor = extractor;
        _settings = settings; _logger = logger;
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

                    // Generate embeddings for new emails
                    await GenerateEmbeddingsAsync(newEmails, folderName, progress, totalProcessed, ct);

                    // Process attachments in background
                    _ = Task.Run(() => ProcessAttachmentsAsync(newEmails, ct), ct);
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

    private async Task GenerateEmbeddingsAsync(
        IEnumerable<Email> emails, string folderName,
        IProgress<SyncProgress>? progress, int baseCount, CancellationToken ct)
    {
        var list = emails.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var email = list[i];

            if (await _embeddings.ExistsAsync(email.EmailId, ct)) continue;

            try
            {
                var text = $"{email.Subject}\n{email.SenderName}\n{email.BodyText}".Truncate(6000);
                var vector = await _embeddingService.GenerateAsync(text, ct);

                var dbEmail = await _emails.GetByEmailIdAsync(email.EmailId, ct);
                if (dbEmail is null) continue;

                await _embeddings.UpsertAsync(new EmailEmbedding
                {
                    EmailRecordId = dbEmail.Id,
                    EmailId = email.EmailId,
                    Vector = vector,
                    Dimensions = vector.Length,
                    ModelUsed = _embeddingService.ModelName,
                    CreatedAt = DateTime.UtcNow
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding failed for {EmailId}", email.EmailId);
            }

            Report(progress, folderName, baseCount + i + 1, list.Count, $"Embedding {i + 1}/{list.Count}");
        }
    }

    private async Task ProcessAttachmentsAsync(IEnumerable<Email> emails, CancellationToken ct)
    {
        foreach (var email in emails.Where(e => e.HasAttachments))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var dbEmail = await _emails.GetByEmailIdAsync(email.EmailId, ct);
                if (dbEmail is null) continue;

                var attachments = await _mail.GetAttachmentsAsync(email.EmailId, ct);
                foreach (var info in attachments)
                {
                    var attachmentKey = $"{email.EmailId}|{info.Id}";
                    if (await _attachments.ExistsAsync(attachmentKey, ct)) continue;

                    var record = new Attachment
                    {
                        AttachmentId = attachmentKey,
                        EmailRecordId = dbEmail.Id,
                        EmailId = email.EmailId,
                        FileName = info.FileName,
                        ContentType = info.ContentType,
                        SizeBytes = info.SizeBytes,
                        CreatedAt = DateTime.UtcNow
                    };

                    if (_extractor.CanExtract(info.ContentType, info.FileName))
                    {
                        var bytes = await _mail.GetAttachmentContentAsync(email.EmailId, info.Id, ct);
                        if (bytes is not null)
                        {
                            record.ExtractedText = await _extractor.ExtractTextAsync(
                                bytes, info.ContentType, info.FileName, ct);
                            record.IsTextExtracted = !string.IsNullOrWhiteSpace(record.ExtractedText);
                        }
                    }

                    await _attachments.InsertAsync(record, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attachment processing failed for {EmailId}", email.EmailId);
            }
        }
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

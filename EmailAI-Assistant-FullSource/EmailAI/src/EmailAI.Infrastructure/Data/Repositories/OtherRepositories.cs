using Dapper;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;

namespace EmailAI.Infrastructure.Data.Repositories;

// ── Attachment ────────────────────────────────────────────────────────────────

public sealed class AttachmentRepository : IAttachmentRepository
{
    private readonly DatabaseConnectionFactory _factory;
    public AttachmentRepository(DatabaseConnectionFactory factory) => _factory = factory;

    public async Task<Attachment?> GetByAttachmentIdAsync(string attachmentId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<Attachment>(
            "SELECT * FROM Attachments WHERE AttachmentId = @attachmentId", new { attachmentId });
    }

    public async Task<IEnumerable<Attachment>> GetByEmailIdAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Attachment>(
            "SELECT * FROM Attachments WHERE EmailId = @emailId", new { emailId });
    }

    public async Task<int> InsertAsync(Attachment attachment, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            """
            INSERT OR IGNORE INTO Attachments
                (AttachmentId, EmailRecordId, EmailId, FileName, ContentType, SizeBytes, ExtractedText, IsTextExtracted, CreatedAt)
            VALUES
                (@AttachmentId, @EmailRecordId, @EmailId, @FileName, @ContentType, @SizeBytes, @ExtractedText, @IsTextExtracted, @CreatedAt)
            RETURNING Id;
            """, attachment);
    }

    public async Task UpdateExtractedTextAsync(int id, string text, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync(
            "UPDATE Attachments SET ExtractedText = @text, IsTextExtracted = 1 WHERE Id = @id",
            new { text, id });
    }

    public async Task<bool> ExistsAsync(string attachmentId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Attachments WHERE AttachmentId = @attachmentId",
            new { attachmentId }) > 0;
    }
}

// ── SyncState ────────────────────────────────────────────────────────────────

public sealed class SyncStateRepository : ISyncStateRepository
{
    private readonly DatabaseConnectionFactory _factory;
    public SyncStateRepository(DatabaseConnectionFactory factory) => _factory = factory;

    public async Task<SyncState?> GetByFolderIdAsync(string folderId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<SyncState>(
            "SELECT * FROM SyncStates WHERE FolderId = @folderId", new { folderId });
    }

    public async Task<IEnumerable<SyncState>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<SyncState>("SELECT * FROM SyncStates");
    }

    public async Task UpsertAsync(SyncState state, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO SyncStates (FolderId, FolderName, DeltaLink, NextLink, LastSyncedAt, TotalSynced, Status, LastError)
            VALUES (@FolderId, @FolderName, @DeltaLink, @NextLink, @LastSyncedAt, @TotalSynced, @Status, @LastError)
            ON CONFLICT(FolderId) DO UPDATE SET
                FolderName   = excluded.FolderName,
                DeltaLink    = excluded.DeltaLink,
                NextLink     = excluded.NextLink,
                LastSyncedAt = excluded.LastSyncedAt,
                TotalSynced  = excluded.TotalSynced,
                Status       = excluded.Status,
                LastError    = excluded.LastError;
            """, state);
    }

    public async Task UpdateDeltaLinkAsync(string folderId, string? deltaLink, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync(
            "UPDATE SyncStates SET DeltaLink = @deltaLink, LastSyncedAt = @now WHERE FolderId = @folderId",
            new { deltaLink, now = DateTime.UtcNow, folderId });
    }

    public async Task UpdateStatusAsync(string folderId, string status, string? error = null, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync(
            "UPDATE SyncStates SET Status = @status, LastError = @error WHERE FolderId = @folderId",
            new { status, error, folderId });
    }

    public async Task DeleteInvalidAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM SyncStates WHERE FolderId IS NULL OR TRIM(FolderId) = ''");
    }
}

// ── ChatMessages ─────────────────────────────────────────────────────────────

public sealed class ChatRepository : IChatRepository
{
    private readonly DatabaseConnectionFactory _factory;
    public ChatRepository(DatabaseConnectionFactory factory) => _factory = factory;

    public async Task<IEnumerable<ChatMessage>> GetSessionMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<ChatMessage>(
            "SELECT * FROM ChatMessages WHERE SessionId = @sessionId ORDER BY CreatedAt",
            new { sessionId });
    }

    public async Task<IEnumerable<string>> GetSessionIdsAsync(int limit = 20, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<string>(
            """
            SELECT DISTINCT SessionId FROM ChatMessages
            ORDER BY MAX(CreatedAt) DESC LIMIT @limit
            """, new { limit });
    }

    public async Task<int> InsertAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            """
            INSERT INTO ChatMessages (SessionId, Role, Content, CreatedAt, RelevantEmailIds, TokensUsed)
            VALUES (@SessionId, @Role, @Content, @CreatedAt, @RelevantEmailIds, @TokensUsed)
            RETURNING Id;
            """, message);
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM ChatMessages WHERE SessionId = @sessionId", new { sessionId });
    }
}

// ── Settings ──────────────────────────────────────────────────────────────────

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly IEncryptionService _encryption;

    public SettingsRepository(DatabaseConnectionFactory factory, IEncryptionService encryption)
    {
        _factory = factory;
        _encryption = encryption;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var row = await c.QueryFirstOrDefaultAsync<AppSetting>(
            "SELECT * FROM AppSettings WHERE Key = @key", new { key });
        if (row is null) return null;
        return row.IsEncrypted ? _encryption.Decrypt(row.Value) : row.Value;
    }

    public async Task SetAsync(string key, string value, bool encrypt = false, CancellationToken ct = default)
    {
        var stored = encrypt ? _encryption.Encrypt(value) : value;
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO AppSettings (Key, Value, IsEncrypted, UpdatedAt)
            VALUES (@key, @stored, @isEncrypted, @now)
            ON CONFLICT(Key) DO UPDATE SET
                Value       = excluded.Value,
                IsEncrypted = excluded.IsEncrypted,
                UpdatedAt   = excluded.UpdatedAt;
            """,
            new { key, stored, isEncrypted = encrypt ? 1 : 0, now = DateTime.UtcNow });
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var rows = await c.QueryAsync<AppSetting>("SELECT * FROM AppSettings");
        return rows.ToDictionary(
            r => r.Key,
            r => r.IsEncrypted ? _encryption.Decrypt(r.Value) : r.Value);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await c.ExecuteAsync("DELETE FROM AppSettings WHERE Key = @key", new { key });
    }
}

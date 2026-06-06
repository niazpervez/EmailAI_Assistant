using Dapper;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Data.Repositories;

public sealed class EmailRepository : IEmailRepository
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly ILogger<EmailRepository> _logger;

    public EmailRepository(DatabaseConnectionFactory factory, ILogger<EmailRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<Email?> GetByEmailIdAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<Email>(
            "SELECT * FROM Emails WHERE EmailId = @emailId", new { emailId });
    }

    public async Task<bool> ExistsAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var count = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Emails WHERE EmailId = @emailId", new { emailId });
        return count > 0;
    }

    public async Task<IEnumerable<Email>> GetByFolderAsync(string folderName, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            """
            SELECT * FROM Emails WHERE FolderName = @folderName
            ORDER BY ReceivedDate DESC LIMIT @pageSize OFFSET @offset
            """,
            new { folderName, pageSize, offset = (page - 1) * pageSize });
    }

    public async Task<IEnumerable<Email>> GetTodaysEmailsAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        return await c.QueryAsync<Email>(
            "SELECT * FROM Emails WHERE DATE(ReceivedDate) = @today ORDER BY ReceivedDate DESC",
            new { today });
    }

    public async Task<IEnumerable<Email>> GetRecentAsync(int days = 7, int limit = 100, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss");
        return await c.QueryAsync<Email>(
            "SELECT * FROM Emails WHERE ReceivedDate >= @cutoff ORDER BY ReceivedDate DESC LIMIT @limit",
            new { cutoff, limit });
    }

    public async Task<IEnumerable<Email>> SearchByKeywordAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        try
        {
            // FTS5 match
            return await c.QueryAsync<Email>(
                """
                SELECT e.* FROM Emails e
                JOIN EmailsFts f ON e.Id = f.rowid
                WHERE EmailsFts MATCH @keyword
                ORDER BY rank
                LIMIT @limit
                """,
                new { keyword = $"{keyword}*", limit });
        }
        catch
        {
            // Fallback: LIKE search
            return await c.QueryAsync<Email>(
                """
                SELECT * FROM Emails
                WHERE Subject LIKE @kw OR BodyText LIKE @kw OR Sender LIKE @kw
                ORDER BY ReceivedDate DESC LIMIT @limit
                """,
                new { kw = $"%{keyword}%", limit });
        }
    }

    public async Task<IEnumerable<Email>> GetBySenderAsync(string senderEmail, int limit = 50, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            "SELECT * FROM Emails WHERE Sender LIKE @sender ORDER BY ReceivedDate DESC LIMIT @limit",
            new { sender = $"%{senderEmail}%", limit });
    }

    public async Task<IEnumerable<Email>> GetUnreadAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            "SELECT * FROM Emails WHERE IsRead = 0 ORDER BY ReceivedDate DESC LIMIT @limit",
            new { limit });
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Emails");
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Emails WHERE IsRead = 0");
    }

    public async Task<int> GetTodayCountAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Emails WHERE DATE(ReceivedDate) = @today", new { today });
    }

    public async Task<IEnumerable<(string Sender, int Count)>> GetTopSendersAsync(int limit = 10, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var rows = await c.QueryAsync<(string Sender, int Count)>(
            """
            SELECT Sender, COUNT(*) as Count FROM Emails
            WHERE FolderName = 'Inbox'
            GROUP BY Sender ORDER BY Count DESC LIMIT @limit
            """,
            new { limit });
        return rows;
    }

    public async Task<IEnumerable<Email>> GetByIdsAsync(IEnumerable<string> emailIds, CancellationToken ct = default)
    {
        var ids = emailIds.ToList();
        if (ids.Count == 0) return Enumerable.Empty<Email>();
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            "SELECT * FROM Emails WHERE EmailId IN @ids", new { ids });
    }

    public async Task<int> UpsertAsync(Email email, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            """
            INSERT INTO Emails
                (EmailId, ConversationId, Subject, Sender, SenderName, Recipients,
                 ReceivedDate, BodyText, BodyHtml, FolderName, FolderId,
                 HasAttachments, IsRead, IsImportant, Importance, SyncedAt, ChangeKey)
            VALUES
                (@EmailId, @ConversationId, @Subject, @Sender, @SenderName, @Recipients,
                 @ReceivedDate, @BodyText, @BodyHtml, @FolderName, @FolderId,
                 @HasAttachments, @IsRead, @IsImportant, @Importance, @SyncedAt, @ChangeKey)
            ON CONFLICT(EmailId) DO UPDATE SET
                Subject        = excluded.Subject,
                IsRead         = excluded.IsRead,
                SyncedAt       = excluded.SyncedAt,
                ChangeKey      = excluded.ChangeKey
            RETURNING Id;
            """,
            email);
    }

    public async Task UpsertBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default)
    {
        var list = emails.ToList();
        if (list.Count == 0) return;

        await using var c = await _factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var email in list)
        {
            await c.ExecuteAsync(
                """
                INSERT INTO Emails
                    (EmailId, ConversationId, Subject, Sender, SenderName, Recipients,
                     ReceivedDate, BodyText, BodyHtml, FolderName, FolderId,
                     HasAttachments, IsRead, IsImportant, Importance, SyncedAt, ChangeKey)
                VALUES
                    (@EmailId, @ConversationId, @Subject, @Sender, @SenderName, @Recipients,
                     @ReceivedDate, @BodyText, @BodyHtml, @FolderName, @FolderId,
                     @HasAttachments, @IsRead, @IsImportant, @Importance, @SyncedAt, @ChangeKey)
                ON CONFLICT(EmailId) DO UPDATE SET
                    Subject  = excluded.Subject,
                    IsRead   = excluded.IsRead,
                    SyncedAt = excluded.SyncedAt,
                    ChangeKey= excluded.ChangeKey;
                """,
                email, tx);
        }

        await tx.CommitAsync(ct);
        _logger.LogDebug("Upserted batch of {Count} emails", list.Count);
    }
}

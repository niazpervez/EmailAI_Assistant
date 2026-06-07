using Dapper;
using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Core.Models;
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
            var ftsQuery = BuildFtsQuery(keyword);
            return await c.QueryAsync<Email>(
                """
                SELECT e.* FROM Emails e
                JOIN EmailsFts f ON e.Id = f.rowid
                WHERE EmailsFts MATCH @keyword
                ORDER BY rank
                LIMIT @limit
                """,
                new { keyword = ftsQuery, limit });
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

    private static string BuildFtsQuery(string keyword)
    {
        var terms = keyword
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeFtsTerm)
            .Where(t => t.Length > 0)
            .Select(t => $"{t}*")
            .ToList();

        return terms.Count switch
        {
            0 => keyword,
            1 => terms[0],
            _ => string.Join(" OR ", terms)
        };
    }

    private static string SanitizeFtsTerm(string term)
    {
        var cleaned = new string(term.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return cleaned.Trim('-', '_');
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
                (EmailId, ConversationId, MessageId, Subject, Sender, SenderName, Recipients,
                 ReceivedDate, BodyText, BodyHtml, FolderName, FolderId,
                 HasAttachments, IsRead, IsImportant, Importance, SyncedAt, ChangeKey)
            VALUES
                (@EmailId, @ConversationId, @MessageId, @Subject, @Sender, @SenderName, @Recipients,
                 @ReceivedDate, @BodyText, @BodyHtml, @FolderName, @FolderId,
                 @HasAttachments, @IsRead, @IsImportant, @Importance, @SyncedAt, @ChangeKey)
            ON CONFLICT(EmailId) DO UPDATE SET
                Subject        = excluded.Subject,
                MessageId      = CASE WHEN excluded.MessageId != '' THEN excluded.MessageId ELSE MessageId END,
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
                    (EmailId, ConversationId, MessageId, Subject, Sender, SenderName, Recipients,
                     ReceivedDate, BodyText, BodyHtml, FolderName, FolderId,
                     HasAttachments, IsRead, IsImportant, Importance, SyncedAt, ChangeKey)
                VALUES
                    (@EmailId, @ConversationId, @MessageId, @Subject, @Sender, @SenderName, @Recipients,
                     @ReceivedDate, @BodyText, @BodyHtml, @FolderName, @FolderId,
                     @HasAttachments, @IsRead, @IsImportant, @Importance, @SyncedAt, @ChangeKey)
                ON CONFLICT(EmailId) DO UPDATE SET
                    Subject   = excluded.Subject,
                    MessageId = CASE WHEN excluded.MessageId != '' THEN excluded.MessageId ELSE MessageId END,
                    IsRead    = excluded.IsRead,
                    SyncedAt  = excluded.SyncedAt,
                    ChangeKey = excluded.ChangeKey;
                """,
                email, tx);
        }

        await tx.CommitAsync(ct);
        _logger.LogDebug("Upserted batch of {Count} emails", list.Count);
    }

    public async Task<IEnumerable<Email>> GetWithUnprocessedAttachmentsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            """
            SELECT e.* FROM Emails e
            WHERE e.HasAttachments = 1
              AND NOT EXISTS (SELECT 1 FROM Attachments a WHERE a.EmailId = e.EmailId)
            ORDER BY e.ReceivedDate DESC
            LIMIT @limit
            """,
            new { limit });
    }

    public async Task<IEnumerable<Email>> GetByConversationIdAsync(
        string conversationId, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return [];

        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<Email>(
            """
            SELECT * FROM Emails
            WHERE ConversationId = @conversationId
            ORDER BY ReceivedDate ASC
            LIMIT @limit
            """,
            new { conversationId, limit });
    }

    public async Task<IEnumerable<FolderSummary>> GetFolderSummariesAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<FolderSummary>(
            """
            SELECT FolderName,
                   COUNT(*) AS TotalCount,
                   SUM(CASE WHEN IsRead = 0 THEN 1 ELSE 0 END) AS UnreadCount
            FROM Emails
            GROUP BY FolderName
            ORDER BY FolderName
            """);
    }

    public async Task<IEnumerable<Email>> GetRecentSentAsync(int days = 14, int limit = 100, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss");
        return await c.QueryAsync<Email>(
            """
            SELECT * FROM Emails
            WHERE FolderName = 'Sent' AND ReceivedDate >= @cutoff
            ORDER BY ReceivedDate DESC
            LIMIT @limit
            """,
            new { cutoff, limit });
    }

    public async Task<Email?> FindFirstReplyToAsync(Email sentEmail, CancellationToken ct = default)
    {
        var threadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sentEmail.MessageId))
            threadIds.Add(sentEmail.MessageId.Trim());
        if (!string.IsNullOrWhiteSpace(sentEmail.ConversationId))
            threadIds.Add(sentEmail.ConversationId.Trim());

        var sentDate = sentEmail.ReceivedDate.ToString("yyyy-MM-ddTHH:mm:ss");
        await using var c = await _factory.OpenAsync(ct);

        if (threadIds.Count > 0)
        {
            var byThread = await c.QueryFirstOrDefaultAsync<Email>(
                """
                SELECT * FROM Emails
                WHERE FolderName != 'Sent'
                  AND ReceivedDate > @sentDate
                  AND ConversationId IN @threadIds
                ORDER BY ReceivedDate ASC
                LIMIT 1
                """,
                new { sentDate, threadIds = threadIds.ToList() });
            if (byThread is not null) return byThread;
        }

        var normSubject = SentFollowUpHelper.NormalizeSubject(sentEmail.Subject);
        if (string.IsNullOrWhiteSpace(normSubject)) return null;

        var recipients = SentFollowUpHelper.ParseRecipientEmails(sentEmail.Recipients);
        if (recipients.Count == 0) return null;

        var candidates = (await c.QueryAsync<Email>(
            """
            SELECT * FROM Emails
            WHERE FolderName != 'Sent'
              AND ReceivedDate > @sentDate
              AND (Subject LIKE @exactSubject OR Subject LIKE @reSubject)
            ORDER BY ReceivedDate ASC
            LIMIT 20
            """,
            new
            {
                sentDate,
                exactSubject = normSubject,
                reSubject = $"Re: {normSubject}%"
            })).ToList();

        return candidates.FirstOrDefault(e =>
            recipients.Any(r =>
                e.Sender.Equals(r, StringComparison.OrdinalIgnoreCase)
                || e.Sender.Contains(r, StringComparison.OrdinalIgnoreCase)));
    }
}

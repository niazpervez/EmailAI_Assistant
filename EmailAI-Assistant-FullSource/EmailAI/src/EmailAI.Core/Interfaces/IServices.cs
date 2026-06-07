using EmailAI.Core.Entities;
using EmailAI.Core.DTOs;

namespace EmailAI.Core.Interfaces;

// ── Email (IMAP/SMTP) ────────────────────────────────────────────────────────

public interface IMailService
{
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);
    Task<string> GetUserDisplayNameAsync(CancellationToken ct = default);
    Task<string> GetUserEmailAsync(CancellationToken ct = default);
    Task SaveAccountAsync(MailAccountConfig config, CancellationToken ct = default);
    Task<MailAccountConfig?> GetSavedAccountAsync(CancellationToken ct = default);
    /// <summary>Validates IMAP credentials, then saves the account on success.</summary>
    Task ConnectAccountAsync(MailAccountConfig config, CancellationToken ct = default);
    /// <summary>Opens the system browser for Google or Microsoft OAuth sign-in.</summary>
    Task ConnectOAuthAsync(MailProvider provider, CancellationToken ct = default);
    Task SignInAsync(CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    Task<IEnumerable<MailFolder>> GetMailFoldersAsync(CancellationToken ct = default);
    Task<SyncResult> SyncFolderAsync(string folderId, string folderName, string? deltaLink, DateTime? sinceUtc = null, CancellationToken ct = default);
    Task<byte[]?> GetAttachmentContentAsync(string messageId, string attachmentId, CancellationToken ct = default);
    Task<IEnumerable<MailAttachmentInfo>> GetAttachmentsAsync(string messageId, CancellationToken ct = default);
    Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default);
    Task<bool> ReplyToEmailAsync(string messageId, string body, CancellationToken ct = default);
}

public record MailFolder(string Id, string Name, string DisplayName, int TotalCount, int UnreadCount);
public record MailAttachmentInfo(string Id, string FileName, string ContentType, long SizeBytes);
public record SyncResult(List<Email> Emails, string? DeltaLink, string? NextLink, bool HasMore);

// ── Embedding ────────────────────────────────────────────────────────────────

public interface IEmbeddingService
{
    Task<float[]> GenerateAsync(string text, CancellationToken ct = default);
    int Dimensions { get; }
    string ModelName { get; }
}

// ── AI / DeepSeek ────────────────────────────────────────────────────────────

public interface IAIService
{
    Task<string> ChatAsync(string userMessage, IEnumerable<Email> contextEmails, IEnumerable<ChatMessage> history, CancellationToken ct = default);
    Task<string> SummarizeEmailsAsync(IEnumerable<Email> emails, string summaryType, CancellationToken ct = default);
    Task<string> GenerateReplyAsync(Email email, string replyType, string? userInstructions = null, CancellationToken ct = default);
    Task<string> ExtractActionItemsAsync(IEnumerable<Email> emails, string? supplementaryContext = null, CancellationToken ct = default);
}

// ── Sync ─────────────────────────────────────────────────────────────────────

public interface ISyncService
{
    Task SyncAllFoldersAsync(IProgress<SyncProgress>? progress = null, SyncOptions? options = null, CancellationToken ct = default);
    Task SyncFolderAsync(string folderId, string folderName, IProgress<SyncProgress>? progress = null, SyncOptions? options = null, CancellationToken ct = default);
    Task<bool> IsRunningAsync();
    event EventHandler<SyncProgress>? SyncProgressChanged;
}

public record SyncProgress(string FolderName, int Processed, int Total, string Status, string? Error = null);

// ── Attachment Extraction ─────────────────────────────────────────────────────

public interface IAttachmentExtractor
{
    Task<string> ExtractTextAsync(byte[] content, string contentType, string fileName, CancellationToken ct = default);
    bool CanExtract(string contentType, string fileName);
}

public interface IAttachmentContextService
{
    Task EnsureProcessedAsync(string emailId, CancellationToken ct = default);
    Task ProcessBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default);
    Task<string> GetContextForEmailAsync(string emailId, CancellationToken ct = default);
    Task<IEnumerable<Email>> EnrichForChatAsync(IEnumerable<Email> emails, CancellationToken ct = default);
}

// ── Search ───────────────────────────────────────────────────────────────────

public interface ISearchService
{
    Task<IEnumerable<Email>> SearchAsync(string query, SearchMode mode = SearchMode.Hybrid, int limit = 20, CancellationToken ct = default);
}

public enum SearchMode { Keyword, Semantic, Hybrid }

public interface ITextChunker
{
    IEnumerable<(int Index, string Source, string Content)> BuildChunks(Email email, string? attachmentText);
}

public interface IChunkIndexingService
{
    Task IndexEmailAsync(string emailId, CancellationToken ct = default);
    Task IndexBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default);
    Task BackfillAsync(int batchSize = 50, CancellationToken ct = default);
}

public interface IRagSearchService
{
    Task<RagSearchResult> SearchAsync(string query, int emailLimit = 15, CancellationToken ct = default);
}

// ── External mail client import ───────────────────────────────────────────────

public interface IExternalMailImportService
{
    Task<InstalledMailClientsInfo> DetectInstalledClientsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiscoveredMailAccount>> DiscoverAccountsAsync(
        ExternalMailClient? client = null, CancellationToken ct = default);
    MailAccountConfig ToMailAccountConfig(DiscoveredMailAccount account);
}

// ── Security ─────────────────────────────────────────────────────────────────

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

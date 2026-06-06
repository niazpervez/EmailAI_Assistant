using EmailAI.Core.Entities;

namespace EmailAI.Core.Interfaces;

public interface IAttachmentRepository
{
    Task<Attachment?> GetByAttachmentIdAsync(string attachmentId, CancellationToken ct = default);
    Task<IEnumerable<Attachment>> GetByEmailIdAsync(string emailId, CancellationToken ct = default);
    Task<int> InsertAsync(Attachment attachment, CancellationToken ct = default);
    Task UpdateExtractedTextAsync(int id, string text, CancellationToken ct = default);
    Task<bool> ExistsAsync(string attachmentId, CancellationToken ct = default);
}

public interface IEmbeddingRepository
{
    Task<EmailEmbedding?> GetByEmailIdAsync(string emailId, CancellationToken ct = default);
    Task UpsertAsync(EmailEmbedding embedding, CancellationToken ct = default);
    Task<bool> ExistsAsync(string emailId, CancellationToken ct = default);
    Task<IEnumerable<string>> FindSimilarEmailIdsAsync(float[] queryVector, int topK = 10, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
}

public interface ISyncStateRepository
{
    Task<SyncState?> GetByFolderIdAsync(string folderId, CancellationToken ct = default);
    Task<IEnumerable<SyncState>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(SyncState state, CancellationToken ct = default);
    Task UpdateDeltaLinkAsync(string folderId, string? deltaLink, CancellationToken ct = default);
    Task UpdateStatusAsync(string folderId, string status, string? error = null, CancellationToken ct = default);
    Task DeleteInvalidAsync(CancellationToken ct = default);
}

public interface IChatRepository
{
    Task<IEnumerable<ChatMessage>> GetSessionMessagesAsync(string sessionId, CancellationToken ct = default);
    Task<IEnumerable<string>> GetSessionIdsAsync(int limit = 20, CancellationToken ct = default);
    Task<int> InsertAsync(ChatMessage message, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
}

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, bool encrypt = false, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

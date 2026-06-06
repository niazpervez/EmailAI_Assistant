using EmailAI.Core.Entities;

namespace EmailAI.Core.Interfaces;

public interface IEmailRepository
{
    Task<Email?> GetByEmailIdAsync(string emailId, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetByFolderAsync(string folderName, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetTodaysEmailsAsync(CancellationToken ct = default);
    Task<IEnumerable<Email>> SearchByKeywordAsync(string keyword, int limit = 20, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetBySenderAsync(string senderEmail, int limit = 50, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetUnreadAsync(int limit = 100, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task<int> GetTodayCountAsync(CancellationToken ct = default);
    Task<IEnumerable<(string Sender, int Count)>> GetTopSendersAsync(int limit = 10, CancellationToken ct = default);
    Task<bool> ExistsAsync(string emailId, CancellationToken ct = default);
    Task<int> UpsertAsync(Email email, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetByIdsAsync(IEnumerable<string> emailIds, CancellationToken ct = default);
    Task<IEnumerable<Email>> GetRecentAsync(int days = 7, int limit = 100, CancellationToken ct = default);
}

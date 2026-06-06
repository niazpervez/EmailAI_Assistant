using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;

namespace EmailAI.Application.Services;

public sealed class DashboardService
{
    private readonly IEmailRepository _emails;
    private readonly IEmbeddingRepository _embeddings;
    private readonly ISyncStateRepository _syncStates;
    private readonly IAIService _ai;

    public DashboardService(
        IEmailRepository emails,
        IEmbeddingRepository embeddings,
        ISyncStateRepository syncStates,
        IAIService ai)
    {
        _emails = emails; _embeddings = embeddings;
        _syncStates = syncStates; _ai = ai;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var totalTask   = _emails.GetTotalCountAsync(ct);
        var unreadTask  = _emails.GetUnreadCountAsync(ct);
        var todayTask   = _emails.GetTodayCountAsync(ct);
        var embedTask   = _embeddings.GetTotalCountAsync(ct);
        var topTask     = _emails.GetTopSendersAsync(5, ct);
        var syncTask    = _syncStates.GetAllAsync(ct);

        await Task.WhenAll(totalTask, unreadTask, todayTask, embedTask, topTask, syncTask);

        var syncStates = (await syncTask).ToList();
        var lastSync = syncStates
            .Where(s => s.LastSyncedAt.HasValue)
            .Max(s => s.LastSyncedAt);

        // Action items from today's emails (AI-generated, cached)
        string actionItems = "No action items. Sync emails first.";
        try
        {
            var todaysEmails = await _emails.GetTodaysEmailsAsync(ct);
            var emailList = todaysEmails.Take(10).ToList();
            if (emailList.Count > 0)
                actionItems = await _ai.ExtractActionItemsAsync(emailList, ct);
        }
        catch { /* AI unavailable */ }

        return new DashboardDto(
            await totalTask,
            await unreadTask,
            await todayTask,
            await embedTask,
            await topTask,
            actionItems,
            lastSync
        );
    }
}

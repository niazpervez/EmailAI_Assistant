using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;

namespace EmailAI.Application.Services;

public sealed class DashboardService
{
    private readonly IEmailRepository _emails;
    private readonly IChunkRepository _chunks;
    private readonly ISyncStateRepository _syncStates;
    private readonly IAIService _ai;
    private readonly SentFollowUpService _followUps;

    public DashboardService(
        IEmailRepository emails,
        IChunkRepository chunks,
        ISyncStateRepository syncStates,
        IAIService ai,
        SentFollowUpService followUps)
    {
        _emails = emails;
        _chunks = chunks;
        _syncStates = syncStates;
        _ai = ai;
        _followUps = followUps;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var totalTask   = _emails.GetTotalCountAsync(ct);
        var unreadTask  = _emails.GetUnreadCountAsync(ct);
        var todayTask   = _emails.GetTodayCountAsync(ct);
        var embedTask   = _chunks.GetIndexedEmailCountAsync(ct);
        var topTask     = _emails.GetTopSendersAsync(5, ct);
        var syncTask    = _syncStates.GetAllAsync(ct);
        var followUpTask = _followUps.AnalyzeAsync(ct: ct);

        await Task.WhenAll(totalTask, unreadTask, todayTask, embedTask, topTask, syncTask, followUpTask);

        var syncStates = (await syncTask).ToList();
        var lastSync = syncStates
            .Where(s => s.LastSyncedAt.HasValue)
            .Max(s => s.LastSyncedAt);

        var sentFollowUps = (await followUpTask).ToList();
        var awaiting = sentFollowUps.Count(f => !f.HasReply);
        var replied = sentFollowUps.Count(f => f.HasReply);

        string actionItems = "No action items. Sync emails first.";
        try
        {
            var todaysEmails = (await _emails.GetTodaysEmailsAsync(ct)).Take(10).ToList();
            var recentSent = (await _emails.GetRecentSentAsync(7, 10, ct))
                .Where(SentFollowUpHelper.LooksLikeFollowUp)
                .Take(5)
                .ToList();

            var contextEmails = todaysEmails
                .Concat(recentSent)
                .GroupBy(e => e.EmailId)
                .Select(g => g.First())
                .Take(15)
                .ToList();

            if (contextEmails.Count > 0)
            {
                var followUpContext = SentFollowUpHelper.BuildAiContext(sentFollowUps);
                actionItems = await _ai.ExtractActionItemsAsync(contextEmails, followUpContext, ct);
            }
            else if (sentFollowUps.Count > 0)
            {
                actionItems = BuildFollowUpOnlyActions(sentFollowUps);
            }
        }
        catch { /* AI unavailable */ }

        return new DashboardDto(
            await totalTask,
            await unreadTask,
            await todayTask,
            await embedTask,
            await topTask,
            actionItems,
            lastSync,
            sentFollowUps,
            awaiting,
            replied
        );
    }

    private static string BuildFollowUpOnlyActions(IReadOnlyList<SentFollowUpItemDto> followUps)
    {
        var awaiting = followUps.Where(f => !f.HasReply).Take(8).ToList();
        if (awaiting.Count == 0)
            return "All recent sent follow-ups have received replies.";

        var lines = awaiting.Select((f, i) =>
            $"{i + 1}. **Action:** Follow up again — \"{f.Subject}\" sent to {f.Recipient} on {f.SentDate:MMM d} (no reply yet)\n" +
            $"- **Responsible:** You\n- **Deadline:** As soon as possible");

        return string.Join("\n\n", lines);
    }
}

using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;

namespace EmailAI.Application.Services;

public sealed class SentFollowUpService
{
    private readonly IEmailRepository _emails;

    public SentFollowUpService(IEmailRepository emails) => _emails = emails;

    public async Task<IReadOnlyList<SentFollowUpItemDto>> AnalyzeAsync(
        int lookbackDays = 14, CancellationToken ct = default)
    {
        var sentEmails = (await _emails.GetRecentSentAsync(lookbackDays, 150, ct)).ToList();
        var results = new List<SentFollowUpItemDto>();

        foreach (var sent in sentEmails)
        {
            if (!SentFollowUpHelper.LooksLikeFollowUp(sent)) continue;

            var reply = await _emails.FindFirstReplyToAsync(sent, ct);
            var category = SentFollowUpHelper.DetectCategory(sent.Subject, sent.BodyText);
            var recipient = SentFollowUpHelper.FormatRecipientDisplay(sent.Recipients);

            results.Add(new SentFollowUpItemDto(
                sent.EmailId,
                sent.Subject,
                recipient,
                sent.ReceivedDate,
                reply is not null,
                reply?.ReceivedDate,
                reply?.SenderName ?? reply?.Sender,
                category,
                reply is not null ? "Replied" : "Awaiting reply"));
        }

        return results
            .OrderByDescending(r => r.SentDate)
            .Take(25)
            .ToList();
    }
}

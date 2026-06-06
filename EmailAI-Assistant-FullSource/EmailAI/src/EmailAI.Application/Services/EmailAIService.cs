using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;

namespace EmailAI.Application.Services;

public sealed class EmailAIService
{
    private readonly IAIService _ai;
    private readonly IEmailRepository _emails;
    private readonly IMailService _mail;

    public EmailAIService(IAIService ai, IEmailRepository emails, IMailService mail)
    {
        _ai = ai; _emails = emails; _mail = mail;
    }

    public async Task<string> GenerateReplyAsync(
        string emailId, string replyType, string? userInstructions = null, CancellationToken ct = default)
    {
        var email = await _emails.GetByEmailIdAsync(emailId, ct)
            ?? throw new InvalidOperationException($"Email {emailId} not found");
        return await _ai.GenerateReplyAsync(email, replyType, userInstructions, ct);
    }

    public async Task<bool> SendReplyAsync(string emailId, string replyBody, CancellationToken ct = default)
        => await _mail.ReplyToEmailAsync(emailId, replyBody, ct);

    public async Task<string> GetDailySummaryAsync(CancellationToken ct = default)
    {
        var emails = await _emails.GetTodaysEmailsAsync(ct);
        return await _ai.SummarizeEmailsAsync(emails, "daily", ct);
    }

    public async Task<string> GetWeeklySummaryAsync(CancellationToken ct = default)
    {
        var emails = await _emails.GetRecentAsync(7, 100, ct);
        return await _ai.SummarizeEmailsAsync(emails, "weekly", ct);
    }

    public async Task<string> GetCustomerSummaryAsync(string senderEmail, CancellationToken ct = default)
    {
        var emails = await _emails.GetBySenderAsync(senderEmail, 50, ct);
        return await _ai.SummarizeEmailsAsync(emails, "customer", ct);
    }
}

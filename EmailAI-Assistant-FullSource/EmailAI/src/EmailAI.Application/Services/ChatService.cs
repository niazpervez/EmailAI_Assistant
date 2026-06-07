using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EmailAI.Application.Services;

public sealed class ChatService
{
    private readonly IAIService _ai;
    private readonly IEmailRepository _emails;
    private readonly IChatRepository _chat;
    private readonly IRagSearchService _ragSearch;
    private readonly IAttachmentContextService _attachmentContext;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAIService ai,
        IEmailRepository emails,
        IChatRepository chat,
        IRagSearchService ragSearch,
        IAttachmentContextService attachmentContext,
        ILogger<ChatService> logger)
    {
        _ai = ai;
        _emails = emails;
        _chat = chat;
        _ragSearch = ragSearch;
        _attachmentContext = attachmentContext;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(AppConstants.ChatTimeoutSeconds));

        var token = timeoutCts.Token;
        var history = (await _chat.GetSessionMessagesAsync(request.SessionId, token)).ToList();

        var intent = ChatQueryRouter.DetectIntent(request.Message);
        _logger.LogInformation("Chat intent: {Intent} for query", intent);

        string aiResponse;
        IEnumerable<string> sourceIds;

        if (intent != ChatQueryIntent.General)
        {
            (aiResponse, sourceIds) = await HandleSummaryIntentAsync(intent, request.Message, history, token);
        }
        else
        {
            (aiResponse, sourceIds) = await HandleRagQueryAsync(request, history, token);
        }

        var userMsg = new ChatMessage
        {
            SessionId = request.SessionId,
            Role = "user",
            Content = request.Message,
            CreatedAt = DateTime.UtcNow
        };
        await _chat.InsertAsync(userMsg, token);

        var assistantMsg = new ChatMessage
        {
            SessionId = request.SessionId,
            Role = "assistant",
            Content = aiResponse,
            CreatedAt = DateTime.UtcNow,
            RelevantEmailIds = JsonSerializer.Serialize(sourceIds)
        };
        await _chat.InsertAsync(assistantMsg, token);

        return new ChatResponse(aiResponse, request.SessionId, sourceIds);
    }

    private async Task<(string Response, IEnumerable<string> SourceIds)> HandleSummaryIntentAsync(
        ChatQueryIntent intent, string _, List<ChatMessage> history, CancellationToken ct)
    {
        List<Email> emails = intent switch
        {
            ChatQueryIntent.TodaySummary => (await _emails.GetTodaysEmailsAsync(ct)).Take(AppConstants.MaxSummaryEmails).ToList(),
            ChatQueryIntent.WeekSummary => (await _emails.GetRecentAsync(7, AppConstants.MaxSummaryEmails, ct)).ToList(),
            ChatQueryIntent.UnreadSummary => (await _emails.GetUnreadAsync(AppConstants.MaxSummaryEmails, ct)).ToList(),
            _ => []
        };

        if (emails.Count == 0)
        {
            var empty = intent switch
            {
                ChatQueryIntent.TodaySummary => "No emails received today in your synced mailbox.",
                ChatQueryIntent.WeekSummary => "No emails found in the last 7 days.",
                ChatQueryIntent.UnreadSummary => "No unread emails in your synced mailbox.",
                _ => "No matching emails found."
            };
            return (empty, []);
        }

        var enriched = (await _attachmentContext.EnrichForChatAsync(emails, ct)).ToList();
        var summaryType = intent switch
        {
            ChatQueryIntent.WeekSummary => "weekly",
            _ => "daily"
        };

        _logger.LogInformation("Direct summary path: {Count} emails ({Intent})", enriched.Count, intent);
        var response = await _ai.SummarizeEmailsAsync(enriched, summaryType, ct);
        return (response, enriched.Select(e => e.EmailId));
    }

    private async Task<(string Response, IEnumerable<string> SourceIds)> HandleRagQueryAsync(
        ChatRequest request, List<ChatMessage> history, CancellationToken ct)
    {
        var topK = request.TopK > 0 ? request.TopK : AppConstants.DefaultTopK;
        var rag = await _ragSearch.SearchAsync(request.Message, topK, ct);
        _logger.LogInformation("RAG retrieved {Count} emails for chat", rag.Emails.Count);

        var enriched = (await _attachmentContext.EnrichForChatAsync(rag.Emails, ct)).ToList();
        var contextEmails = ApplyExcerpts(enriched, rag.SnippetsByEmailId);
        var aiResponse = await _ai.ChatAsync(request.Message, contextEmails, history, ct);
        var sourceIds = rag.Emails.Select(e => e.EmailId).Distinct().Take(topK);
        return (aiResponse, sourceIds);
    }

    public async Task<IEnumerable<ChatMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
        => await _chat.GetSessionMessagesAsync(sessionId, ct);

    public async Task<IEnumerable<string>> GetSessionsAsync(CancellationToken ct = default)
        => await _chat.GetSessionIdsAsync(20, ct);

    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
        => await _chat.DeleteSessionAsync(sessionId, ct);

    public string NewSession() => Guid.NewGuid().ToString();

    private static List<Email> ApplyExcerpts(
        IEnumerable<Email> emails, IReadOnlyDictionary<string, string> snippets)
    {
        var result = new List<Email>();
        foreach (var email in emails)
        {
            if (!snippets.TryGetValue(email.EmailId, out var excerpt))
            {
                result.Add(email);
                continue;
            }

            result.Add(new Email
            {
                Id = email.Id,
                EmailId = email.EmailId,
                ConversationId = email.ConversationId,
                Subject = email.Subject,
                Sender = email.Sender,
                SenderName = email.SenderName,
                Recipients = email.Recipients,
                ReceivedDate = email.ReceivedDate,
                BodyText = $"Most relevant excerpt:\n{excerpt}\n\nFull content:\n{Truncate(email.BodyText, 2000)}",
                BodyHtml = email.BodyHtml,
                FolderName = email.FolderName,
                FolderId = email.FolderId,
                HasAttachments = email.HasAttachments,
                IsRead = email.IsRead,
                IsImportant = email.IsImportant,
                Importance = email.Importance,
                SyncedAt = email.SyncedAt,
                ChangeKey = email.ChangeKey
            });
        }

        return result;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

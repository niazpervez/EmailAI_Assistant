using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EmailAI.Application.Services;

public sealed class ChatService
{
    private readonly IAIService _ai;
    private readonly IEmbeddingService _embeddingService;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmailRepository _emails;
    private readonly IChatRepository _chat;
    private readonly ISearchService _search;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAIService ai,
        IEmbeddingService embeddingService,
        IEmbeddingRepository embeddings,
        IEmailRepository emails,
        IChatRepository chat,
        ISearchService search,
        ILogger<ChatService> logger)
    {
        _ai = ai; _embeddingService = embeddingService; _embeddings = embeddings;
        _emails = emails; _chat = chat; _search = search; _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken ct = default)
    {
        // 1. Load conversation history
        var history = (await _chat.GetSessionMessagesAsync(request.SessionId, ct)).ToList();

        // 2. RAG: find relevant emails via hybrid search
        var relevantEmails = (await _search.SearchAsync(request.Message, SearchMode.Hybrid, request.TopK, ct)).ToList();
        _logger.LogDebug("RAG retrieved {Count} relevant emails for query: {Query}", relevantEmails.Count, request.Message);

        // 3. Call AI with context
        var aiResponse = await _ai.ChatAsync(request.Message, relevantEmails, history, ct);

        // 4. Persist user message
        var userMsg = new ChatMessage
        {
            SessionId = request.SessionId,
            Role = "user",
            Content = request.Message,
            CreatedAt = DateTime.UtcNow
        };
        await _chat.InsertAsync(userMsg, ct);

        // 5. Persist assistant response
        var assistantMsg = new ChatMessage
        {
            SessionId = request.SessionId,
            Role = "assistant",
            Content = aiResponse,
            CreatedAt = DateTime.UtcNow,
            RelevantEmailIds = JsonSerializer.Serialize(relevantEmails.Select(e => e.EmailId))
        };
        await _chat.InsertAsync(assistantMsg, ct);

        return new ChatResponse(
            aiResponse,
            request.SessionId,
            relevantEmails.Select(e => e.EmailId));
    }

    public async Task<IEnumerable<ChatMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
        => await _chat.GetSessionMessagesAsync(sessionId, ct);

    public async Task<IEnumerable<string>> GetSessionsAsync(CancellationToken ct = default)
        => await _chat.GetSessionIdsAsync(20, ct);

    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
        => await _chat.DeleteSessionAsync(sessionId, ct);

    public string NewSession() => Guid.NewGuid().ToString();
}

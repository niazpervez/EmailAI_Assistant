using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services;

public sealed class RagSearchService : IRagSearchService
{
    private readonly IEmailRepository _emails;
    private readonly IChunkRepository _chunks;
    private readonly IAttachmentRepository _attachments;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISearchService _search;
    private readonly ILogger<RagSearchService> _logger;

    public RagSearchService(
        IEmailRepository emails,
        IChunkRepository chunks,
        IAttachmentRepository attachments,
        IEmbeddingService embeddingService,
        ISearchService search,
        ILogger<RagSearchService> logger)
    {
        _emails = emails;
        _chunks = chunks;
        _attachments = attachments;
        _embeddingService = embeddingService;
        _search = search;
        _logger = logger;
    }

    public async Task<RagSearchResult> SearchAsync(string query, int emailLimit = 15, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RagSearchResult([], new Dictionary<string, string>());

        emailLimit = emailLimit > 0 ? emailLimit : AppConstants.DefaultTopK;
        var chunkLimit = AppConstants.ChunkSearchTopK;

        var queryVector = await _embeddingService.GenerateAsync(query, ct);

        var semanticTask = _chunks.FindSimilarChunksAsync(queryVector, chunkLimit, ct);
        var chunkKeywordTask = _chunks.SearchByKeywordAsync(query, chunkLimit, ct);
        var emailKeywordTask = _search.SearchAsync(query, SearchMode.Keyword, emailLimit, ct);
        var attachmentIdsTask = _attachments.SearchEmailIdsByExtractedTextAsync(query, emailLimit, ct);

        await Task.WhenAll(semanticTask, chunkKeywordTask, emailKeywordTask, attachmentIdsTask);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var snippets = new Dictionary<string, (double Score, string Snippet)>(StringComparer.Ordinal);

        void AddHit(string emailId, double score, string snippet, string source)
        {
            scores[emailId] = scores.GetValueOrDefault(emailId) + score;
            if (!snippets.TryGetValue(emailId, out var existing) || score > existing.Score)
                snippets[emailId] = (score, $"[{source}] {snippet}");
        }

        foreach (var hit in await semanticTask)
            AddHit(hit.EmailId, hit.Score * 1.0, hit.Snippet, hit.Source);

        foreach (var hit in await chunkKeywordTask)
            AddHit(hit.EmailId, hit.Score * 0.85, hit.Snippet, hit.Source);

        foreach (var email in await emailKeywordTask)
        {
            AddHit(email.EmailId, 0.55, BuildSnippet(email), "email-keyword");
            ApplyRecencyBoost(scores, email);
        }

        var attachmentIds = (await attachmentIdsTask).ToList();
        if (attachmentIds.Count > 0)
        {
            var attachmentEmails = await _emails.GetByIdsAsync(attachmentIds, ct);
            foreach (var email in attachmentEmails)
            {
                AddHit(email.EmailId, 0.8, BuildSnippet(email), "attachment");
                ApplyRecencyBoost(scores, email);
            }
        }

        if (scores.Count == 0)
        {
            _logger.LogInformation("Chunk RAG found no hits, falling back to legacy hybrid search");
            var fallback = (await _search.SearchAsync(query, SearchMode.Hybrid, emailLimit, ct)).ToList();
            var fallbackSnippets = fallback.ToDictionary(
                e => e.EmailId,
                e => BuildSnippet(e));
            return new RagSearchResult(fallback, fallbackSnippets);
        }

        var rankedIds = scores
            .OrderByDescending(kv => kv.Value)
            .Take(emailLimit)
            .Select(kv => kv.Key)
            .ToList();

        var primaryEmails = (await _emails.GetByIdsAsync(rankedIds, ct)).ToList();
        primaryEmails = rankedIds
            .Select(id => primaryEmails.FirstOrDefault(e => e.EmailId == id))
            .Where(e => e is not null)
            .Cast<Email>()
            .ToList();

        var expanded = await ExpandWithThreadsAsync(primaryEmails, ct);

        var snippetMap = expanded
            .Where(e => snippets.ContainsKey(e.EmailId))
            .ToDictionary(
                e => e.EmailId,
                e => snippets[e.EmailId].Snippet);

        foreach (var email in expanded.Where(e => !snippetMap.ContainsKey(e.EmailId)))
            snippetMap[email.EmailId] = BuildSnippet(email);

        _logger.LogInformation(
            "RAG ranked {Primary} primary + {Thread} thread emails from {Chunks} chunk hits",
            primaryEmails.Count, expanded.Count - primaryEmails.Count, scores.Count);

        return new RagSearchResult(expanded.Take(AppConstants.MaxContextEmails).ToList(), snippetMap);
    }

    private async Task<List<Email>> ExpandWithThreadsAsync(List<Email> primaryEmails, CancellationToken ct)
    {
        var result = new List<Email>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var email in primaryEmails)
        {
            if (seen.Add(email.EmailId))
                result.Add(email);
        }

        var conversationIds = primaryEmails
            .Select(e => e.ConversationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(5);

        foreach (var conversationId in conversationIds)
        {
            var thread = (await _emails.GetByConversationIdAsync(conversationId!, AppConstants.MaxThreadEmails, ct)).ToList();
            foreach (var email in thread)
            {
                if (seen.Add(email.EmailId))
                    result.Add(email);
            }
        }

        return result;
    }

    private static void ApplyRecencyBoost(Dictionary<string, double> scores, Email email)
    {
        var ageDays = (DateTime.UtcNow - email.ReceivedDate.ToUniversalTime()).TotalDays;
        if (ageDays <= 7) scores[email.EmailId] = scores.GetValueOrDefault(email.EmailId) + 0.12;
        else if (ageDays <= 30) scores[email.EmailId] = scores.GetValueOrDefault(email.EmailId) + 0.06;
    }

    private static string BuildSnippet(Email email)
    {
        var text = string.IsNullOrWhiteSpace(email.BodyText) ? email.Subject : email.BodyText;
        text = text.Replace("\r\n", " ").Trim();
        return text.Length > 400 ? text[..400] + "…" : text;
    }
}

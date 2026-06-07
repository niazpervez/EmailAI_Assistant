using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services;

public sealed class SearchService : ISearchService
{
    private readonly IEmailRepository _emails;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAttachmentRepository _attachments;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IEmailRepository emails,
        IEmbeddingRepository embeddings,
        IEmbeddingService embeddingService,
        IAttachmentRepository attachments,
        ILogger<SearchService> logger)
    {
        _emails = emails; _embeddings = embeddings;
        _embeddingService = embeddingService; _attachments = attachments;
        _logger = logger;
    }

    public async Task<IEnumerable<Email>> SearchAsync(
        string query, SearchMode mode = SearchMode.Hybrid, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return mode switch
        {
            SearchMode.Keyword => await KeywordSearchAsync(query, limit, ct),
            SearchMode.Semantic => await SemanticSearchAsync(query, limit, ct),
            SearchMode.Hybrid => await HybridSearchAsync(query, limit, ct),
            _ => []
        };
    }

    private async Task<IEnumerable<Email>> KeywordSearchAsync(string query, int limit, CancellationToken ct)
        => await _emails.SearchByKeywordAsync(query, limit, ct);

    private async Task<IEnumerable<Email>> SemanticSearchAsync(string query, int limit, CancellationToken ct)
    {
        var vector = await _embeddingService.GenerateAsync(query, ct);
        var similarIds = await _embeddings.FindSimilarEmailIdsAsync(vector, limit, ct);
        return await _emails.GetByIdsAsync(similarIds, ct);
    }

    private async Task<IEnumerable<Email>> HybridSearchAsync(string query, int limit, CancellationToken ct)
    {
        var keywordTask = KeywordSearchAsync(query, limit, ct);
        var semanticTask = SemanticSearchAsync(query, limit, ct);
        var attachmentTask = AttachmentSearchAsync(query, limit, ct);

        await Task.WhenAll(keywordTask, semanticTask, attachmentTask);

        var semantic = (await semanticTask).ToList();
        var keyword = (await keywordTask).ToList();
        var attachment = (await attachmentTask).ToList();

        var merged = semantic.ToList();
        var seenIds = new HashSet<string>(merged.Select(e => e.EmailId));

        foreach (var e in keyword.Concat(attachment))
        {
            if (seenIds.Add(e.EmailId))
                merged.Add(e);
        }

        return merged.Take(limit);
    }

    private async Task<IEnumerable<Email>> AttachmentSearchAsync(string query, int limit, CancellationToken ct)
    {
        var emailIds = (await _attachments.SearchEmailIdsByExtractedTextAsync(query, limit, ct)).ToList();
        if (emailIds.Count == 0) return [];
        return await _emails.GetByIdsAsync(emailIds, ct);
    }
}

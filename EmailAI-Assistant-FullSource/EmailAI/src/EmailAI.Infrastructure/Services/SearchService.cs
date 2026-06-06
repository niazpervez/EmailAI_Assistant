using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services;

public sealed class SearchService : ISearchService
{
    private readonly IEmailRepository _emails;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IEmailRepository emails,
        IEmbeddingRepository embeddings,
        IEmbeddingService embeddingService,
        ILogger<SearchService> logger)
    {
        _emails = emails; _embeddings = embeddings;
        _embeddingService = embeddingService; _logger = logger;
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
        // Run both searches in parallel
        var keywordTask = KeywordSearchAsync(query, limit, ct);
        var semanticTask = SemanticSearchAsync(query, limit, ct);

        await Task.WhenAll(keywordTask, semanticTask);

        // Merge results: semantic results first (higher relevance), then keyword-only results
        var semantic = (await semanticTask).ToList();
        var keyword = (await keywordTask).ToList();

        var merged = semantic.ToList();
        var seenIds = new HashSet<string>(merged.Select(e => e.EmailId));

        foreach (var e in keyword)
        {
            if (seenIds.Add(e.EmailId))
                merged.Add(e);
        }

        return merged.Take(limit);
    }
}

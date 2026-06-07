namespace EmailAI.Core.DTOs;

public record ChunkSearchHit(string ChunkId, string EmailId, string Snippet, double Score, string Source);

public record RagSearchResult(
    IReadOnlyList<Entities.Email> Emails,
    IReadOnlyDictionary<string, string> SnippetsByEmailId);

using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services;

public sealed class ChunkIndexingService : IChunkIndexingService
{
    private readonly IEmailRepository _emails;
    private readonly IAttachmentRepository _attachments;
    private readonly IChunkRepository _chunks;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ChunkIndexingService> _logger;

    public ChunkIndexingService(
        IEmailRepository emails,
        IAttachmentRepository attachments,
        IChunkRepository chunks,
        ITextChunker chunker,
        IEmbeddingService embeddingService,
        ILogger<ChunkIndexingService> logger)
    {
        _emails = emails;
        _attachments = attachments;
        _chunks = chunks;
        _chunker = chunker;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task IndexEmailAsync(string emailId, CancellationToken ct = default)
    {
        var email = await _emails.GetByEmailIdAsync(emailId, ct);
        if (email is null) return;
        await IndexEmailInternalAsync(email, ct);
    }

    public async Task IndexBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default)
    {
        foreach (var email in emails)
        {
            if (ct.IsCancellationRequested) break;
            await IndexEmailInternalAsync(email, ct);
        }
    }

    public async Task BackfillAsync(int batchSize = 50, CancellationToken ct = default)
    {
        var pending = (await _chunks.GetUnindexedEmailIdsAsync(batchSize, ct)).ToList();
        if (pending.Count == 0) return;

        _logger.LogInformation("Chunk backfill: indexing {Count} email(s)", pending.Count);
        foreach (var emailId in pending)
        {
            if (ct.IsCancellationRequested) break;
            await IndexEmailAsync(emailId, ct);
        }
    }

    private async Task IndexEmailInternalAsync(Email email, CancellationToken ct)
    {
        try
        {
            var dbEmail = await _emails.GetByEmailIdAsync(email.EmailId, ct);
            if (dbEmail is null) return;

            var attachmentText = dbEmail.HasAttachments
                ? await _attachments.GetCombinedExtractedTextAsync(dbEmail.EmailId, 8000, ct)
                : null;

            var parts = _chunker.BuildChunks(dbEmail, attachmentText).ToList();
            if (parts.Count == 0) return;

            await _chunks.DeleteByEmailIdAsync(dbEmail.EmailId, ct);

            var chunkEntities = parts.Select(p => new EmailChunk
            {
                ChunkId = $"{dbEmail.EmailId}|{p.Source}|{p.Index}",
                EmailRecordId = dbEmail.Id,
                EmailId = dbEmail.EmailId,
                ChunkIndex = p.Index,
                Source = p.Source,
                Content = p.Content,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _chunks.InsertBatchAsync(chunkEntities, ct);

            foreach (var chunk in chunkEntities)
            {
                if (ct.IsCancellationRequested) break;

                var input = chunk.Content.Length > 8000 ? chunk.Content[..8000] : chunk.Content;
                var vector = await _embeddingService.GenerateAsync(input, ct);
                await _chunks.UpsertEmbeddingAsync(
                    chunk.ChunkId, chunk.EmailId, vector, _embeddingService.ModelName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chunk indexing failed for {EmailId}", email.EmailId);
        }
    }
}

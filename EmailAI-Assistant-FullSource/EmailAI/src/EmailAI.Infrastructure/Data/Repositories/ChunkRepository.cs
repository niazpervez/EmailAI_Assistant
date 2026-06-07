using Dapper;
using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Data.Repositories;

public sealed class ChunkRepository : IChunkRepository
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly ILogger<ChunkRepository> _logger;
    private bool _vecAvailable = true;

    public ChunkRepository(DatabaseConnectionFactory factory, ILogger<ChunkRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<bool> ExistsForEmailAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM EmailChunks WHERE EmailId = @emailId", new { emailId }) > 0;
    }

    public async Task DeleteByEmailIdAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        var chunkIds = (await c.QueryAsync<string>(
            "SELECT ChunkId FROM EmailChunks WHERE EmailId = @emailId", new { emailId }, tx)).ToList();

        if (_vecAvailable && chunkIds.Count > 0)
        {
            try
            {
                foreach (var chunkId in chunkIds)
                {
                    await c.ExecuteAsync(
                        "DELETE FROM EmailChunkVectors WHERE chunk_id = @chunkId",
                        new { chunkId }, tx);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Chunk vec delete failed: {Msg}", ex.Message);
                _vecAvailable = false;
            }
        }

        await c.ExecuteAsync("DELETE FROM EmailChunkEmbeddings WHERE EmailId = @emailId", new { emailId }, tx);
        await c.ExecuteAsync("DELETE FROM EmailChunks WHERE EmailId = @emailId", new { emailId }, tx);
        await tx.CommitAsync(ct);
    }

    public async Task InsertBatchAsync(IEnumerable<EmailChunk> chunks, CancellationToken ct = default)
    {
        var list = chunks.ToList();
        if (list.Count == 0) return;

        await using var c = await _factory.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        foreach (var chunk in list)
        {
            await c.ExecuteAsync(
                """
                INSERT INTO EmailChunks (ChunkId, EmailRecordId, EmailId, ChunkIndex, Source, Content, CreatedAt)
                VALUES (@ChunkId, @EmailRecordId, @EmailId, @ChunkIndex, @Source, @Content, @CreatedAt)
                """,
                chunk, tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task UpsertEmbeddingAsync(
        string chunkId, string emailId, float[] vector, string modelName, CancellationToken ct = default)
    {
        var blob = VectorToBytes(vector);

        await using var c = await _factory.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        await c.ExecuteAsync(
            """
            INSERT INTO EmailChunkEmbeddings (ChunkId, EmailId, VectorData, Dimensions, ModelUsed, CreatedAt)
            VALUES (@chunkId, @emailId, @blob, @dims, @model, @now)
            ON CONFLICT(ChunkId) DO UPDATE SET
                VectorData = excluded.VectorData,
                ModelUsed  = excluded.ModelUsed,
                CreatedAt  = excluded.CreatedAt
            """,
            new
            {
                chunkId,
                emailId,
                blob,
                dims = vector.Length,
                model = modelName,
                now = DateTime.UtcNow
            }, tx);

        if (_vecAvailable)
        {
            try
            {
                await using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO EmailChunkVectors (chunk_id, embedding) VALUES (@chunk_id, @embedding)
                    ON CONFLICT(chunk_id) DO UPDATE SET embedding = excluded.embedding;
                    """;
                cmd.Parameters.AddWithValue("@chunk_id", chunkId);
                cmd.Parameters.AddWithValue("@embedding", blob);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Chunk vec upsert failed: {Msg}", ex.Message);
                _vecAvailable = false;
            }
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IEnumerable<ChunkSearchHit>> FindSimilarChunksAsync(
        float[] queryVector, int topK = 40, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);

        List<(string ChunkId, double Score)> hits;
        if (_vecAvailable)
        {
            try
            {
                hits = (await FindViaVecTableAsync(c, queryVector, topK, ct)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Chunk vec search failed, falling back: {Msg}", ex.Message);
                _vecAvailable = false;
                hits = (await FindViaBruteForceAsync(c, queryVector, topK, ct)).ToList();
            }
        }
        else
        {
            hits = (await FindViaBruteForceAsync(c, queryVector, topK, ct)).ToList();
        }

        if (hits.Count == 0) return [];

        var chunkIds = hits.Select(h => h.ChunkId).ToList();
        var chunks = await c.QueryAsync<EmailChunk>(
            "SELECT * FROM EmailChunks WHERE ChunkId IN @chunkIds", new { chunkIds });

        var scoreMap = hits.ToDictionary(h => h.ChunkId, h => h.Score);
        return chunks
            .Select(ch => new ChunkSearchHit(
                ch.ChunkId,
                ch.EmailId,
                ch.Content.Length > 500 ? ch.Content[..500] + "…" : ch.Content,
                scoreMap.GetValueOrDefault(ch.ChunkId),
                "semantic"))
            .OrderByDescending(h => h.Score);
    }

    public async Task<IEnumerable<ChunkSearchHit>> SearchByKeywordAsync(
        string keyword, int limit = 30, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        try
        {
            var ftsQuery = BuildFtsQuery(keyword);
            var rows = await c.QueryAsync<(string ChunkId, string EmailId, string Content)>(
                """
                SELECT ch.ChunkId, ch.EmailId, ch.Content
                FROM EmailChunks ch
                JOIN EmailChunksFts f ON ch.Id = f.rowid
                WHERE EmailChunksFts MATCH @keyword
                ORDER BY rank
                LIMIT @limit
                """,
                new { keyword = ftsQuery, limit });

            return rows.Select(r => new ChunkSearchHit(
                r.ChunkId,
                r.EmailId,
                r.Content.Length > 500 ? r.Content[..500] + "…" : r.Content,
                0.75,
                "keyword"));
        }
        catch
        {
            var rows = await c.QueryAsync<(string ChunkId, string EmailId, string Content)>(
                """
                SELECT ChunkId, EmailId, Content FROM EmailChunks
                WHERE Content LIKE @kw
                ORDER BY CreatedAt DESC
                LIMIT @limit
                """,
                new { kw = $"%{keyword}%", limit });

            return rows.Select(r => new ChunkSearchHit(
                r.ChunkId,
                r.EmailId,
                r.Content.Length > 500 ? r.Content[..500] + "…" : r.Content,
                0.6,
                "keyword"));
        }
    }

    public async Task<IEnumerable<string>> GetUnindexedEmailIdsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.QueryAsync<string>(
            """
            SELECT e.EmailId FROM Emails e
            WHERE NOT EXISTS (SELECT 1 FROM EmailChunks c WHERE c.EmailId = e.EmailId)
            ORDER BY e.ReceivedDate DESC
            LIMIT @limit
            """,
            new { limit });
    }

    public async Task<int> GetIndexedEmailCountAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT EmailId) FROM EmailChunks");
    }

    private static async Task<IEnumerable<(string ChunkId, double Score)>> FindViaVecTableAsync(
        SqliteConnection c, float[] queryVector, int topK, CancellationToken ct)
    {
        var blob = VectorToBytes(queryVector);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT chunk_id, distance FROM EmailChunkVectors
            WHERE embedding MATCH @query AND k = @topK
            ORDER BY distance;
            """;
        cmd.Parameters.AddWithValue("@query", blob);
        cmd.Parameters.AddWithValue("@topK", topK);

        var results = new List<(string, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var chunkId = reader.GetString(0);
            var distance = reader.GetDouble(1);
            results.Add((chunkId, 1.0 / (1.0 + distance)));
        }

        return results;
    }

    private static async Task<IEnumerable<(string ChunkId, double Score)>> FindViaBruteForceAsync(
        SqliteConnection c, float[] queryVector, int topK, CancellationToken ct)
    {
        var rows = await c.QueryAsync<(string ChunkId, byte[] VectorData)>(
            "SELECT ChunkId, VectorData FROM EmailChunkEmbeddings");

        return rows
            .Select(r => (r.ChunkId, Score: (double)CosineSimilarity(queryVector, BytesToVector(r.VectorData))))
            .OrderByDescending(x => x.Score)
            .Take(topK);
    }

    private static string BuildFtsQuery(string keyword)
    {
        var terms = keyword
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeFtsTerm)
            .Where(t => t.Length > 0)
            .Select(t => $"{t}*")
            .ToList();

        return terms.Count switch
        {
            0 => keyword,
            1 => terms[0],
            _ => string.Join(" OR ", terms)
        };
    }

    private static string SanitizeFtsTerm(string term)
    {
        var cleaned = new string(term.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return cleaned.Trim('-', '_');
    }

    private static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToVector(byte[] bytes)
    {
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return (normA == 0 || normB == 0) ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}

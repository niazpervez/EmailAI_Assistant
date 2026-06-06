using Dapper;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace EmailAI.Infrastructure.Data.Repositories;

public sealed class EmbeddingRepository : IEmbeddingRepository
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly ILogger<EmbeddingRepository> _logger;
    private bool _vecAvailable = true;

    public EmbeddingRepository(DatabaseConnectionFactory factory, ILogger<EmbeddingRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<EmailEmbedding?> GetByEmailIdAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var row = await c.QueryFirstOrDefaultAsync<EmbeddingRow>(
            "SELECT * FROM EmailEmbeddings WHERE EmailId = @emailId", new { emailId });

        if (row is null) return null;
        return MapRow(row);
    }

    public async Task<bool> ExistsAsync(string emailId, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        var count = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM EmailEmbeddings WHERE EmailId = @emailId", new { emailId });
        return count > 0;
    }

    public async Task UpsertAsync(EmailEmbedding embedding, CancellationToken ct = default)
    {
        var blob = VectorToBytes(embedding.Vector);

        await using var c = await _factory.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);

        // Upsert into regular table (stores blob)
        await c.ExecuteAsync(
            """
            INSERT INTO EmailEmbeddings (EmailRecordId, EmailId, VectorData, Dimensions, ModelUsed, CreatedAt)
            VALUES (@EmailRecordId, @EmailId, @VectorData, @Dimensions, @ModelUsed, @CreatedAt)
            ON CONFLICT(EmailId) DO UPDATE SET
                VectorData = excluded.VectorData,
                ModelUsed  = excluded.ModelUsed,
                CreatedAt  = excluded.CreatedAt;
            """,
            new
            {
                embedding.EmailRecordId,
                embedding.EmailId,
                VectorData = blob,
                embedding.Dimensions,
                embedding.ModelUsed,
                embedding.CreatedAt
            }, tx);

        // Upsert into sqlite-vec virtual table
        if (_vecAvailable)
        {
            try
            {
                await UpsertVecTableAsync(c, embedding.EmailId, embedding.Vector, tx, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("sqlite-vec upsert failed: {Msg}", ex.Message);
                _vecAvailable = false;
            }
        }

        await tx.CommitAsync(ct);
    }

    private static async Task UpsertVecTableAsync(
        SqliteConnection c, string emailId, float[] vector,
        SqliteTransaction tx, CancellationToken ct)
    {
        // sqlite-vec requires the vector as a raw float32 blob
        var blob = VectorToBytes(vector);

        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO EmailVectors (email_id, embedding) VALUES (@email_id, @embedding)
            ON CONFLICT(email_id) DO UPDATE SET embedding = excluded.embedding;
            """;
        cmd.Parameters.AddWithValue("@email_id", emailId);
        cmd.Parameters.AddWithValue("@embedding", blob);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IEnumerable<string>> FindSimilarEmailIdsAsync(float[] queryVector, int topK = 10, CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);

        if (_vecAvailable)
        {
            try
            {
                return await FindViaVecTableAsync(c, queryVector, topK, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("sqlite-vec search failed, falling back: {Msg}", ex.Message);
                _vecAvailable = false;
            }
        }

        // Fallback: cosine similarity computed in-process
        return await FindViaBruteForceAsync(c, queryVector, topK, ct);
    }

    private static async Task<IEnumerable<string>> FindViaVecTableAsync(
        SqliteConnection c, float[] queryVector, int topK, CancellationToken ct)
    {
        var blob = VectorToBytes(queryVector);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT email_id FROM EmailVectors
            WHERE embedding MATCH @query AND k = @topK
            ORDER BY distance;
            """;
        cmd.Parameters.AddWithValue("@query", blob);
        cmd.Parameters.AddWithValue("@topK", topK);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    private static async Task<IEnumerable<string>> FindViaBruteForceAsync(
        SqliteConnection c, float[] queryVector, int topK, CancellationToken ct)
    {
        var rows = await c.QueryAsync<EmbeddingRow>("SELECT EmailId, VectorData FROM EmailEmbeddings");
        return rows
            .Select(r => (r.EmailId, Score: CosineSimilarity(queryVector, BytesToVector(r.VectorData))))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.EmailId);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        await using var c = await _factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM EmailEmbeddings");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static EmailEmbedding MapRow(EmbeddingRow row) => new()
    {
        Id = row.Id,
        EmailRecordId = row.EmailRecordId,
        EmailId = row.EmailId,
        Vector = BytesToVector(row.VectorData),
        Dimensions = row.Dimensions,
        ModelUsed = row.ModelUsed,
        CreatedAt = row.CreatedAt
    };

    private sealed class EmbeddingRow
    {
        public int Id { get; init; }
        public int EmailRecordId { get; init; }
        public string EmailId { get; init; } = "";
        public byte[] VectorData { get; init; } = Array.Empty<byte>();
        public int Dimensions { get; init; }
        public string ModelUsed { get; init; } = "";
        public DateTime CreatedAt { get; init; }
    }
}

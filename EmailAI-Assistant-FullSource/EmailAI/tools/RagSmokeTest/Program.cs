using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.Infrastructure.Data.Repositories;
using EmailAI.Infrastructure.Security;
using EmailAI.Infrastructure.Services;
using EmailAI.Infrastructure.Services.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var appData = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    AppConstants.AppName);
var dbPath = Path.Combine(appData, "emailai.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine("FAIL: Database not found at " + dbPath);
    return 1;
}

Console.WriteLine("=== EmailAI RAG Smoke Test ===");
Console.WriteLine("DB: " + dbPath);

var services = new ServiceCollection();
services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
services.AddSingleton(_ => new DatabaseConnectionFactory(dbPath));
services.AddSingleton<IEncryptionService, PlatformEncryptionService>();
services.AddSingleton<ISettingsRepository, SettingsRepository>();
services.AddSingleton<IEmailRepository, EmailRepository>();
services.AddSingleton<IAttachmentRepository, AttachmentRepository>();
services.AddSingleton<IChunkRepository, ChunkRepository>();
services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
services.AddSingleton<ITextChunker, TextChunker>();
services.AddSingleton<IChunkIndexingService, ChunkIndexingService>();
services.AddSingleton<ISearchService, SearchService>();
services.AddSingleton<IRagSearchService, RagSearchService>();
services.AddTransient<DeepSeekAuthHandler>();
services.AddHttpClient<DeepSeekEmbeddingService>((_, http) =>
{
    http.DefaultRequestHeaders.Add("Accept", "application/json");
}).AddHttpMessageHandler<DeepSeekAuthHandler>();
services.AddTransient<IEmbeddingService>(sp => sp.GetRequiredService<DeepSeekEmbeddingService>());

await using var provider = services.BuildServiceProvider();

// Apply schema migrations (chunk tables)
var dbInit = new DatabaseInitializer(dbPath, provider.GetRequiredService<ILogger<DatabaseInitializer>>());
await dbInit.InitializeAsync();

await using (var c = new SqliteConnection($"Data Source={dbPath};"))
{
    await c.OpenAsync();
    static async Task<int> Count(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    var emails = await Count(c, "SELECT COUNT(1) FROM Emails");
    var chunks = await Count(c, "SELECT COUNT(1) FROM EmailChunks");
    var indexed = await Count(c, "SELECT COUNT(DISTINCT EmailId) FROM EmailChunks");
    var attachments = await Count(c, "SELECT COUNT(1) FROM Attachments");

    Console.WriteLine($"Emails: {emails} | Chunk rows: {chunks} | Indexed emails: {indexed} | Attachments: {attachments}");
}

var chunkRepo = provider.GetRequiredService<IChunkRepository>();
var chunkIndexing = provider.GetRequiredService<IChunkIndexingService>();
var rag = provider.GetRequiredService<IRagSearchService>();

var unindexed = (await chunkRepo.GetUnindexedEmailIdsAsync(3)).ToList();
if (unindexed.Count > 0)
{
    Console.WriteLine($"Indexing {unindexed.Count} sample email(s) for test…");
    foreach (var id in unindexed)
        await chunkIndexing.IndexEmailAsync(id);
}

var indexedAfter = await chunkRepo.GetIndexedEmailCountAsync();
Console.WriteLine($"Indexed emails after sample index: {indexedAfter}");

if (indexedAfter == 0)
{
    Console.WriteLine("WARN: No chunk index yet — run Sync in the app to index mail.");
}

Console.WriteLine("Running RAG search: \"recent important emails\"…");
var result = await rag.SearchAsync("recent important emails", 5);
Console.WriteLine($"RAG hits: {result.Emails.Count} email(s)");
foreach (var e in result.Emails.Take(3))
{
    var excerpt = result.SnippetsByEmailId.GetValueOrDefault(e.EmailId, "(no excerpt)");
    var preview = excerpt.Length > 100 ? excerpt[..100] + "…" : excerpt;
    Console.WriteLine($"  - [{e.ReceivedDate:yyyy-MM-dd}] {e.Subject} | {preview}");
}

Console.WriteLine("OK: RAG smoke test passed.");
return 0;

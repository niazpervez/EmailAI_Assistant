using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Data;

/// <summary>
/// Initialises the SQLite database, loads the sqlite-vec extension,
/// and runs all DDL migrations idempotently.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly string _dbPath;
    private readonly ILogger<DatabaseInitializer> _logger;

    // sqlite-vec dimensions must match the embedding model (1536 for ada-002 / deepseek)
    private const int VecDimensions = 1536;

    public DatabaseInitializer(string dbPath, ILogger<DatabaseInitializer> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        await using var connection = new SqliteConnection($"Data Source={_dbPath};");
        await connection.OpenAsync(ct);

        // Load sqlite-vec extension when a valid native DLL is present
        VecExtensionLoader.TryLoad(connection, _logger);

        // Enable WAL mode for better concurrency
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", ct);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;", ct);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", ct);
        await ExecuteAsync(connection, "PRAGMA cache_size=-64000;", ct); // 64MB cache

        await CreateTablesAsync(connection, ct);
        await RunMigrationsAsync(connection, ct);
        await CreateVirtualTablesAsync(connection, ct);
        await CreateIndexesAsync(connection, ct);
        await SeedDefaultSettingsAsync(connection, ct);

        _logger.LogInformation("Database initialized at {Path}", _dbPath);
    }

    private static async Task CreateTablesAsync(SqliteConnection c, CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS Emails (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                EmailId         TEXT    NOT NULL UNIQUE,
                ConversationId  TEXT    NOT NULL DEFAULT '',
                Subject         TEXT    NOT NULL DEFAULT '',
                Sender          TEXT    NOT NULL DEFAULT '',
                SenderName      TEXT    NOT NULL DEFAULT '',
                Recipients      TEXT    NOT NULL DEFAULT '[]',
                ReceivedDate    TEXT    NOT NULL,
                BodyText        TEXT    NOT NULL DEFAULT '',
                BodyHtml        TEXT    NOT NULL DEFAULT '',
                FolderName      TEXT    NOT NULL DEFAULT '',
                FolderId        TEXT    NOT NULL DEFAULT '',
                HasAttachments  INTEGER NOT NULL DEFAULT 0,
                IsRead          INTEGER NOT NULL DEFAULT 0,
                IsImportant     INTEGER NOT NULL DEFAULT 0,
                Importance      TEXT    NOT NULL DEFAULT 'normal',
                SyncedAt        TEXT    NOT NULL DEFAULT (datetime('now')),
                ChangeKey       TEXT
            ) STRICT;

            CREATE TABLE IF NOT EXISTS Attachments (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                AttachmentId    TEXT    NOT NULL UNIQUE,
                EmailRecordId   INTEGER NOT NULL REFERENCES Emails(Id) ON DELETE CASCADE,
                EmailId         TEXT    NOT NULL,
                FileName        TEXT    NOT NULL DEFAULT '',
                ContentType     TEXT    NOT NULL DEFAULT '',
                SizeBytes       INTEGER NOT NULL DEFAULT 0,
                ExtractedText   TEXT    NOT NULL DEFAULT '',
                IsTextExtracted INTEGER NOT NULL DEFAULT 0,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            ) STRICT;

            CREATE TABLE IF NOT EXISTS EmailEmbeddings (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                EmailRecordId   INTEGER NOT NULL UNIQUE REFERENCES Emails(Id) ON DELETE CASCADE,
                EmailId         TEXT    NOT NULL UNIQUE,
                VectorData      BLOB    NOT NULL,
                Dimensions      INTEGER NOT NULL,
                ModelUsed       TEXT    NOT NULL DEFAULT '',
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            ) STRICT;

            CREATE TABLE IF NOT EXISTS SyncStates (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderId        TEXT    NOT NULL UNIQUE,
                FolderName      TEXT    NOT NULL DEFAULT '',
                DeltaLink       TEXT,
                NextLink        TEXT,
                LastSyncedAt    TEXT,
                TotalSynced     INTEGER NOT NULL DEFAULT 0,
                Status          TEXT    NOT NULL DEFAULT 'idle',
                LastError       TEXT
            ) STRICT;

            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId       TEXT    NOT NULL,
                Role            TEXT    NOT NULL,
                Content         TEXT    NOT NULL DEFAULT '',
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                RelevantEmailIds TEXT,
                TokensUsed      INTEGER
            ) STRICT;

            CREATE TABLE IF NOT EXISTS AppSettings (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Key             TEXT    NOT NULL UNIQUE,
                Value           TEXT    NOT NULL DEFAULT '',
                IsEncrypted     INTEGER NOT NULL DEFAULT 0,
                UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            ) STRICT;

            CREATE TABLE IF NOT EXISTS EmailChunks (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ChunkId         TEXT    NOT NULL UNIQUE,
                EmailRecordId   INTEGER NOT NULL REFERENCES Emails(Id) ON DELETE CASCADE,
                EmailId         TEXT    NOT NULL,
                ChunkIndex      INTEGER NOT NULL DEFAULT 0,
                Source          TEXT    NOT NULL DEFAULT 'body',
                Content         TEXT    NOT NULL DEFAULT '',
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            ) STRICT;

            CREATE TABLE IF NOT EXISTS EmailChunkEmbeddings (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ChunkId         TEXT    NOT NULL UNIQUE,
                EmailId         TEXT    NOT NULL,
                VectorData      BLOB    NOT NULL,
                Dimensions      INTEGER NOT NULL,
                ModelUsed       TEXT    NOT NULL DEFAULT '',
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            ) STRICT;
            """;

        await ExecuteAsync(c, sql, ct);
    }

    private static async Task RunMigrationsAsync(SqliteConnection c, CancellationToken ct)
    {
        await TryAddColumnAsync(c, "Emails", "MessageId", "TEXT NOT NULL DEFAULT ''", ct);
        await ExecuteSingleAsync(c, "CREATE INDEX IF NOT EXISTS idx_emails_messageid ON Emails(MessageId);", ct);
    }

    private static async Task TryAddColumnAsync(
        SqliteConnection c, string table, string column, string definition, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(1);
            if (name.Equals(column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        await ExecuteSingleAsync(c, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", ct);
    }

    private static async Task CreateVirtualTablesAsync(SqliteConnection c, CancellationToken ct)
    {
        // sqlite-vec virtual table for ANN search
        // The table is created only once; it will silently fail if vec extension isn't loaded.
        try
        {
            var sql = $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS EmailVectors
                USING vec0(
                    email_id     TEXT     PRIMARY KEY,
                    embedding    float[{VecDimensions}]
                );
                """;
            await ExecuteAsync(c, sql, ct);
        }
        catch
        {
            // vec extension not available — vector search disabled
        }

        try
        {
            var chunkVecSql = $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS EmailChunkVectors
                USING vec0(
                    chunk_id     TEXT     PRIMARY KEY,
                    embedding    float[{VecDimensions}]
                );
                """;
            await ExecuteAsync(c, chunkVecSql, ct);
        }
        catch
        {
            // chunk vec table optional
        }

        // FTS5 full-text search on email body + subject (each statement separate — triggers contain ';')
        await ExecuteAsync(c, """
            CREATE VIRTUAL TABLE IF NOT EXISTS EmailsFts
            USING fts5(
                email_id    UNINDEXED,
                subject,
                sender_name,
                body_text,
                content='Emails',
                content_rowid='Id'
            )
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON Emails BEGIN
                INSERT INTO EmailsFts(rowid, email_id, subject, sender_name, body_text)
                VALUES (new.Id, new.EmailId, new.Subject, new.SenderName, new.BodyText);
            END
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON Emails BEGIN
                INSERT INTO EmailsFts(EmailsFts, rowid, email_id, subject, sender_name, body_text)
                VALUES ('delete', old.Id, old.EmailId, old.Subject, old.SenderName, old.BodyText);
            END
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON Emails BEGIN
                INSERT INTO EmailsFts(EmailsFts, rowid, email_id, subject, sender_name, body_text)
                VALUES ('delete', old.Id, old.EmailId, old.Subject, old.SenderName, old.BodyText);
                INSERT INTO EmailsFts(rowid, email_id, subject, sender_name, body_text)
                VALUES (new.Id, new.EmailId, new.Subject, new.SenderName, new.BodyText);
            END
            """, ct);

        await ExecuteAsync(c, """
            CREATE VIRTUAL TABLE IF NOT EXISTS EmailChunksFts
            USING fts5(
                chunk_id    UNINDEXED,
                email_id    UNINDEXED,
                content,
                content='EmailChunks',
                content_rowid='Id'
            )
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS email_chunks_ai AFTER INSERT ON EmailChunks BEGIN
                INSERT INTO EmailChunksFts(rowid, chunk_id, email_id, content)
                VALUES (new.Id, new.ChunkId, new.EmailId, new.Content);
            END
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS email_chunks_ad AFTER DELETE ON EmailChunks BEGIN
                INSERT INTO EmailChunksFts(EmailChunksFts, rowid, chunk_id, email_id, content)
                VALUES ('delete', old.Id, old.ChunkId, old.EmailId, old.Content);
            END
            """, ct);

        await ExecuteSingleAsync(c, """
            CREATE TRIGGER IF NOT EXISTS email_chunks_au AFTER UPDATE ON EmailChunks BEGIN
                INSERT INTO EmailChunksFts(EmailChunksFts, rowid, chunk_id, email_id, content)
                VALUES ('delete', old.Id, old.ChunkId, old.EmailId, old.Content);
                INSERT INTO EmailChunksFts(rowid, chunk_id, email_id, content)
                VALUES (new.Id, new.ChunkId, new.EmailId, new.Content);
            END
            """, ct);
    }

    private static async Task CreateIndexesAsync(SqliteConnection c, CancellationToken ct)
    {
        const string sql = """
            CREATE INDEX IF NOT EXISTS idx_emails_received    ON Emails(ReceivedDate DESC);
            CREATE INDEX IF NOT EXISTS idx_emails_sender      ON Emails(Sender);
            CREATE INDEX IF NOT EXISTS idx_emails_folder      ON Emails(FolderName);
            CREATE INDEX IF NOT EXISTS idx_emails_isread      ON Emails(IsRead);
            CREATE INDEX IF NOT EXISTS idx_emails_conv        ON Emails(ConversationId);
            CREATE INDEX IF NOT EXISTS idx_attach_email       ON Attachments(EmailId);
            CREATE INDEX IF NOT EXISTS idx_embed_email        ON EmailEmbeddings(EmailId);
            CREATE INDEX IF NOT EXISTS idx_chunk_email        ON EmailChunks(EmailId);
            CREATE INDEX IF NOT EXISTS idx_chunk_embed        ON EmailChunkEmbeddings(EmailId);
            CREATE INDEX IF NOT EXISTS idx_chat_session       ON ChatMessages(SessionId, CreatedAt);
            CREATE INDEX IF NOT EXISTS idx_settings_key       ON AppSettings(Key);
            """;
        await ExecuteAsync(c, sql, ct);
    }

    private static async Task SeedDefaultSettingsAsync(SqliteConnection c, CancellationToken ct)
    {
        const string sql = """
            INSERT OR IGNORE INTO AppSettings (Key, Value, IsEncrypted) VALUES
                ('Sync:IntervalMinutes',    '15',       0),
                ('Sync:Folders',            '["Inbox","Sent","Archive"]', 0),
                ('Sync:AllFolders',         '1',        0),
                ('Sync:MaxEmailsPerSync',   '500',      0),
                ('DeepSeek:Model',          'deepseek-chat', 0),
                ('Embedding:Dimensions',    '1536',     0),
                ('Database:Version',        '2',        0);
            """;
        await ExecuteAsync(c, sql, ct);
    }

    private static async Task ExecuteAsync(SqliteConnection c, string sql, CancellationToken ct)
    {
        // Execute each statement separately
        foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            await ExecuteSingleAsync(c, stmt, ct);
        }
    }

    private static async Task ExecuteSingleAsync(SqliteConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql.TrimEnd().EndsWith(';') ? sql.Trim() : sql.Trim() + ";";
        try { await cmd.ExecuteNonQueryAsync(ct); }
        catch (SqliteException ex) when (ex.Message.Contains("already exists")) { /* idempotent */ }
    }
}

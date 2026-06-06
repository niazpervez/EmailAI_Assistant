using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailAI.Infrastructure.Data;

public sealed class DatabaseConnectionFactory
{
    private readonly string _connectionString;

    public DatabaseConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _connectionString = $"Data Source={options.Value.Path};";
    }

    public DatabaseConnectionFactory(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};";
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        // Always enable FK and WAL per-connection
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync(ct);
        return connection;
    }
}

public sealed class DatabaseOptions
{
    public string Path { get; set; } = string.Empty;
}

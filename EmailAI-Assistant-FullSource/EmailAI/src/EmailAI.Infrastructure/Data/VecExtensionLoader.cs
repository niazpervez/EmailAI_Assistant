using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Data;

/// <summary>
/// Loads the optional sqlite-vec native extension (vec0.dll) when present.
/// </summary>
internal static class VecExtensionLoader
{
    private const long MinValidDllBytes = 4096;

    public static bool TryLoad(SqliteConnection connection, ILogger? logger = null)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "native", "vec0.dll");
        if (!IsValidExtensionFile(dllPath))
        {
            logger?.LogInformation(
                "sqlite-vec extension not found at {Path}; semantic search uses in-process fallback",
                dllPath);
            return false;
        }

        try
        {
            connection.EnableExtensions(true);
            connection.LoadExtension(Path.Combine(AppContext.BaseDirectory, "native", "vec0"));
            logger?.LogInformation("sqlite-vec extension loaded");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not load sqlite-vec; semantic search uses in-process fallback");
            return false;
        }
    }

    internal static bool IsValidExtensionFile(string dllPath)
    {
        if (!File.Exists(dllPath)) return false;

        try
        {
            var info = new FileInfo(dllPath);
            return info.Length >= MinValidDllBytes;
        }
        catch
        {
            return false;
        }
    }
}

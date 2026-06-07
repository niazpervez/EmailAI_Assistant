using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Data;

/// <summary>
/// Loads the optional sqlite-vec native extension when present.
/// </summary>
internal static class VecExtensionLoader
{
    private const long MinValidExtensionBytes = 4096;

    public static bool TryLoad(SqliteConnection connection, ILogger? logger = null)
    {
        var (directory, baseName) = GetNativeExtensionPaths();
        var extensionPath = Path.Combine(directory, baseName);

        if (!IsValidExtensionFile(extensionPath))
        {
            logger?.LogInformation(
                "sqlite-vec extension not found at {Path}; semantic search uses in-process fallback",
                extensionPath);
            return false;
        }

        try
        {
            connection.EnableExtensions(true);
            connection.LoadExtension(Path.Combine(directory, Path.GetFileNameWithoutExtension(baseName)));
            logger?.LogInformation("sqlite-vec extension loaded from {Path}", extensionPath);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not load sqlite-vec; semantic search uses in-process fallback");
            return false;
        }
    }

    internal static (string Directory, string FileName) GetNativeExtensionPaths()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "native");
        var fileName = OperatingSystem.IsWindows() ? "vec0.dll"
            : OperatingSystem.IsMacOS() ? "vec0.dylib"
            : "vec0.so";
        return (directory, fileName);
    }

    internal static bool IsValidExtensionFile(string extensionPath)
    {
        if (!File.Exists(extensionPath)) return false;

        try
        {
            var info = new FileInfo(extensionPath);
            return info.Length >= MinValidExtensionBytes;
        }
        catch
        {
            return false;
        }
    }
}

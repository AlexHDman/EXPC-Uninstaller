namespace ExpcUninstaller;

// Cache/diagnostics are per-user, independent of the portable executable's location.
// Persistent data is outside Cache so cache cleanup cannot remove settings or backups.
internal static class RuntimeStorage
{
    internal static string Root => Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    internal static string Resolve(string localAppData) => Path.Combine(localAppData, "EXPC", "Uninstaller");
    internal static string Data => Path.Combine(Root, "Data");
    internal static string Cache => Path.Combine(Root, "Cache");
    internal static string Logs => Path.Combine(Root, "Logs");
    internal static string UpdateCache => Path.Combine(Cache, "SelfUpdate");
}

internal static class DiagnosticLog
{
    internal static bool Enabled => IsEnabled(Environment.GetEnvironmentVariable("EXPC_DIAGNOSTICS"));
    internal static bool IsEnabled(string? value) => value == "1";
    internal static void Append(bool enabled, string directory, string file, Func<string> serialize)
    {
        if (!enabled) return; // No directory creation, file I/O or serialization by default.
        try
        {
            SelfUpdatePolicy.NoLinks(directory);
            var path = Path.Combine(directory, file);
            SelfUpdatePolicy.NoLinks(path);
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, serialize() + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* Optional diagnostics must not block startup or fall back beside the EXE. */ }
    }
}

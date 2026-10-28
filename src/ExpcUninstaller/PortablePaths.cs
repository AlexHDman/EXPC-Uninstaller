namespace ExpcUninstaller;
internal static class PortablePaths
{
    internal static string Root => RuntimeStorage.Root;
    internal static string LegacyRoot => Resolve(Environment.GetEnvironmentVariable("EXPC_PORTABLE_ORIGIN"), AppContext.BaseDirectory);
    internal static string Resolve(string? origin, string fallback) => string.IsNullOrWhiteSpace(origin) ? Path.GetFullPath(fallback) : Path.GetDirectoryName(Path.GetFullPath(origin))!;
    internal static void VerifyWritable()
    {
        SelfUpdatePolicy.NoLinks(RuntimeStorage.Data);
        Directory.CreateDirectory(RuntimeStorage.Data);
        var path = Path.Combine(RuntimeStorage.Data, ".expc-write-" + Guid.NewGuid().ToString("N"));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}

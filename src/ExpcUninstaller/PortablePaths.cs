namespace ExpcUninstaller;
internal static class PortablePaths
{
    internal static string Root => Resolve(Environment.GetEnvironmentVariable("EXPC_PORTABLE_ORIGIN"), AppContext.BaseDirectory);
    internal static string Resolve(string? origin, string fallback) => string.IsNullOrWhiteSpace(origin) ? Path.GetFullPath(fallback) : Path.GetDirectoryName(Path.GetFullPath(origin))!;
    internal static void VerifyWritable()
    {
        var path = Path.Combine(Root, ".expc-write-" + Guid.NewGuid().ToString("N"));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
}

namespace ExpcUninstaller;

internal static class UserDataMigration
{
    // Copy only: legacy settings/vault/backups remain available for rollback.
    // Existing destination wins; never overwrite a newer profile or unlock a vault.
    internal static void Ensure(string legacyRoot, string destination)
    {
        SelfUpdatePolicy.NoLinks(destination);
        var marker = Path.Combine(destination, ".legacy-import-v1.done");
        if (File.Exists(marker)) return;
        var sources = new List<(string Source, string Target)>();
        var oldData = Path.Combine(legacyRoot, "local-data");
        void Collect(string directory, string relative)
        {
            SelfUpdatePolicy.NoLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                SelfUpdatePolicy.NoLinks(path);
                var name = Path.Combine(relative, Path.GetFileName(path));
                if (Directory.Exists(path)) Collect(path, name);
                else sources.Add((path, Path.Combine(destination, name)));
            }
        }
        if (Directory.Exists(oldData)) Collect(oldData, "local-data");
        if (Directory.Exists(legacyRoot))
            foreach (var path in Directory.EnumerateFiles(legacyRoot, "accounts.expc-vault*"))
            {
                SelfUpdatePolicy.NoLinks(path);
                sources.Add((path, Path.Combine(destination, Path.GetFileName(path))));
            }
        if (sources.Count == 0) return;
        foreach (var (source, target) in sources)
        {
            SelfUpdatePolicy.NoLinks(target);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var pending = target + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, pending, false);
                if (SelfUpdatePolicy.Hash(source) != SelfUpdatePolicy.Hash(pending))
                    throw new IOException("Legacy data changed during migration; original retained");
                File.Move(pending, target, false);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        File.WriteAllText(marker, "1");
    }
}

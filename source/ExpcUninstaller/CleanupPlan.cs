namespace ExpcUninstaller;

internal sealed record CleanupEntry(JunkItem Root, string Path, bool Directory, string Section, string? ValueName = null, string? ValueFingerprint = null, string? ApprovedAnchor = null)
{
    public bool Registry => Root.Kind == "registry";
    public string DisplayPath => Registry ? (Root.Hive == Microsoft.Win32.RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER")
        + "\\" + Path + (ValueName == null ? "" : " :: " + (ValueName.Length == 0 ? "(Default)" : ValueName)) + " [" + Root.View + "]" : Path;
}

internal static class CleanupPlan
{
    internal static string Category(string path, bool registry)
    {
        // Registry settings are conservatively user data; no name-only claim that a key is disposable.
        if (registry || path.Split('\\').Any(part => new[] { "users", "appdata", "profile", "profiles", "user data", "userdata", "settings", "history", "bookmarks", "config" }
            .Any(word => part.Equals(word, StringComparison.OrdinalIgnoreCase) || part.StartsWith(word + ".", StringComparison.OrdinalIgnoreCase)))) return "CleanupUserData";
        return "CleanupFiles";
    }
    public static List<CleanupEntry> Expand(IEnumerable<JunkItem> roots)
    {
        var result = new List<CleanupEntry>();
        foreach (var root in roots.Distinct())
        {
            if (root.Kind == "registry")
            {
                using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(root.Hive, root.View);
                using var ownershipKey = hive.OpenSubKey(root.Path);
                var ownershipLocation = ownershipKey?.GetValue("InstallLocation") as string;
                var pending = new Stack<string>(); pending.Push(root.Path);
                while (pending.TryPop(out var path))
                {
                    if (result.Count > 100000) throw new IOException(L.T("Operation63"));
                    RegistryBoundary.NoLinks(root.Hive, root.View, path);
                    using var key = hive.OpenSubKey(path); if (key == null) throw new IOException(L.T("Operation27"));
                    if (path != root.Path && key.GetValue("InstallLocation") is string otherLocation && ownershipLocation != null &&
                        (!CleanupBoundary.LocalPath(otherLocation) || !CleanupBoundary.LocalPath(ownershipLocation) || !CleanupBoundary.Within(CleanupBoundary.Normalize(otherLocation), CleanupBoundary.Normalize(ownershipLocation))))
                        throw new IOException(L.T("Operation27") + " " + path);
                    result.Add(new(root, path, false, Category(path, true)));
                    foreach (var value in key.GetValueNames()) result.Add(new(root, path, false, Category(path, true), value, Fingerprint(key, value)));
                    foreach (var child in key.GetSubKeyNames()) pending.Push(path + "\\" + child);
                }
                continue;
            }
            if (root.Kind != "folder") continue;
            foreach (var path in CleanupBoundary.InspectTree(root.Path).Prepend(root.Path))
                result.Add(new(root, path, System.IO.Directory.Exists(path), Category(path, false)));
        }
        return result.Distinct().ToList();
    }
    internal static string Fingerprint(Microsoft.Win32.RegistryKey key, string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.GetValueKind(value) + ":" + System.Text.Json.JsonSerializer.Serialize(key.GetValue(value, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames)))));
    public static List<CleanupEntry> Build(IReadOnlyList<CleanupEntry> all, IReadOnlySet<CleanupEntry> selected)
    {
        static string Domain(CleanupEntry e) => e.Registry ? e.Root.Hive + "/" + e.Root.View + "/" : "file/";
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in all.Where(e => !selected.Contains(e)))
        {
            var path = entry.Path;
            while (true) { blocked.Add(Domain(entry) + path); var slash = path.LastIndexOf('\\'); if (slash < 0) break; path = path[..slash]; }
        }
        return all.Where(e => selected.Contains(e) && (e.ValueName != null || !blocked.Contains(Domain(e) + e.Path)))
            .Where(e => !(e.Registry && e.Path == e.Root.Path && e.ValueName == "InstallLocation"))
            .Select(e => e.Registry && e.ValueName == null && e.Path == e.Root.Path ? e with { ApprovedAnchor = all.FirstOrDefault(v => v.Root == e.Root && v.Path == e.Path && v.ValueName == "InstallLocation" && selected.Contains(v))?.ValueFingerprint } : e)
            .OrderBy(e => e.Registry ? 0 : 1).ThenByDescending(e => e.Path.Length).ThenBy(e => e.ValueName == "InstallLocation" ? 1 : e.ValueName != null ? 0 : 2).ToList();
    }

    public static async Task Delete(AppEntry app, CleanupEntry entry)
    {
        if (ProductActions.HasKnownRunning(app)) throw new IOException(L.T("KnownUninstallerRunning"));
        // Revalidate the original BCU ownership candidate for every individual operation.
        if (!BcuTargetedJunkAdapter.Find(app).Contains(entry.Root)) throw new IOException(L.T("Operation27"));
        if (entry.Registry)
        {
            if (!CleanupBoundary.Within(entry.Path, entry.Root.Path)) throw new IOException(L.T("Operation27"));
            OperationLog.Write("cleanup-intent", app.Id, entry.DisplayPath);
            // DeleteSubKey refuses new descendants; never elevate through recursive reg.exe delete here.
            await RegistryBackup.Delete(entry.Root.Hive, entry.Root.View, entry.Path, leafOnly: true, valueName: entry.ValueName, fingerprint: entry.ValueFingerprint, approvedAnchor: entry.ApprovedAnchor);
            OperationLog.Write("cleanup-result", app.Id, "deleted: " + entry.DisplayPath); return;
        }
        if (!CleanupBoundary.Within(entry.Path, entry.Root.Path)) throw new IOException(L.T("Operation27"));
        OperationLog.Write("cleanup-intent", app.Id, entry.DisplayPath);
        DeleteFileEntry(entry);
        OperationLog.Write("cleanup-result", app.Id, "deleted: " + entry.DisplayPath);
    }
    internal static void DeleteFileEntry(CleanupEntry entry)
    {
        if (entry.Registry || !CleanupBoundary.Within(entry.Path, entry.Root.Path)) throw new IOException(L.T("Operation27"));
        CleanupBoundary.NoReparse(entry.Path);
        // Never recursively delete a parent. Unchecked or newly created descendants survive even a stale plan.
        if (entry.Directory) System.IO.Directory.Delete(entry.Path, false);
        else File.Delete(entry.Path);
    }
}

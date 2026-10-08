namespace ExpcUninstaller;

internal sealed record CleanupEntry(JunkItem Root, string Path, bool Directory, string Section)
{
    public bool Registry => Root.Kind == "registry";
    public string DisplayPath => Registry ? (Root.Hive == Microsoft.Win32.RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER")
        + "\\" + Path + " [" + Root.View + "]" : Path;
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
            if (root.Kind == "registry") { result.Add(new(root, root.Path, false, Category(root.Path, true))); continue; }
            if (root.Kind != "folder") continue;
            foreach (var path in CleanupBoundary.InspectTree(root.Path).Prepend(root.Path))
                result.Add(new(root, path, System.IO.Directory.Exists(path), Category(path, false)));
        }
        return result.Distinct().ToList();
    }
    public static List<CleanupEntry> Build(IReadOnlyList<CleanupEntry> all, IReadOnlySet<CleanupEntry> selected)
    {
        static string Domain(CleanupEntry e) => e.Registry ? e.Root.Hive + "/" + e.Root.View + "/" : "file/";
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in all.Where(e => !selected.Contains(e)))
        {
            var path = entry.Path;
            while (true) { blocked.Add(Domain(entry) + path); var slash = path.LastIndexOf('\\'); if (slash < 0) break; path = path[..slash]; }
        }
        return all.Where(e => selected.Contains(e) && !blocked.Contains(Domain(e) + e.Path))
            .OrderBy(e => e.Registry ? 0 : 1).ThenByDescending(e => e.Path.Length).ToList();
    }

    public static async Task Delete(AppEntry app, CleanupEntry entry)
    {
        // Revalidate the original BCU ownership candidate for every individual operation.
        if (!BcuTargetedJunkAdapter.Find(app).Contains(entry.Root)) throw new IOException(L.T("Operation27"));
        if (entry.Registry)
        {
            if (entry.Path != entry.Root.Path) throw new IOException(L.T("Operation27"));
            OperationLog.Write("cleanup-intent", app.Id, entry.DisplayPath);
            // DeleteSubKey refuses new descendants; never elevate through recursive reg.exe delete here.
            await RegistryBackup.Delete(entry.Root.Hive, entry.Root.View, entry.Path, leafOnly: true);
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

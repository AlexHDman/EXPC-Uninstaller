// Targeted adaptation of BCU JunkCreatorBase / UninstallerLocationScanner / RegistryKeyJunk.
// Copyright (c) 2017 Marcin Szeniak. Apache-2.0; see Licence.txt and NOTICE.
// EXPC narrows candidates to explicit ownership and revalidates boundaries before each deletion.
using System.Diagnostics;
using Klocman.Tools;
using Microsoft.Win32;

namespace ExpcUninstaller;

internal sealed record JunkItem(string Kind, string Path, string Evidence, RegistryHive Hive = RegistryHive.CurrentUser,
    RegistryView View = RegistryView.Registry64);

internal static class CleanupBoundary
{
    public static bool LocalPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length > 3 &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
        path.AsSpan(2).IndexOfAny(':', '*', '?') < 0 && !path.Contains('"') && !path.Contains("..") && !path.Contains('/') &&
        !path.Any(char.IsControl) && path.TrimEnd('\\')[3..].Split('\\').All(x => x.Length > 0 && x == x.TrimEnd(' ', '.'));
    public static string Normalize(string path) => System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))).TrimEnd('\\');
    public static bool Within(string child, string parent) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    public static bool Overlap(string a, string b) => Within(a, b) || Within(b, a);
    public static bool SafeDirectory(AppEntry app, IEnumerable<AppEntry> all)
    {
        if (app.Store || app.Protected || app.Metadata == null || !LocalPath(app.Metadata.Location)) return false;
        var path = Normalize(app.Metadata.Location);
        var protectedRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), @"C:\Apps", PortablePaths.Root, PortablePaths.LegacyRoot, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppContext.BaseDirectory.TrimEnd('\\') };
        if (path.Split('\\').Length < 3 || protectedRoots.Where(x => x.Length > 0).Any(x => Within(x, path)) ||
            Within(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ||
            path.Split('\\').Any(x => new[] { "Common Files", "WindowsApps", "Package Cache", "Microsoft", "Windows", "System32", "Installer" }.Contains(x, StringComparer.OrdinalIgnoreCase))) return false;
        // A product-specific leaf is additional evidence, not a name-only search.
        static string Token(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var leaf = Token(System.IO.Path.GetFileName(path));
        var name = app.Name;
        if (!string.IsNullOrWhiteSpace(app.Version)) name = name.Replace(app.Version, "", StringComparison.OrdinalIgnoreCase).Replace("version", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (leaf.Length < 3 || (leaf != Token(app.Name) && leaf != Token(name))) return false;
        if (new[] { "Shared", "Components", "SDK", "Runtime", "Cache" }.Any(x => path.Split('\\').Contains(x, StringComparer.OrdinalIgnoreCase))) return false;
        // The installation root AND uninstaller location must agree. Name resemblance alone never qualifies.
        ProcessStartCommand command;
        try { command = UninstallCommand.Parse(app.Metadata.Uninstall, _ => true); } catch { return false; }
        if (!Within(command.FileName, path))
        {
            var icon = LazyIcons.Parse(app.Metadata.Icon).Path;
            if (app.Metadata.WindowsInstaller != "1" || !Guid.TryParse(app.Metadata.KeyName, out _) ||
                !System.IO.Path.GetFileName(command.FileName).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                !LocalPath(icon) || !Within(icon, path)) return false;
        }
        if (!string.IsNullOrWhiteSpace(app.Publisher) && System.IO.Path.GetFileName(path).Equals(app.Publisher.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var other in all.Where(x => x.Metadata != null && !x.Sources.Intersect(app.Sources).Any()))
        {
            var m = other.Metadata!;
            if (LocalPath(m.Location) && Overlap(Normalize(m.Location), path)) return false;
            try { if (Within(UninstallCommand.Parse(m.Uninstall, _ => true).FileName, path)) return false; } catch { }
            var icon = m.Icon.Trim('"').Split(',')[0];
            if (LocalPath(icon) && Within(icon, path)) return false;
        }
        return true;
    }
    public static void NoReparse(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(L.T("Operation62") + current.FullName);
    }
    public static List<string> InspectTree(string path, CancellationToken token = default)
    {
        NoReparse(path);
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            NoReparse(directory);
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (result.Count > 100000) throw new IOException(L.T("Operation63"));
                var flags = File.GetAttributes(item);
                if ((flags & FileAttributes.ReparsePoint) != 0) throw new IOException(L.T("Operation64"));
                result.Add(item);
                if ((flags & FileAttributes.Directory) != 0) pending.Push(item);
            }
        }
        return result;
    }
}

internal sealed record TargetedScan(List<JunkItem> Items, string? Warning, int CheckedScopes);

internal static class BcuTargetedJunkAdapter
{
    public static string[] CaptureDataPaths(AppEntry app)
    {
        if (app.Metadata == null || app.Store) return app.LinkedDataPaths.ToArray();
        var result = new List<string>(app.LinkedDataPaths);
        try
        {
            if (!SafeSegment(app.Publisher) || !SafeSegment(app.Name)) return result.ToArray();
            RegistryBoundary.NoLinks(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\" + app.Publisher.Trim() + @"\" + app.Name.Trim());
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\" + app.Publisher.Trim() + @"\" + app.Name.Trim());
            if (key?.GetValue("InstallLocation") is not string location || !CleanupBoundary.LocalPath(location) ||
                !CleanupBoundary.LocalPath(app.Metadata.Location) || !CleanupBoundary.Normalize(location).Equals(CleanupBoundary.Normalize(app.Metadata.Location), StringComparison.OrdinalIgnoreCase)) return result.ToArray();
            foreach (var field in new[] { "UserDataPath", "DataPath", "ProfilePath" })
                if (key.GetValue(field) is string path && SafeLinkedData(app, path)) result.Add(CleanupBoundary.Normalize(path));
        } catch (Exception ex) { throw new IOException(L.T("DataScopeUnavailable"), ex); }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static bool SafeUserData(string path) => CleanupBoundary.LocalPath(path) && new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }
        .Any(root => root.Length > 0 && CleanupBoundary.Within(path, root) && path.TrimEnd('\\').Length > root.Length + 2);
    internal static bool SafeLinkedData(AppEntry app, string path)
    {
        if (!SafeUserData(path)) return false;
        static string Token(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var name = app.Version.Length == 0 ? app.Name : app.Name.Replace(app.Version, "", StringComparison.OrdinalIgnoreCase).Replace("version", "", StringComparison.OrdinalIgnoreCase).Trim();
        var leaf = Token(System.IO.Path.GetFileName(path.TrimEnd('\\')));
        return leaf.Length >= 3 && leaf != Token(app.Publisher) && (leaf == Token(name) || leaf == Token(app.Name));
    }
    internal static string[] CaptureInstallPaths(AppEntry app, IEnumerable<AppEntry> catalog)
    {
        if (app.Metadata is not { } metadata) return [];
        var candidates = new List<string>(app.ConfirmedInstallPaths) { metadata.Location };
        var icon = LazyIcons.Parse(metadata.Icon.Length == 0 ? app.IconPath ?? "" : metadata.Icon).Path;
        if (CleanupBoundary.LocalPath(icon)) candidates.Add(Path.GetDirectoryName(icon)!);
        try { var command = UninstallCommand.Parse(metadata.Uninstall, _ => true); if (CleanupBoundary.LocalPath(command.FileName)) candidates.Add(Path.GetDirectoryName(command.FileName)!); } catch { }
        return candidates.Where(CleanupBoundary.LocalPath).Select(CleanupBoundary.Normalize).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => CleanupBoundary.SafeDirectory(app with { Metadata = metadata with { Location = path } }, catalog)).ToArray();
    }
    public static List<JunkItem> Find(AppEntry app, CancellationToken token = default)
    {
        var result = Scan(app, token);
        if (result.CheckedScopes == 0 && result.Warning != null) throw new IOException(result.Warning);
        return result.Items;
    }
    internal static TargetedScan Scan(AppEntry app, CancellationToken token = default, CatalogResult? catalogOverride = null)
    {
        token.ThrowIfCancellationRequested();
        if (app.Store || app.Protected || app.Metadata == null) return new([], L.T("CleanupScopeUnknown"), 0);
        if (ProductIdentity.IsRegistered(app)) ProductIdentity.RequireUnchanged(app);
        var catalog = catalogOverride ?? FastCatalog.ReadRegistry();
        if (catalog.ReadErrors != 0) throw new IOException(L.T("Operation65"));
        var results = new List<JunkItem>(); var warnings = new List<string>(); int checkedScopes = 0;
        void CheckScope(Action action)
        {
            token.ThrowIfCancellationRequested();
            try { action(); checkedScopes++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { warnings.Add(ex.Message); }
        }
        static bool ExistingDirectory(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        var paths = CaptureInstallPaths(app, catalog.Entries);
        if (paths.Length == 0) warnings.Add(L.T("CleanupScopeUnknown"));
        foreach (var path in paths) CheckScope(() =>
        {
            if (!ExistingDirectory(path)) return;
            CleanupBoundary.InspectTree(path, token); results.Add(new("folder", path, "Operation66"));
        });
        var publisher = app.Publisher.Trim(); var name = app.Name.Trim();
        if (!SafeSegment(publisher) || !SafeSegment(name) || publisher.Equals(name, StringComparison.OrdinalIgnoreCase)) warnings.Add(L.T("CleanupRegistryScopeUnknown"));
        else if (paths.Length > 0)
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in hive == RegistryHive.CurrentUser ? new[] { RegistryView.Registry64 } : new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    CheckScope(() =>
                    {
                        var path = @"SOFTWARE\" + publisher + @"\" + name;
                        using var root = RegistryKey.OpenBaseKey(hive, view); RegistryBoundary.NoLinks(hive, view, path);
                        using var key = root.OpenSubKey(path); if (key == null) return;
                        var location = key.GetValue("InstallLocation", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (location != null && CleanupBoundary.LocalPath(location) && paths.Contains(CleanupBoundary.Normalize(location), StringComparer.OrdinalIgnoreCase))
                            results.Add(new("registry", path, "Operation67", hive, view));
                        else warnings.Add(L.T("CleanupRegistryScopeUnknown") + ": " + hive + @"\" + path);
                    });
        var linked = new List<string>(app.LinkedDataPaths);
        // Missing InstallLocation must not suppress already captured data paths or other scoped checks.
        foreach (var path in paths) CheckScope(() => linked.AddRange(CaptureDataPaths(app with { Metadata = app.Metadata with { Location = path } })));
        foreach (var data in linked.Distinct(StringComparer.OrdinalIgnoreCase).Where(path => SafeLinkedData(app, path))) CheckScope(() =>
        {
            if (!ExistingDirectory(data)) return;
            if (catalog.Entries.Any(other => !other.Sources.Intersect(app.Sources).Any() && other.Metadata is { } m && CleanupBoundary.LocalPath(m.Location) && CleanupBoundary.Overlap(m.Location, data)))
            { warnings.Add(L.T("CleanupScopeUnknown") + ": " + data); return; }
            CleanupBoundary.InspectTree(data, token); results.Add(new("folder", data, "LinkedUserData"));
        });
        return new(results.Distinct().ToList(), warnings.Count == 0 ? null : string.Join("\n", warnings.Distinct()), checkedScopes);
    }
    private static bool SafeSegment(string value) => value.Length >= 3 && value.IndexOfAny(['\\', '/', '"', '\r', '\n']) < 0 && !new[] { "Microsoft", "Windows", "Classes" }.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static async Task Delete(AppEntry app, JunkItem item)
    {
        OperationLog.Write("cleanup-intent", app.Id, item.Path);
        if (!Find(app).Contains(item)) throw new IOException(L.T("Operation27"));
        if (item.Kind == "registry") await RegistryBackup.Delete(item.Hive, item.View, item.Path);
        else if (item.Kind == "folder")
        {
            var contents = CleanupBoundary.InspectTree(item.Path);
            // Explicit postorder, never Directory.Delete(recursive:true), never follow reparse points.
            foreach (var path in contents.OrderByDescending(x => x.Length))
            {
                CleanupBoundary.NoReparse(System.IO.Path.GetDirectoryName(path)!);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(L.T("Operation28"));
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, false);
                else File.Delete(path);
            }
            CleanupBoundary.NoReparse(item.Path);
            Directory.Delete(item.Path, false);
        }
        else throw new InvalidOperationException(L.T("Operation29"));
        OperationLog.Write("cleanup-result", app.Id, "deleted: " + item.Path);
    }
}

internal static class RegistryBackup
{
    public static async Task Delete(RegistryHive hive, RegistryView view, string path, bool leafOnly = false, string? valueName = null, string? fingerprint = null, string? approvedAnchor = null)
    {
        RegistryBoundary.NoLinks(hive, view, path);
        var full = (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + @"\" + path;
        if (hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) || path.IndexOfAny(['"', '\r', '\n']) >= 0 || path.Split('\\').Length < 3)
            throw new IOException(L.T("Operation68"));
        var directory = System.IO.Path.Combine(OperationLog.DirectoryPath, "registry-backups");
        Directory.CreateDirectory(directory);
        var backup = System.IO.Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + view + "-" + PathTools.SanitizeFileName(System.IO.Path.GetFileName(path)) + ".reg");
        var regExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            view == RegistryView.Registry32 && Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32", "reg.exe");
        using (var export = Process.Start(new ProcessStartInfo(regExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "export", full, backup }
        }) ?? throw new IOException(L.T("Operation69")))
        {
            await export.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (export.ExitCode != 0 || !File.Exists(backup) || new FileInfo(backup).Length < 40) throw new IOException(L.T("Operation70"));
        }
        File.WriteAllText(backup + ".txt", "Restore using reg.exe import with /reg:" + (view == RegistryView.Registry32 ? "32" : "64") + Environment.NewLine + full);
        OperationLog.Write("registry-backup", full, backup);
        RegistryBoundary.NoLinks(hive, view, path);
        using var root = RegistryKey.OpenBaseKey(hive, view);
        try
        {
            if (valueName != null)
            {
                using var key = root.OpenSubKey(path, true) ?? throw new IOException(L.T("Operation27"));
                if (!key.GetValueNames().Contains(valueName) || CleanupPlan.Fingerprint(key, valueName) != fingerprint) throw new IOException(L.T("Operation27"));
                key.DeleteValue(valueName, true); return;
            }
            if (leafOnly)
            {
                using (var key = root.OpenSubKey(path))
                {
                    if (key == null || key.SubKeyCount != 0) throw new IOException(L.T("Operation27"));
                    var values = key.GetValueNames();
                    if (values.Length != 0 && !(values.Length == 1 && values[0] == "InstallLocation" && approvedAnchor != null && CleanupPlan.Fingerprint(key, "InstallLocation") == approvedAnchor)) throw new IOException(L.T("Operation27"));
                }
                root.DeleteSubKey(path, true);
            }
            else root.DeleteSubKeyTree(path, true);
        }
        catch (UnauthorizedAccessException) when (!leafOnly && hive == RegistryHive.LocalMachine)
        {
            // Only this approved key is elevated, never the shell, and only after an access-denied result.
            using var elevated = Process.Start(new ProcessStartInfo(regExe, "delete \"" + full + "\" /f") { UseShellExecute = true, Verb = "runas" })
                ?? throw new IOException(L.T("Operation30"));
            await elevated.WaitForExitAsync();
            if (elevated.ExitCode != 0) throw new IOException(L.T("Operation31") + elevated.ExitCode);
        }
        using var check = root.OpenSubKey(path);
        if (check != null) throw new IOException(L.T("Operation32"));
    }
    public static async Task RemoveRecord(AppEntry app)
    {
        if (app.Protected || app.Store) throw new InvalidOperationException(L.T("Operation33"));
        ProductIdentity.RequireUnchanged(app);
        OperationLog.Write("record-only-start", app.Id, "Files remain");
        foreach (var registration in app.Sources.Select(Registration.Parse).Distinct())
        {
            using (var key = registration.Open()) { if (key == null) continue; }
            ProductIdentity.RequireUnchanged(app with { Sources = [registration.Hive + "/" + registration.View + "/" + registration.Key] });
            await Delete(registration.Hive, registration.View, registration.Path);
        }
        OperationLog.Write("record-only-result", app.Id, "Registration removed; files remain");
    }
}

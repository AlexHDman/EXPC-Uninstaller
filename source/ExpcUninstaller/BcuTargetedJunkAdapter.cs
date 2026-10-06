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
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
        if (!Within(command.FileName, path)) return false;
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
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Путь содержит ссылку/junction: " + current.FullName);
    }
    public static List<string> InspectTree(string path)
    {
        NoReparse(path);
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            NoReparse(directory);
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                if (result.Count > 100000) throw new IOException("Слишком много объектов для безопасной адресной проверки.");
                var flags = File.GetAttributes(item);
                if ((flags & FileAttributes.ReparsePoint) != 0) throw new IOException("Внутри есть ссылка/junction; каталог исключён.");
                result.Add(item);
                if ((flags & FileAttributes.Directory) != 0) pending.Push(item);
            }
        }
        return result;
    }
}

internal static class BcuTargetedJunkAdapter
{
    public static List<JunkItem> Find(AppEntry app)
    {
        if (app.Store || app.Protected || app.Metadata == null) return [];
        if (ProductIdentity.IsRegistered(app)) ProductIdentity.RequireUnchanged(app);
        var catalog = FastCatalog.ReadRegistry();
        if (catalog.ReadErrors != 0) throw new IOException("Не удалось проверить все регистрации; поиск остатков остановлен.");
        var results = new List<JunkItem>();
        if (CleanupBoundary.SafeDirectory(app, catalog.Entries))
        {
            var path = CleanupBoundary.Normalize(app.Metadata.Location);
            if (Directory.Exists(path))
            {
                CleanupBoundary.InspectTree(path);
                results.Add(new("folder", path, "InstallLocation + путь штатного EXE; нет пересечений с другими продуктами"));
            }
        }
        // Only exact product subkeys with a matching explicit InstallLocation value; never delete a publisher root.
        if (!CleanupBoundary.SafeDirectory(app, catalog.Entries)) return results;
        var publisher = app.Publisher.Trim(); var name = app.Name.Trim();
        if (!SafeSegment(publisher) || !SafeSegment(name) || publisher.Equals(name, StringComparison.OrdinalIgnoreCase)) return results;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in hive == RegistryHive.CurrentUser ? new[] { RegistryView.Registry64 } : new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                var path = @"SOFTWARE\" + publisher + @"\" + name;
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(path);
                if (key == null || key.GetSubKeyNames().Length != 0) continue; // No nested product/component keys in Phase 03.
                var location = key.GetValue("InstallLocation", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (location != null && CleanupBoundary.LocalPath(location) && CleanupBoundary.Normalize(location).Equals(CleanupBoundary.Normalize(app.Metadata.Location), StringComparison.OrdinalIgnoreCase))
                    results.Add(new("registry", path, "Точный ключ продукта + совпадающий InstallLocation; без вложенных ключей", hive, view));
            }
        return results.Distinct().ToList();
    }
    private static bool SafeSegment(string value) => value.Length >= 3 && value.IndexOfAny(['\\', '/', '"', '\r', '\n']) < 0 && !new[] { "Microsoft", "Windows", "Classes" }.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static async Task Delete(AppEntry app, JunkItem item)
    {
        OperationLog.Write("cleanup-intent", app.Id, item.Path);
        if (!Find(app).Contains(item)) throw new IOException("Границы или принадлежность объекта изменились. Повторите поиск.");
        if (item.Kind == "registry") await RegistryBackup.Delete(item.Hive, item.View, item.Path);
        else if (item.Kind == "folder")
        {
            var contents = CleanupBoundary.InspectTree(item.Path);
            // Explicit postorder, never Directory.Delete(recursive:true), never follow reparse points.
            foreach (var path in contents.OrderByDescending(x => x.Length))
            {
                CleanupBoundary.NoReparse(System.IO.Path.GetDirectoryName(path)!);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Появилась ссылка; удаление остановлено.");
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, false);
                else File.Delete(path);
            }
            CleanupBoundary.NoReparse(item.Path);
            Directory.Delete(item.Path, false);
        }
        else throw new InvalidOperationException("Неизвестный вид остатка.");
        OperationLog.Write("cleanup-result", app.Id, "deleted: " + item.Path);
    }
}

internal static class RegistryBackup
{
    public static async Task Delete(RegistryHive hive, RegistryView view, string path)
    {
        var full = (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + @"\" + path;
        if (hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) || path.IndexOfAny(['"', '\r', '\n']) >= 0 || path.Split('\\').Length < 3)
            throw new IOException("Небезопасный путь реестра.");
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
        }) ?? throw new IOException("Не запущен экспорт реестра."))
        {
            await export.WaitForExitAsync();
            if (export.ExitCode != 0 || !File.Exists(backup) || new FileInfo(backup).Length < 40) throw new IOException("Резервная копия не создана; удаление запрещено.");
        }
        File.WriteAllText(backup + ".txt", "Restore using reg.exe import with /reg:" + (view == RegistryView.Registry32 ? "32" : "64") + Environment.NewLine + full);
        OperationLog.Write("registry-backup", full, backup);
        using var root = RegistryKey.OpenBaseKey(hive, view);
        try { root.DeleteSubKeyTree(path, true); }
        catch (UnauthorizedAccessException) when (hive == RegistryHive.LocalMachine)
        {
            // Only this approved key is elevated, never the shell, and only after an access-denied result.
            using var elevated = Process.Start(new ProcessStartInfo(regExe, "delete \"" + full + "\" /f") { UseShellExecute = true, Verb = "runas" })
                ?? throw new IOException("Не запущено удаление ключа.");
            await elevated.WaitForExitAsync();
            if (elevated.ExitCode != 0) throw new IOException("Удаление ключа: код " + elevated.ExitCode);
        }
        using var check = root.OpenSubKey(path);
        if (check != null) throw new IOException("Ключ остался; удаление не подтверждено.");
    }
    public static async Task RemoveRecord(AppEntry app)
    {
        if (app.Protected || app.Store) throw new InvalidOperationException("Действие недоступно.");
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

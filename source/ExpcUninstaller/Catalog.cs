using System.Globalization;
using System.Security;
using Microsoft.Win32;

namespace ExpcUninstaller;

internal sealed record AppEntry(string Id, string Name, string Version, long? SizeBytes,
    DateTime? Installed, string? IconPath, string Publisher, bool Store = false);

// No discovery pipeline, MSI queries, startup enumeration, directory traversal or writes.
internal sealed record RegistryMetadata(string KeyName, string Name, string Version, string Publisher,
    string Location, string Uninstall, string QuietUninstall, string Icon, string Date,
    long? SizeKb, string SystemComponent, string NoRemove);

internal sealed record CatalogResult(List<AppEntry> Entries, int ReadErrors, int Duplicates = 0);

internal static class FastCatalog
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static CatalogResult ReadRegistry()
    {
        var records = new List<(string Source, RegistryMetadata Data)>();
        var errors = 0;
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in views)
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(UninstallPath, writable: false);
                    if (uninstall == null) continue;
                    foreach (var keyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = uninstall.OpenSubKey(keyName, writable: false);
                            if (key == null) { errors++; continue; }
                            string Text(string name) => Convert.ToString(key.GetValue(name, null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames), CultureInfo.InvariantCulture) ?? "";
                            var name = Text("DisplayName");
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            records.Add(($"{hive}/{view}/{keyName}", new RegistryMetadata(keyName, name,
                                Text("DisplayVersion"), Text("Publisher"), Text("InstallLocation"),
                                Text("UninstallString"), Text("QuietUninstallString"), Text("DisplayIcon"),
                                Text("InstallDate"), ParseSizeKb(key.GetValue("EstimatedSize")),
                                Text("SystemComponent"), Text("NoRemove"))));
                        }
                        catch (Exception ex) when (IsReadError(ex)) { errors++; }
                    }
                }
                catch (Exception ex) when (IsReadError(ex)) { errors++; }
            }
        var entries = Deduplicate(records);
        return new CatalogResult(entries, errors, records.Count - entries.Count);
    }

    private static bool IsReadError(Exception ex) => ex is SecurityException or UnauthorizedAccessException or IOException;

    internal static List<AppEntry> Deduplicate(IEnumerable<(string Source, RegistryMetadata Data)> records)
    {
        // Exact metadata equality only; same product name never suffices to merge records.
        var seen = new HashSet<RegistryMetadata>();
        return records.Where(x => seen.Add(x.Data)).Select(x => new AppEntry(x.Source,
            x.Data.Name.Trim(), x.Data.Version, ToBytes(x.Data.SizeKb), ParseDate(x.Data.Date),
            x.Data.Icon, x.Data.Publisher)).ToList();
    }

    internal static long? ParseSizeKb(object? value) => value switch
    {
        int n => (uint)n, // Registry DWORD is unsigned even though GetValue returns Int32.
        long n when n >= 0 => n,
        string s when long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 => n,
        _ => null
    };

    internal static long? ToBytes(long? kb) => kb is >= 0 and <= long.MaxValue / 1024 ? kb * 1024 : null;
    internal static DateTime? ParseDate(string value) => DateTime.TryParseExact(value, "yyyyMMdd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static CatalogResult ReadStore()
    {
        // Reuse BCU's current-user Store query only, on the first explicit Store selection.
        // Never invoke the helper executable or AppManager.UninstallApp.
        var apps = StoreAppHelper.AppManager.QueryApps().Select(app =>
        {
            var parts = app.FullName.Split('_');
            var version = parts.Length > 1 && System.Version.TryParse(parts[1], out _) ? parts[1] : "";
            return new AppEntry(app.FullName, app.DisplayName, version, null, null, app.Logo,
                app.PublisherDisplayName, true);
        }).DistinctBy(x => x.Id).ToList();
        return new CatalogResult(apps, 0);
    }
}

internal static class CatalogView
{
    public static List<AppEntry> Select(IEnumerable<AppEntry> entries, string search, int column, bool descending)
    {
        var filtered = entries.Where(x => x.Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase));
        IOrderedEnumerable<AppEntry> sorted = column switch
        {
            1 => Order(filtered, x => x.SizeBytes, descending),
            2 => Order(filtered, x => x.Installed, descending),
            3 => filtered.OrderBy(x => x.Version, new NaturalVersionComparer(descending)),
            _ => Order(filtered, x => x.Name, descending)
        };
        return sorted.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    private static IOrderedEnumerable<AppEntry> Order<T>(IEnumerable<AppEntry> items, Func<AppEntry, T> key, bool desc)
        => desc ? items.OrderByDescending(key) : items.OrderBy(key);

    private sealed class NaturalVersionComparer(bool descending) : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            var parsedA = System.Version.TryParse(a, out var va);
            var parsedB = System.Version.TryParse(b, out var vb);
            var comparison = parsedA && parsedB ? va!.CompareTo(vb)
                : parsedA != parsedB ? parsedA.CompareTo(parsedB)
                : StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
            return descending ? -comparison : comparison;
        }
    }
}

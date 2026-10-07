using System.Diagnostics;
using System.Text.Json;
using ExpcUninstaller;

// Read-only verification tool, never included in the portable archive.
if (args.Length != 2 || args[0] is not ("--network-report" or "--manager-report" or "--icon-status-report" or "--vault-send")) return 2;
if (args[0] == "--vault-send") return VaultTransport.Send(args[1]);
var reportPath = Path.GetFullPath(args[1]);
var rules = VersionChecker.Rules();
var catalog = FastCatalog.ReadRegistry();
if (args[0] == "--icon-status-report")
{
    var count = new Dictionary<string, int>();
    var store = FastCatalog.ReadStore();
    foreach (var pair in new[] { ("Programs", catalog), ("Store", store) })
    {
        int direct = 0, resolved = 0;
        foreach (var app in pair.Item2.Entries)
        { using var old = LazyIcons.Load(app.IconPath); using var improved = LazyIcons.Load(app); if (old != null) direct++; if (improved != null) resolved++; }
        count[pair.Item1 + "Direct"] = direct; count[pair.Item1 + "Resolved"] = resolved; count[pair.Item1 + "Total"] = pair.Item2.Entries.Count;
    }
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4)); var manager = new PackageManagers(); await manager.Discover(timeout.Token);
    var newlyMapped = catalog.Entries.Count(a => manager.Packages.Any(p => PackageManagers.Matches(a, p) && !p.ProductCodes.Contains(a.Metadata!.KeyName, StringComparer.OrdinalIgnoreCase)));
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Icons = count, GuidMappedAfterFix = newlyMapped }, new JsonSerializerOptions { WriteIndented = true })); return 0;
}
if (args[0] == "--manager-report")
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var managers = new PackageManagers(); await managers.Discover(timeout.Token);
    var results = catalog.Entries.Select(a => managers.Evaluate(a, VersionChecker.Resolve(a, rules), false)).ToArray();
    var repeated = FastCatalog.ReadRegistry();
    var store = FastCatalog.ReadStore();
    var search = catalog.Entries.FirstOrDefault()?.Name ?? "EXPC";
    var matches = CatalogView.Select(catalog.Entries, search, 0, false);
    var sorted = CatalogView.Select(catalog.Entries, "", 1, false);
    var reversed = CatalogView.Select(catalog.Entries, "", 1, true);
    var data = new { Utc = DateTimeOffset.UtcNow, Count = catalog.Entries.Count, catalog.ReadErrors, RefreshCount = repeated.Entries.Count,
        StoreCount = store.Entries.Count, StoreReadErrors = store.ReadErrors, SearchCount = matches.Count,
        SortCountPreserved = sorted.Count == reversed.Count && sorted.Count == catalog.Entries.Count,
        managers.Availability, ManagedPackages = managers.Packages.Count, Installable = results.Count(x => x.CanInstall),
        Statuses = results.GroupBy(x => x.State.ToString()).ToDictionary(x => x.Key, x => x.Count()) };
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(data)); return 0;
}
var samples = new Dictionary<string, AppEntry>
{
    ["cryptopro-csp"] = new("fixture", "КриптоПро CSP", "5.0.13800", null, null, null, "Crypto-Pro LLC"),
    ["cryptopro-browser-plugin"] = new("fixture", "КриптоПро ЭЦП Browser plug-in", "2.0.15700", null, null, null, "Crypto-Pro LLC"),
    ["rutoken-drivers"] = new("fixture", "Драйверы Рутокен", "5.0.1.0", null, null, null, "Компания \"Актив\""),
    ["rutoken-plugin"] = new("fixture", "Рутокен Плагин", "4.11.2.0", null, null, null, "Aktiv Company"),
    ["bcu"] = new("fixture", "BCUninstaller 6.3.0.0", "6.3.0.0", null, null, null, "Marcin Szeniak"),
    ["7zip"] = new("fixture", "7-Zip 25.01 (x64)", "25.01", null, null, null, "Igor Pavlov"),
    ["kontur-plugin"] = new("fixture", "Контур.Плагин", "4.15.0.5950", null, null, null, "SKB Kontur")
};
var output = new List<object>();
foreach (var rule in rules)
{
    var actual = catalog.Entries.FirstOrDefault(rule.Matches);
    if (rule.Provider == "1c")
    { output.Add(new { rule.Id, State = "NOT TESTED", Reason = "Requires authorized account, selected product page and subscription; no secrets used by verification tool" }); continue; }
    var app = actual ?? samples[rule.Id];
    var timer = Stopwatch.StartNew();
    var result = await new VersionChecker().Check(app, rule, CancellationToken.None);
    output.Add(new { rule.Id, Identity = actual == null ? "fixture metadata; LIVE network" : "installed product; LIVE network",
        ElapsedMs = timer.Elapsed.TotalMilliseconds, State = result.State.ToString(), result.Installed, result.Latest, result.Source,
        result.CheckedAt, result.AttemptedAt, result.Reason, result.Compatibility });
    Console.WriteLine($"{rule.Id}: {result.State}, {result.Latest}, {timer.ElapsedMilliseconds} ms");
}
// Optional source, explicit package identifier only; no inventory upload or source agreements accepted.
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(22)))
{
    try { output.Add(new { Id = "winget-bcu", Version = await VersionChecker.Winget("Klocman.BulkCrapUninstaller", timeout.Token) }); }
    catch (Exception ex) { output.Add(new { Id = "winget-bcu", Error = ex.Message }); }
}
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, catalog.ReadErrors, Count = catalog.Entries.Count, Results = output }, new JsonSerializerOptions { WriteIndented = true }));
return 0;

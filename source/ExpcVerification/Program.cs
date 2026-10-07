using System.Diagnostics;
using System.Text.Json;
using ExpcUninstaller;

// Read-only verification tool, never included in the portable archive.
if (args.Length != 2 || args[0] != "--network-report") return 2;
var reportPath = Path.GetFullPath(args[1]);
var rules = VersionChecker.Rules();
var catalog = FastCatalog.ReadRegistry();
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

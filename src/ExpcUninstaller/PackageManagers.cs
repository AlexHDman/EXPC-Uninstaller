using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExpcUninstaller;

internal sealed record CommandResult(int ExitCode, string Output, string Error);
internal static class ManagerCommand
{
    public static async Task<CommandResult> Run(string executable, IEnumerable<string> arguments, CancellationToken token,
        bool readOnly = true, IProgress<string>? progress = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException(L.T("ManagerUnavailable"));
        process.StandardInput.Close(); // Never answer license prompts on the user's behalf.
        async Task<string> Read(StreamReader reader)
        {
            var output = new StringBuilder(); var buffer = new char[4096]; int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
            {
                if (output.Length < 4_000_000) output.Append(buffer, 0, Math.Min(count, 4_000_000 - output.Length));
                if (progress != null) progress.Report(new string(buffer, 0, count));
            }
            return output.ToString();
        }
        var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
        try { await process.WaitForExitAsync(readOnly ? token : CancellationToken.None); }
        catch { if (readOnly) { try { process.Kill(true); } catch (InvalidOperationException) { } } throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }
}

internal sealed class PackageManagers
{
    private readonly List<ManagedPackage> _packages = [];
    internal PackageManagers(IEnumerable<ManagedPackage>? packages = null) { if (packages != null) _packages.AddRange(packages); }
    public IReadOnlyList<ManagedPackage> Packages => _packages;
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> Availability { get; } = new();
    public string Error { get; private set; } = "";
    internal static string WingetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
    internal static string ChocoPath => Path.Combine(Environment.GetEnvironmentVariable("ChocolateyInstall") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey"), "bin", "choco.exe");
    internal static string ScoopRoot => Environment.GetEnvironmentVariable("SCOOP") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
    internal static string ScoopPath => Path.Combine(ScoopRoot, "shims", "scoop.ps1");
    internal static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    public async Task Discover(CancellationToken token, IProgress<string>? progress = null)
    {
        _packages.Clear(); Availability.Clear(); Error = "";
        var operations = new[] { DiscoverWinget(token, progress), DiscoverChocolatey(token), DiscoverScoop(token) };
        await Task.WhenAll(operations);
    }
    private async Task DiscoverWinget(CancellationToken token, IProgress<string>? progress)
    {
        if (!File.Exists(WingetPath)) { Availability["WinGet"] = L.T("ManagerUnavailable"); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var version = await ManagerCommand.Run(WingetPath, ["--version"], timeout.Token);
            Availability["WinGet"] = version.Output.Trim();
            var probe = Path.Combine(AppContext.BaseDirectory, "package-probe", "Expc.PackageProbe.exe");
            if (!File.Exists(probe)) throw new IOException("PackageProbeMissing");
            var response = await ManagerCommand.Run(probe, [], timeout.Token, progress: progress);
            var data = JsonSerializer.Deserialize<ProbeResult>(response.Output) ?? throw new IOException("PackageProbeInvalid");
            if (response.ExitCode != 0 || data.Error.Length > 0) throw new IOException(data.Error);
            // Query only the catalog we matched. Unrelated Store agreements must not block WinGet pins.
            var pins = await ManagerCommand.Run(WingetPath, ["pin", "list", "--source", "winget", "--disable-interactivity"], timeout.Token);
            var pinned = ParsePins(pins, data.Packages.Select(p => p.Id));
            lock (_packages) _packages.AddRange(data.Packages.Select(p => p with { Pinned = pinned == null || pinned.Contains(p.Id) }));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Error = "ManagerTimeout"; Availability["WinGet"] = L.T(Error); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = "ManagerFailed"; Availability["WinGet"] = L.T(Error) + " (" + ex.GetType().Name + ")"; }
    }
    internal static HashSet<string>? ParsePins(CommandResult value, IEnumerable<string> knownIds)
    {
        if (value.ExitCode != 0) return null;
        var text = Regex.Replace(value.Output, @"\x1b\[[0-9;?]*[A-Za-z]", "", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        // English: official v1.29.380 PinNoPinsExist resource. Russian: captured v1.29.380 output.
        if (text is "There are no pins configured." or "Закрепления не настроены.") return new(StringComparer.OrdinalIgnoreCase);
        if (text.Contains('…') || text.Contains("...") || !Regex.IsMatch(text, @"(?m)^-{5,}\s*$")) return null;
        var ids = knownIds.ToHashSet(StringComparer.OrdinalIgnoreCase); var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').SkipWhile(x => !Regex.IsMatch(x.Trim(), @"^-{5,}$")).Skip(1).Where(x => x.Trim().Length > 0))
        {
            var columns = Regex.Split(line.Trim(), @"\s{2,}");
            var matches = columns.Where(ids.Contains).ToArray();
            if (matches.Length != 1) return null; // A localized/truncated/unknown format never means "not pinned".
            result.Add(matches[0]);
        }
        return result;
    }
    private async Task DiscoverChocolatey(CancellationToken token)
    {
        if (!File.Exists(ChocoPath)) { Availability["Chocolatey"] = L.T("ManagerUnavailable"); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var version = await ManagerCommand.Run(ChocoPath, ["--version"], timeout.Token);
            Availability["Chocolatey"] = version.Output.Trim();
            if (!Version.TryParse(version.Output.Trim(), out var parsed) || parsed.Major < 2) throw new IOException("Chocolatey2Required");
            var installed = await ManagerCommand.Run(ChocoPath, ["list", "--limit-output", "--no-color"], timeout.Token);
            var sources = await ManagerCommand.Run(ChocoPath, ["source", "list", "--limit-output", "--no-color"], timeout.Token);
            var addresses = sources.Output.Split('\n').Select(x => x.Trim().Split('|')).Where(x => x.Length >= 3 && x[2].Equals("false", StringComparison.OrdinalIgnoreCase))
                .Select(x => x[1]).Where(ValidManagerSource).Distinct().ToArray();
            if (sources.ExitCode != 0 || addresses.Length != 1) throw new IOException("ChocolateySourceAmbiguous");
            var outdated = await ManagerCommand.Run(ChocoPath, ["outdated", "--source", addresses[0], "--limit-output", "--no-color"], timeout.Token);
            if (installed.ExitCode != 0 || outdated.ExitCode is not (0 or 2)) throw new IOException("ChocolateyQueryFailed");
            lock (_packages) _packages.AddRange(ParseChocolatey(installed.Output, outdated.Output).Select(p => p with { Source = addresses[0] }));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Availability["Chocolatey"] = L.T("ManagerTimeout"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Availability["Chocolatey"] = L.T("ManagerFailed"); }
    }
    internal static List<ManagedPackage> ParseChocolatey(string installed, string outdated)
    {
        var updates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in outdated.Split('\n'))
        {
            var fields = line.Trim().Split('|');
            if (fields.Length == 4 && SafeId(fields[0]) && bool.TryParse(fields[3], out _)) updates.Add(fields[0], fields);
        }
        var result = new List<ManagedPackage>();
        foreach (var line in installed.Split('\n'))
        {
            var fields = line.Trim().Split('|'); if (fields.Length != 2 || !SafeId(fields[0]) || ReleaseVersion.Parse(fields[1]) == null) continue;
            updates.TryGetValue(fields[0], out var update);
            result.Add(new ManagedPackage
            {
                Manager = "Chocolatey",
                Id = fields[0],
                Name = fields[0],
                Installed = fields[1],
                Latest = update?[2] ?? fields[1],
                Pinned = update?[3].Equals("true", StringComparison.OrdinalIgnoreCase) == true,
                Source = "Chocolatey",
                Applicable = true,
                Scope = "System"
            });
        }
        return result;
    }
    private async Task DiscoverScoop(CancellationToken token)
    {
        if (!File.Exists(ScoopPath)) { Availability["Scoop"] = L.T("ManagerUnavailable"); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            // Official export emits structured installed package identities. No execution-policy bypass.
            var version = await ManagerCommand.Run(PowerShell, ["-NoProfile", "-NonInteractive", "-File", ScoopPath, "--version"], timeout.Token);
            Availability["Scoop"] = version.ExitCode == 0 ? version.Output.Trim() : L.T("ManagerFailed");
            var exported = await ManagerCommand.Run(PowerShell, ["-NoProfile", "-NonInteractive", "-File", ScoopPath, "export"], timeout.Token);
            if (exported.ExitCode != 0) throw new IOException("ScoopExportFailed");
            using var document = JsonDocument.Parse(exported.Output);
            foreach (var app in document.RootElement.GetProperty("apps").EnumerateArray())
            {
                var id = app.GetProperty("Name").GetString() ?? ""; var versionText = app.GetProperty("Version").GetString() ?? "";
                var bucket = app.GetProperty("Source").GetString() ?? "";
                if (!SafeId(id) || !SafeId(bucket)) continue;
                var global = app.TryGetProperty("Info", out var info) && info.ToString().Contains("Global", StringComparison.OrdinalIgnoreCase);
                var root = global ? Environment.GetEnvironmentVariable("SCOOP_GLOBAL") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "scoop") : ScoopRoot;
                var current = Path.Combine(root, "apps", id, "current");
                var metadataPath = new[] { "scoop-install.json", "install.json" }.Select(n => Path.Combine(current, n)).FirstOrDefault(File.Exists);
                if (metadataPath == null) continue;
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                var pinned = metadata.RootElement.TryGetProperty("hold", out var held) && held.ValueKind == JsonValueKind.True;
                // Read the configured bucket's upstream manifest over HTTPS; do not update Scoop/buckets during a check.
                var git = Path.Combine(ScoopRoot, "buckets", bucket, ".git", "config");
                if (!File.Exists(git)) continue;
                var remote = Regex.Match(File.ReadAllText(git), @"(?m)^\s*url\s*=\s*https://github\.com/([\w.-]+)/([\w.-]+?)(?:\.git)?\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
                if (!remote.Success) continue;
                var address = $"https://raw.githubusercontent.com/{remote.Groups[1].Value}/{remote.Groups[2].Value}/HEAD/bucket/{id}.json";
                using var manifest = JsonDocument.Parse(await VersionChecker.Fetch(address, timeout.Token));
                var latest = manifest.RootElement.GetProperty("version").GetString() ?? "";
                var localManifest = Path.Combine(ScoopRoot, "buckets", bucket, "bucket", id + ".json");
                using var local = File.Exists(localManifest) ? JsonDocument.Parse(File.ReadAllText(localManifest)) : null;
                var synchronized = local != null && JsonElement.DeepEquals(local.RootElement, manifest.RootElement);
                lock (_packages) _packages.Add(new ManagedPackage
                {
                    Manager = "Scoop",
                    Id = id,
                    Name = id,
                    Installed = versionText,
                    Latest = latest,
                    Source = bucket,
                    Location = current,
                    Scope = global ? "System" : "User",
                    Pinned = pinned,
                    Applicable = !global && synchronized,
                    Error = synchronized ? "" : "ScoopReview"
                });
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Availability["Scoop"] = L.T("ManagerTimeout"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Availability["Scoop"] = L.T("ManagerFailed"); }
    }
    internal static bool SafeId(string id) => Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,150}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static string CompatibilitySystem => Environment.OSVersion.Version + "/" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
    internal static bool ValidManagerSource(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        || Regex.IsMatch(source, @"^[A-Za-z]:\\", RegexOptions.CultureInvariant) && Path.IsPathFullyQualified(source) && Directory.Exists(source);
    internal static string[] ChocolateyUpgradeArguments(ManagedPackage package, bool explicitConsent)
    {
        if (!explicitConsent || !package.IdentityConfirmed || !package.Applicable || package.Pinned || !SafeId(package.Id)
            || ReleaseVersion.Parse(package.Latest) == null || !ValidManagerSource(package.Source)) throw new InvalidOperationException(L.T("ManagerIdentityReview"));
        return ["upgrade", package.Id, "--version", package.Latest, "--source", package.Source, "--yes", "--no-progress", "--limit-output", "--no-color"];
    }
    internal static bool Matches(AppEntry app, ManagedPackage package)
    {
        if (app.Store || app.Metadata == null || app.Version != package.Installed || app.Publisher.Length == 0
            || !app.Publisher.Trim().Equals(package.Publisher.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        var scope = app.Sources.All(x => x.StartsWith("LocalMachine/")) ? "System" : app.Sources.All(x => x.StartsWith("CurrentUser/")) ? "User" : "";
        if (scope.Length == 0 || scope != package.Scope || !package.ProductCodes.Any(code => SameProductCode(code, app.Metadata.KeyName))) return false;
        if (package.Architecture == "X64" && app.Sources.All(s => s.Contains("/Registry32/"))) return false;
        return true;
    }
    internal static bool SameProductCode(string left, string right) => Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b)
        ? a == b : left.Equals(right, StringComparison.OrdinalIgnoreCase);
    internal static bool Excluded(AppEntry app, ManagedPackage p) => app.Protected || Regex.IsMatch(app.Name + " " + p.Id,
        @"\b(firmware|bios|driver|drivers|драйвер|драйверы)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public VersionResult Evaluate(AppEntry app, VersionRule? rule, bool explicitRule)
    {
        var now = DateTimeOffset.UtcNow; var stamp = rule?.Stamp ?? "";
        VersionResult State(VersionState state, string reason, ManagedPackage? p = null) => new(state, app.Version, p?.Latest ?? "", p == null ? "" : p.Manager + " / " + p.Source + " / " + p.Id,
            p == null ? null : now, now, reason, p?.Applicable == true ? p.Manager == "WinGet" ? "ManagerCompatible" : "UserConfirmedCompatibility" : "CompatibilityUnknown", stamp)
        { Package = p };
        var candidates = _packages.Where(p => Matches(app, p)).ToArray();
        if (explicitRule && rule is { Provider: "winget" }) candidates = candidates.Where(p => p.Id == rule.WingetId && p.Manager == "WinGet").ToArray();
        if (explicitRule && rule is { ConfirmManagedIdentity: true, Provider: "chocolatey" or "scoop" } && !app.Store && app.Metadata != null)
        {
            var manager = rule.Provider == "scoop" ? "Scoop" : "Chocolatey";
            var scope = app.Sources.All(x => x.StartsWith("LocalMachine/")) ? "System" : app.Sources.All(x => x.StartsWith("CurrentUser/")) ? "User" : "";
            candidates = _packages.Where(p => p.Id == rule.WingetId && p.Manager == manager && p.Installed == app.Version && p.Scope == scope)
                .Select(p => p with
                {
                    IdentityConfirmed = true,
                    Publisher = app.Publisher,
                    ProductCodes = [app.Metadata.KeyName],
                    Applicable = p.Applicable && rule.ConfirmedCompatibilityVersion == p.Latest && rule.ConfirmedCompatibilitySystem == CompatibilitySystem
                }).ToArray();
        }
        if (candidates.Length > 1) return State(VersionState.Ambiguous, "ManagerAmbiguous");
        if (candidates.Length == 0)
        {
            if (explicitRule && rule?.Provider is "winget" or "chocolatey" or "scoop")
            {
                var manager = rule.Provider == "winget" ? "WinGet" : rule.Provider == "scoop" ? "Scoop" : "Chocolatey";
                if (Availability.TryGetValue(manager, out var status))
                {
                    if (status == L.T("ManagerUnavailable")) return State(VersionState.Unknown, "ManagerUnavailable");
                    if (status.StartsWith(L.T("ManagerFailed"))) return State(VersionState.Error, "ManagerFailed");
                    if (status == L.T("ManagerTimeout")) return State(VersionState.Error, "ManagerTimeout");
                }
            }
            return State(Error.Length > 0 ? VersionState.Error : VersionState.Unknown, Error.Length > 0 ? Error : "NoRule");
        }
        var package = candidates[0];
        if (package.Error.Length > 0) return State(VersionState.Error, "ManagerFailed", package);
        if (!package.IdentityConfirmed || Excluded(app, package)) return State(VersionState.Manual, "ManagerIdentityReview", package);
        if (package.Channel.Length > 0 && !package.Channel.Equals("stable", StringComparison.OrdinalIgnoreCase)) return State(VersionState.Manual, "BranchMismatch", package);
        if (rule?.Branch.Length > 0 && (!app.Version.StartsWith(rule.Branch + ".") || !package.Latest.StartsWith(rule.Branch + "."))) return State(VersionState.Manual, "BranchMismatch", package);
        if (ReleaseVersion.DifferentChannel(app.Version, package.Latest)) return State(VersionState.Manual, "ChannelMismatch", package);
        var comparison = ReleaseVersion.Compare(app.Version, package.Latest);
        if (comparison == null || comparison > 0) return State(VersionState.Unknown, comparison == null ? "AmbiguousVersion" : "InstalledNewer", package);
        if (comparison == 0) return State(VersionState.Current, "ComparedBranch", package);
        if (!package.Applicable || package.Pinned) return State(VersionState.Manual, package.Pinned ? "ManagerPinned" : "CompatibilityUnknown", package);
        return State(VersionState.Update, "ManagerReady", package);
    }
    public static string[] WingetUpgradeArguments(ManagedPackage package)
    {
        if (!SafeId(package.Id) || ReleaseVersion.Parse(package.Latest) == null || package.Scope is not ("User" or "System")
            || package.Architecture is not ("X64" or "X86" or "Arm64" or "Neutral") || package.Pinned || !package.IdentityConfirmed || !package.Applicable)
            throw new InvalidOperationException(L.T("ManagerIdentityReview"));
        var args = new List<string> { "upgrade", "--id", package.Id, "--exact", "--version", package.Latest, "--source", "winget", "--scope", package.Scope == "User" ? "user" : "machine", "--disable-interactivity" };
        if (package.Architecture != "Neutral") args.AddRange(["--architecture", package.Architecture.ToLowerInvariant()]);
        return args.ToArray();
    }
}

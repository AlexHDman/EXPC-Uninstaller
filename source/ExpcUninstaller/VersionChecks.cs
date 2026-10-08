using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExpcUninstaller;

internal enum VersionState { Unchecked, Unknown, Current, Update, EndOfSupport, Manual, Error, Cancelled, Authentication, Ambiguous }
internal sealed record VersionResult(VersionState State, string Installed, string Latest, string Source,
    DateTimeOffset? CheckedAt, DateTimeOffset AttemptedAt, string Reason, string Compatibility, string RuleStamp)
{
    public ManagedPackage? Package { get; init; }
    public bool CanInstall => State == VersionState.Update && ReleaseVersion.Compare(Installed, Latest) < 0
        && Package is { Applicable: true, IdentityConfirmed: true, Pinned: false, Error.Length: 0 }
        && Package.Installed == Installed && Package.Latest == Latest && Package.Manager is "WinGet" or "Chocolatey" or "Scoop";
    public string SelectionReason => CanInstall ? "ReadyForUpdate"
        : Package is null ? Reason == "ManagerReady" ? "ManagerIdentityReview" : Reason
        : Package.Pinned ? "ManagerPinned"
        : !Package.IdentityConfirmed ? "ManagerIdentityReview"
        : !Package.Applicable ? "CompatibilityUnknown"
        : Package.Error.Length > 0 ? "ManagerFailed"
        : Package.Installed != Installed || Package.Latest != Latest ? "UpdatePlanChanged"
        : Reason == "ManagerReady" ? "ManagerIdentityReview" : Reason;
}

internal sealed record VersionRule
{
    public string Id { get; init; } = "";
    public string NamePattern { get; init; } = "";
    public string PublisherPattern { get; init; } = "";
    public string KeyPattern { get; init; } = "";
    public string Architecture { get; init; } = "any";
    public string Provider { get; init; } = "page";
    public string Url { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string Channel { get; init; } = "stable";
    public string InstalledPattern { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Compatibility { get; init; } = "CompatibilityUnknown";
    public string ManualReason { get; init; } = "";
    public string EndOfSupportPattern { get; init; } = "";
    public string WingetId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public bool ConfirmManagedIdentity { get; init; }
    public string ConfirmedCompatibilityVersion { get; init; } = "";
    public string ConfirmedCompatibilitySystem { get; init; } = "";
    public bool Matches(AppEntry app) => !app.Store && NamePattern.Length > 0 && PublisherPattern.Length > 0
        && Match(app.Name, NamePattern) && Match(app.Publisher, PublisherPattern)
        && (KeyPattern.Length == 0 || Match(app.Metadata?.KeyName ?? app.Id, KeyPattern))
        && (Architecture == "any" || app.Sources.All(s => s.Contains(Architecture == "x64" ? "Registry64" : "Registry32")));
    public static bool Match(string input, string pattern) => Regex.IsMatch(input, pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    [System.Text.Json.Serialization.JsonIgnore]
    public string Stamp => JsonSerializer.Serialize(this);
    [System.Text.Json.Serialization.JsonIgnore]
    public string EvidenceSource => Provider == "winget" ? "WinGet / winget / " + WingetId : Url;
}

internal static class ReleaseVersion
{
    internal static BigInteger[]? Parse(string input)
    {
        if (input.Length > 100 || !Regex.IsMatch(input, @"^v?\d+(?:\.\d+){1,5}$", RegexOptions.IgnoreCase)) return null;
        return input.TrimStart('v', 'V').Split('.').Select(BigInteger.Parse).ToArray();
    }
    public static int? Compare(string installed, string available)
    {
        var a = Parse(installed); var b = Parse(available);
        if (a == null || b == null) return null;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var n = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (n != 0) return n;
        }
        return 0;
    }
}

internal sealed class VersionChecker(Func<string, CancellationToken, Task<string>>? fetch = null)
{
    public static List<VersionRule> Rules()
    {
        using var stream = typeof(VersionChecker).Assembly.GetManifestResourceStream("Expc.VersionRules.json")!;
        return JsonSerializer.Deserialize<List<VersionRule>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static VersionRule? Resolve(AppEntry app, IEnumerable<VersionRule> rules)
    {
        var matches = rules.Where(r => r.Matches(app)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
    public static Uri PublicUrl(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.')
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(uri.Query, @"(?:[?&])(?:password|passwd|token|api[_-]?key|access[_-]?token|ticket)=", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) throw new FormatException(L.T("PublicHttps"));
        return uri;
    }
    private static bool Private(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
        if (ip.IsIPv4MappedToIPv6) return Private(ip.MapToIPv4());
        var b = ip.GetAddressBytes();
        return b.Length == 16 ? (b[0] & 0xfe) == 0xfc || ip.Equals(IPAddress.IPv6None) :
            b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31
            || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }
    public static async Task<string> Fetch(string address, CancellationToken token)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EXPC-Uninstaller/0.4 (+https://github.com/AlexHDman/EXPC-Uninstaller)");
        var uri = PublicUrl(address);
        for (var redirect = 0; redirect < 5; redirect++)
        {
            var ips = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, token);
            if (ips.Length == 0 || ips.Any(Private)) throw new IOException(L.T("PublicHttps"));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
            { uri = PublicUrl(new Uri(uri, response.Headers.Location).AbsoluteUri); continue; }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            var text = new System.Text.StringBuilder(); var buffer = new char[8192]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            { text.Append(buffer, 0, count); if (text.Length > 2_000_000) throw new IOException(L.T("PageTooLarge")); }
            return text.ToString();
        }
        throw new IOException(L.T("RedirectLimit"));
    }
    public static string Extract(string text, string pattern)
    {
        if (pattern.Length is 0 or > 1500) throw new FormatException(L.T("ExtractionRequired"));
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(200)).Select(m => m.Groups["version"].Value).Distinct().Take(3).ToArray();
        if (matches.Length != 1 || ReleaseVersion.Parse(matches[0]) == null) throw new FormatException(L.T("AmbiguousVersion"));
        return matches[0].TrimStart('v', 'V');
    }
    public static string PlainText(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        .Replace('\u00a0', ' ');

    public static async Task<string> Winget(string id, CancellationToken token)
    {
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9.\-_]{2,150}$")) throw new FormatException(L.T("WingetId"));
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(exe)) throw new IOException(L.T("WingetUnavailable"));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "show", "--id", id, "--exact", "--source", "winget", "--disable-interactivity" }
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { try { process.Kill(true); } catch { } throw; }
        var text = await output; await error;
        if (process.ExitCode != 0) throw new IOException(L.T("WingetUnavailable"));
        return Extract(text, @"(?:^|[\r\n])(?:Version|Версия):\s*(?<version>\d+(?:\.\d+)+)\s*(?:[\r\n]|$)");
    }
    public static VersionResult Evaluate(AppEntry app, VersionRule rule, string version, DateTimeOffset now)
    {
        var installed = rule.InstalledPattern.Length == 0 ? app.Version : Extract(app.Version, rule.InstalledPattern);
        var comparison = ReleaseVersion.Compare(installed, version);
        var sameBranch = rule.Branch.Length == 0 || installed.StartsWith(rule.Branch + ".", StringComparison.Ordinal)
            && version.StartsWith(rule.Branch + ".", StringComparison.Ordinal);
        var state = !sameBranch ? VersionState.Manual : comparison switch { 0 => VersionState.Current, < 0 => VersionState.Update, _ => VersionState.Unknown };
        return new(state, app.Version, version, rule.EvidenceSource, now, now,
            !sameBranch ? "BranchMismatch" : comparison == null ? "AmbiguousVersion" : comparison > 0 ? "InstalledNewer" : "ComparedBranch",
            rule.Compatibility, rule.Stamp);
    }
    public async Task<VersionResult> Check(AppEntry app, VersionRule? rule, CancellationToken cancellation, VersionResult? previous = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (rule == null) return new(VersionState.Unknown, app.Version, "", "", null, now, "NoRule", "CompatibilityUnknown", "");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(22));
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (rule.Provider is not ("page" or "github" or "winget" or "manual")) throw new FormatException(L.T("ExtractionRequired"));
            string page;
            if (rule.Provider == "winget") page = await Winget(rule.WingetId, timeout.Token);
            else
            {
                var url = rule.Url;
                if (rule.Provider == "github")
                {
                    var uri = PublicUrl(url); var parts = uri.AbsolutePath.Trim('/').Split('/');
                    if (uri.Host != "github.com" || parts.Length != 2 || parts.Any(p => !Regex.IsMatch(p, @"^[A-Za-z0-9_.-]+$"))) throw new FormatException(L.T("GithubRepo"));
                    url = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";
                }
                page = await (fetch ?? Fetch)(url, timeout.Token);
            }
            if (rule.Provider == "manual") return new(VersionState.Manual, app.Version, "", rule.Url, now, now, rule.ManualReason, rule.Compatibility, rule.Stamp);
            if (rule.EndOfSupportPattern.Length > 0 && VersionRule.Match(PlainText(page), rule.EndOfSupportPattern))
                return new(VersionState.EndOfSupport, app.Version, "", rule.Url, now, now, "ExplicitEndOfSupport", rule.Compatibility, rule.Stamp);
            string version;
            if (rule.Provider == "github")
            {
                using var document = JsonDocument.Parse(page); var release = document.RootElement;
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) throw new FormatException(L.T("ChannelMismatch"));
                version = Extract(release.GetProperty("tag_name").GetString() ?? "", rule.Pattern);
            }
            else version = rule.Provider == "winget" ? page : Extract(PlainText(page), rule.Pattern);
            return Evaluate(app, rule, version, now);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or JsonException or OperationCanceledException or RegexMatchTimeoutException or System.Net.Sockets.SocketException or InvalidOperationException or System.ComponentModel.Win32Exception or KeyNotFoundException or ArgumentException)
        {
            var cancelled = cancellation.IsCancellationRequested;
            var keep = previous?.Installed == app.Version && previous.RuleStamp == rule.Stamp ? previous : null;
            return new(cancelled ? VersionState.Cancelled : ex is FormatException ? VersionState.Manual : VersionState.Error,
                app.Version, keep?.Latest ?? "", rule.EvidenceSource, keep?.CheckedAt, now,
                cancelled ? "Cancelled" : ex is OperationCanceledException ? "Timeout" : ex.Message, rule.Compatibility, rule.Stamp);
        }
    }
}

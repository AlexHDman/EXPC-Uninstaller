using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ExpcUninstaller;

internal static class ProtectedSources
{
    internal const string OneCLogin = "https://login.1c.ru/login?service=https://releases.1c.ru/public/security_check";
    internal static bool AllowedOneC(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "login.1c.ru" or "releases.1c.ru";
    internal static Dictionary<string, string> LoginFields(string html)
    {
        if (!Regex.IsMatch(html, "<form[^>]*id=[\"']loginForm[\"']", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException(L.T("AuthenticationInteractive"));
        if (Regex.IsMatch(html, "<input[^>]+(?:name|id)=[\"'][^\"']*(?:captcha|otp|totp)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            throw new InvalidOperationException(L.T("AuthenticationInteractive"));
        var result = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150)))
        {
            var attributes = Regex.Matches(input.Value, "([a-zA-Z]+)\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.None, TimeSpan.FromMilliseconds(100))
                .ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => WebUtility.HtmlDecode(m.Groups[2].Value));
            if (attributes.GetValueOrDefault("type") == "hidden" && attributes.TryGetValue("name", out var name)
                && name is "execution" or "_eventId" or "geolocation" or "inviteCode" or "inviteType") result[name] = attributes.GetValueOrDefault("value", "");
        }
        if (!result.ContainsKey("execution") || result.GetValueOrDefault("_eventId") != "submit") throw new InvalidOperationException(L.T("AuthenticationInteractive"));
        return result;
    }
    public static async Task<VersionResult> Check(AppEntry app, VersionRule rule, SecretVault vault, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        VersionResult State(VersionState state, string reason) => new(state, app.Version, "", rule.Url, null, now, reason, rule.Compatibility, rule.Stamp);
        if (!vault.Unlocked) return State(VersionState.Authentication, "VaultLocked");
        var account = vault.Accounts.SingleOrDefault(x => x.Id == rule.AccountId);
        if (account == null || account.Host != "login.1c.ru") return State(VersionState.Authentication, "AccountRequired");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, vault.ProtectedRequests);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            var target = VersionChecker.PublicUrl(rule.Url);
            if (target.Host != "releases.1c.ru") return State(VersionState.Error, "OneCOfficialOnly");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            async Task<(Uri Uri, string Text)> Read(Uri address, HttpContent? form = null)
            {
                for (var redirect = 0; redirect < 8; redirect++)
                {
                    if (!AllowedOneC(address)) throw new InvalidOperationException(L.T("OneCOfficialOnly"));
                    // Passwords are posted exactly once to login.1c.ru/login, never replayed on redirects.
                    if (form != null && (address.Host != "login.1c.ru" || address.AbsolutePath != "/login")) throw new InvalidOperationException();
                    using var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, address) { Content = form };
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    form = null;
                    if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
                    { address = new Uri(address, response.Headers.Location); continue; }
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var reader = new StreamReader(stream); var output = new System.Text.StringBuilder(); var buffer = new char[8192]; int count;
                    while ((count = await reader.ReadAsync(buffer, timeout.Token)) > 0)
                    { output.Append(buffer, 0, count); if (output.Length > 2_000_000) throw new IOException(); }
                    return (address, output.ToString());
                }
                throw new IOException();
            }
            var login = await Read(new Uri(OneCLogin));
            var fields = LoginFields(login.Text);
            fields["username"] = account.Username; fields["password"] = account.Password;
            fields["anotherComputer"] = "on"; // No persistent remember-me session.
            (Uri Uri, string Text) authorized;
            try { authorized = await Read(new Uri(OneCLogin), new FormUrlEncodedContent(fields)); }
            finally { fields.Clear(); }
            if (authorized.Uri.Host != "releases.1c.ru") return State(VersionState.Authentication, "AuthenticationInteractive");
            var page = await Read(target);
            if (page.Uri.Host != "releases.1c.ru" || page.Text.Contains("id=\"loginForm\"", StringComparison.OrdinalIgnoreCase))
                return State(VersionState.Authentication, "AuthenticationInteractive");
            var version = VersionChecker.Extract(VersionChecker.PlainText(page.Text), rule.Pattern);
            var result = VersionChecker.Evaluate(app, rule, version, now);
            return result with { Reason = "OneCManualDownload" }; // Official downloads open in the user's browser; no database/configuration updates.
        }
        catch (OperationCanceledException) { return State(VersionState.Cancelled, "CheckCancelledReason"); }
        catch (InvalidOperationException) { return State(VersionState.Authentication, "AuthenticationInteractive"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or RegexMatchTimeoutException)
        { return State(VersionState.Error, "NetworkOrRuleError"); }
    }
}

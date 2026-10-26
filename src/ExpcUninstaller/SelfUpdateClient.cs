using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ExpcUninstaller;

internal sealed record SelfRelease(SelfUpdateManifest Manifest, string Notes);
internal sealed class SelfUpdateClient : IDisposable
{
    private readonly HttpClient client;
    internal SelfUpdateClient(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EXPCdelete/" + Current);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    internal static Version Current => new(typeof(SelfUpdateClient).Assembly.GetName().Version!.ToString(3));
    private async Task<byte[]> Fetch(Uri url, int limit, CancellationToken token)
    {
        // ResponseHeadersRead does not apply HttpClient.Timeout to the response body.
        // Bound the complete redirect/body chain too, including a stalled download.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        token = deadline.Token;
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) || url.Port != 443 ||
                url.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Untrusted update URL");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            { url = new Uri(url, response.Headers.Location ?? throw new IOException("Missing redirect")); continue; }
            response.EnsureSuccessStatusCode(); // Includes offline/rate limit; never translates to up-to-date.
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update response too large");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            { if (output.Length + count > limit) throw new InvalidDataException("Update response too large"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
            return output.ToArray();
        }
        throw new IOException("Too many redirects");
    }
    internal async Task<SelfRelease?> Check(CancellationToken token)
    {
        var bytes = await Fetch(new Uri($"https://api.github.com/repos/{SelfUpdatePolicy.Repository}/releases?per_page=100"), 2_000_000, token);
        using var list = JsonDocument.Parse(bytes);
        var releases = new List<(Version Version, JsonElement Release)>();
        foreach (var release in list.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            try { if (tag.StartsWith('v')) releases.Add((SelfUpdatePolicy.ParseVersion(tag[1..]), release)); } catch (InvalidDataException) { }
        }
        foreach (var entry in releases.OrderByDescending(x => x.Version))
        {
            var version = entry.Version.ToString(3);
            var assets = entry.Release.GetProperty("assets").EnumerateArray().ToArray();
            var manifestAsset = assets.SingleOrDefault(x => x.GetProperty("name").GetString() == "EXPCdelete.update.json");
            var exeAsset = assets.SingleOrDefault(x => x.GetProperty("name").GetString() == "EXPCdelete.exe");
            if (manifestAsset.ValueKind == JsonValueKind.Undefined || exeAsset.ValueKind == JsonValueKind.Undefined) continue;
            SelfUpdatePolicy.RequireAssetUrl(manifestAsset.GetProperty("browser_download_url").GetString()!, version, "EXPCdelete.update.json");
            SelfUpdatePolicy.RequireAssetUrl(exeAsset.GetProperty("browser_download_url").GetString()!, version, "EXPCdelete.exe");
            var manifest = JsonSerializer.Deserialize<SelfUpdateManifest>(await Fetch(SelfUpdatePolicy.AssetUri(version, "EXPCdelete.update.json"), 16384, token), SelfUpdatePolicy.Json)
                ?? throw new InvalidDataException("Missing manifest");
            SelfUpdatePolicy.Validate(manifest);
            if (manifest.Version != version || manifest.Bytes != exeAsset.GetProperty("size").GetInt64()) throw new InvalidDataException("Release metadata mismatch");
            return new SelfRelease(manifest, entry.Release.GetProperty("body").GetString() ?? "");
        }
        return null;
    }
    internal async Task<string> Download(SelfUpdateManifest manifest, CancellationToken token)
    {
        SelfUpdatePolicy.Validate(manifest);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EXPC", "SelfUpdate", Guid.NewGuid().ToString("N"));
        SelfUpdatePolicy.NoLinks(root); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "EXPCdelete.exe");
        var bytes = await Fetch(SelfUpdatePolicy.AssetUri(manifest.Version, manifest.Asset), (int)manifest.Bytes, token);
        await File.WriteAllBytesAsync(path, bytes, token);
        SelfUpdatePolicy.Verify(path, manifest);
        return path;
    }
    public void Dispose() => client.Dispose();
}

internal static class UpdateOrigin
{
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public IntPtr Reserved, Peb, Reserved2, Reserved3, Pid, Parent; }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int info, ref BasicInfo value, int length, out int returned);
    internal static (string Path, Process Launcher) Verify()
    {
        using var current = Process.GetCurrentProcess(); var basic = new BasicInfo();
        if (NtQueryInformationProcess(current.Handle, 0, ref basic, Marshal.SizeOf<BasicInfo>(), out _) != 0) throw new IOException("Cannot verify launcher identity");
        var origin = Environment.GetEnvironmentVariable("EXPC_PORTABLE_ORIGIN");
        if (string.IsNullOrWhiteSpace(origin) || !int.TryParse(Environment.GetEnvironmentVariable("EXPC_LAUNCHER_PID"), out var pid) || basic.Parent.ToInt64() != pid)
            throw new IOException("Self-update requires the original portable launcher");
        var launcher = Process.GetProcessById(pid);
        try
        {
            var path = Path.GetFullPath(origin); SelfUpdatePolicy.NoLinks(path);
            if (!string.Equals(launcher.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Origin differs from running launcher");
            return (path, launcher);
        }
        catch { launcher.Dispose(); throw; }
    }
}

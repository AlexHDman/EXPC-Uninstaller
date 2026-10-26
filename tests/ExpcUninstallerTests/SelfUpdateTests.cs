using System.Net;
using System.Text;
using System.Text.Json;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace ExpcUninstallerTests;
[TestClass]
public sealed class SelfUpdateTests
{
    private static SelfUpdateManifest Manifest(string version = "6.3.1") => new(1, version, "EXPCdelete.exe", 4096, new string('a',64), "win-x64", "10.0.12", "10.0.18362");
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static object Release(string version, bool draft = false, bool prerelease = false, bool assets = true) => new {
        tag_name = "v" + version, draft, prerelease, body = "test", assets = assets ? new[] {
            new { name="EXPCdelete.update.json",size=500,browser_download_url=$"https://github.com/AlexHDman/EXPC-Uninstaller/releases/download/v{version}/EXPCdelete.update.json" },
            new { name="EXPCdelete.exe",size=4096,browser_download_url=$"https://github.com/AlexHDman/EXPC-Uninstaller/releases/download/v{version}/EXPCdelete.exe" }
        } : [] };
    [TestMethod]
    [DataRow("6.3.0", -1)] [DataRow("6.3.1", 0)] [DataRow("6.3.2", 1)]
    public void VersionOrdering(string version, int expected) => Assert.AreEqual(expected, Math.Sign(SelfUpdatePolicy.ParseVersion(version).CompareTo(new Version(6,3,1))));
    [TestMethod]
    [DataRow("latest")] [DataRow("6.3.2-preview")] [DataRow("v6.3.2")] [DataRow("6.3")]
    public void RejectInvalidVersion(string value) => Assert.ThrowsExactly<InvalidDataException>(() => SelfUpdatePolicy.ParseVersion(value));
    [TestMethod]
    public async Task StableReleaseAndManifestUseSameIdentity()
    {
        using var client = new SelfUpdateClient(new Handler(r => r.RequestUri!.Host == "api.github.com" ? Json(new[] { Release("6.3.2") }) : Json(Manifest("6.3.2"))));
        Assert.AreEqual("6.3.2", (await client.Check(default))!.Manifest.Version);
    }
    [TestMethod]
    public async Task EmptyDraftPreviewAndMissingAssetsAreUnavailable()
    {
        foreach (var releases in new[] { Array.Empty<object>(), new[] { Release("6.3.2", draft:true) }, new[] { Release("6.3.2", prerelease:true) }, new[] { Release("6.3.2", assets:false) } })
        {
            using var client = new SelfUpdateClient(new Handler(_ => Json(releases)));
            Assert.IsNull(await client.Check(default));
        }
    }
    [TestMethod]
    [DataRow(403)] [DataRow(429)] [DataRow(500)]
    public async Task ApiErrorsNeverMeanLatest(int status)
    {
        using var client = new SelfUpdateClient(new Handler(_ => new HttpResponseMessage((HttpStatusCode)status)));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.Check(default));
    }
    [TestMethod]
    public async Task OfflineAndCancellationPropagate()
    {
        using var offline = new SelfUpdateClient(new Handler(_ => throw new HttpRequestException("offline")));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => offline.Check(default));
        using var timeout = new SelfUpdateClient(new Handler(_ => throw new TaskCanceledException("timeout")));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => timeout.Check(default));
    }
    [TestMethod]
    public async Task RejectRedirectOutsideGithub()
    {
        using var client = new SelfUpdateClient(new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://example.com/evil"); return r; }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.Check(default));
    }
    [TestMethod]
    public void RejectWrongRepositoryArchitectureAndHash()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => SelfUpdatePolicy.RequireAssetUrl("https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/releases/download/v6.3.1/EXPCdelete.exe", "6.3.1", "EXPCdelete.exe"));
        Assert.ThrowsExactly<InvalidDataException>(() => SelfUpdatePolicy.Validate(Manifest() with { Architecture="win-x86" }));
        Assert.ThrowsExactly<InvalidDataException>(() => SelfUpdatePolicy.Validate(Manifest() with { Sha256="wrong" }));
    }
    [TestMethod]
    public void InvalidDownloadNeverReplacesOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "EXPC-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var origin=Path.Combine(root,"старый файл.exe");var next=Path.Combine(root,"new.exe");File.WriteAllText(origin,"original");File.WriteAllText(next,"invalid");
            var request=new ReplacementRequest(origin,next,SelfUpdatePolicy.Hash(origin),Manifest(),0,0,0,0,"fixture");
            Assert.ThrowsExactly<InvalidDataException>(() => Replacement.Swap(request));
            Assert.AreEqual("original",File.ReadAllText(origin));
        } finally { foreach(var file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root); }
    }
}

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

    [TestMethod]
    public void SummaryUsesOnlyMarkedUserLanguageAndNeverTechnicalBody()
    {
        var r = new SelfRelease(Manifest(), "Hash TESTED RU/EN\n<!-- expc-summary:ru -->\n- Коротко\n<!-- /expc-summary -->\n<!-- expc-summary:en -->\n- Brief\n<!-- /expc-summary -->");
        CollectionAssert.AreEqual(new[]{"Коротко"},r.Summary("ru"));
        CollectionAssert.AreEqual(new[]{"Brief"},r.Summary("en"));
        Assert.AreEqual(0,(r with { Notes="full technical release body" }).Summary("ru").Length);
    }
    private sealed class ProgressSink : IProgress<UpdateProgress>
    {
        internal readonly List<UpdateProgress> Values = [];
        public void Report(UpdateProgress value) => Values.Add(value);
    }
    [TestMethod]
    public async Task DownloadReportsBytesVerifiesAndBadHashCanRetryWithoutTouchingData()
    {
        var root = Path.Combine(AppContext.BaseDirectory,"update-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var data = File.ReadAllBytes(typeof(SelfUpdateClient).Assembly.Location);
        var m = Manifest(SelfUpdateClient.Current.ToString()) with { Bytes=data.Length, Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) };
        File.WriteAllText(Path.Combine(root,"user-data.txt"),"preserve");
        using var client = new SelfUpdateClient(new Handler(_ => new(HttpStatusCode.OK) {Content=new ByteArrayContent(data)}));
        var sink = new ProgressSink();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.Download(m with {Sha256=new string('0',64)},default,sink,root));
        Assert.AreEqual(0,Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories).Length);
        var path = await client.Download(m,default,sink,root);
        Assert.AreEqual(m.Sha256,SelfUpdatePolicy.Hash(path));
        Assert.IsTrue(sink.Values.Any(x=>x.Stage=="download" && x.Bytes==m.Bytes));
        Assert.IsTrue(sink.Values.Any(x=>x.Stage=="verify"));
        Assert.AreEqual("preserve",File.ReadAllText(Path.Combine(root,"user-data.txt")));
    }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead=>true; public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
        public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    [TestMethod]
    public async Task StalledBodyHasBoundedIdleTimeoutAndCancellation()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"stall-"+Guid.NewGuid().ToString("N"));
        using var client=new SelfUpdateClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())}),TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsExactlyAsync<TimeoutException>(()=>client.Download(Manifest(),default,null,root));
        using var cancel=new CancellationTokenSource(20);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>client.Download(Manifest(),cancel.Token,null,root));
        Assert.AreEqual(0,Directory.GetFiles(root,"*.exe",SearchOption.AllDirectories).Length);
    }
    [TestMethod]
    public void WaitingForLiveProcessTimesOutWithoutKillingIt()
    {
        using var self=System.Diagnostics.Process.GetCurrentProcess();
        Assert.ThrowsExactly<TimeoutException>(()=>Replacement.Wait(self.Id,self.StartTime.ToUniversalTime().Ticks,20));
        Assert.IsFalse(self.HasExited);
    }
    [TestMethod]
    public void ExplicitUpdateJournalSurvivesOutcomeAndDoesNotEnableMetrics()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"journal-"+Guid.NewGuid().ToString("N"));
        var journal=new UpdateJournal(testLogs:root);journal.Write("wait-old-launcher");journal.Outcome(false,"handshake-failed");
        Assert.IsTrue(File.ReadAllText(journal.Path).Contains("handshake-failed"));
        Assert.IsFalse(File.Exists(Path.Combine(root,"startup-metrics.jsonl")));
    }

    [TestMethod]
    public void ReplacementStagesOnTargetAndProtectsRollbackAndNeighbourData()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"swap-"+Guid.NewGuid().ToString("N"));
        var cache=Path.Combine(root,"cache");var target=Path.Combine(root,"target том");Directory.CreateDirectory(cache);Directory.CreateDirectory(target);
        var bytes=File.ReadAllBytes(typeof(SelfUpdateClient).Assembly.Location);
        var download=Path.Combine(cache,"next.exe");File.WriteAllBytes(download,bytes);
        var origin=Path.Combine(target,"мой EXPC.exe");File.WriteAllBytes(origin,bytes.Concat(new byte[]{1,2,3}).ToArray());
        var data=Path.Combine(target,"profile.dat");File.WriteAllText(data,"preserve");
        var m=Manifest(SelfUpdateClient.Current.ToString()) with {Bytes=bytes.Length,Sha256=SelfUpdatePolicy.Hash(download)};
        var request=new ReplacementRequest(origin,download,SelfUpdatePolicy.Hash(origin),m,0,0,0,0,"fixture");
        var backup=Replacement.Swap(request);
        Assert.AreEqual(target,Path.GetDirectoryName(backup));Assert.AreEqual(m.Sha256,SelfUpdatePolicy.Hash(origin));
        Assert.AreEqual(request.OldHash,SelfUpdatePolicy.Hash(backup));Assert.AreEqual(0,Directory.GetFiles(target,"replace-*.exe").Length);
        Replacement.Rollback(origin,backup,request.OldHash,m.Sha256);
        Assert.AreEqual(request.OldHash,SelfUpdatePolicy.Hash(origin));Assert.AreEqual("preserve",File.ReadAllText(data));
    }
}

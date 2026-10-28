using ExpcUninstaller;
using System.Text.Json;
if (args[0] == "check-public")
{
    using var client = new SelfUpdateClient();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var release = client.Check(timeout.Token).GetAwaiter().GetResult() ?? throw new Exception("Release unavailable");
    var v = SelfUpdatePolicy.ParseVersion(release.Manifest.Version);
    Console.WriteLine(JsonSerializer.Serialize(new { version=release.Manifest.Version, current=SelfUpdateClient.Current.ToString(), equal=v==SelfUpdateClient.Current, olderNeedsUpdate=v>new Version(v.Major,v.Minor,Math.Max(0,v.Build-1)), newerNoDowngrade=v<new Version(v.Major,v.Minor,v.Build+1) }));
    return 0;
}
if (args[0] == "swap")
{
    var request=JsonSerializer.Deserialize<ReplacementRequest>(File.ReadAllText(args[1]),SelfUpdatePolicy.Json)!;
    var backup=Replacement.Swap(request);
    Replacement.Rollback(request.Origin,backup,request.OldHash,request.Manifest.Sha256);
    return 0;
}
using var instance = new InstanceSession(args[0]);
if (!instance.Primary) return 10;
File.WriteAllText(args[1] + ".ready",Environment.ProcessId.ToString());
var end=DateTime.UtcNow.AddSeconds(12);
while(DateTime.UtcNow<end && !File.Exists(args[1]+".stop"))
{
    if(instance.PollActivation())File.AppendAllText(args[1]+".signals","activate\n");
    Thread.Sleep(20);
}
return 0;

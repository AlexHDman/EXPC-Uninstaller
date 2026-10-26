// Background protocol fixture only: no registry, installed product or filesystem mutations.
using System.Diagnostics;

if (args.Length != 2 || args[0] != "--operation-fixture") return 99;
if (args[1] == "parent")
{
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, CreateNoWindow = true,
        ArgumentList = { "--operation-fixture", "child" }
    });
    await Task.Delay(200);
    return 0;
}
await Task.Delay(args[1] is "slow" or "child" ? 1500 : 100);
return args[1] switch { "cancel" => 1602, "error" => 1603, "reboot" => 3010, _ => 0 };

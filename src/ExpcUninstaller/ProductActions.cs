using System.ComponentModel;
using System.Management;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Klocman.Tools;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace ExpcUninstaller;

internal sealed record Registration(RegistryHive Hive, RegistryView View, string Key)
{
    public const string Parent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public string Path => Parent + @"\" + Key;
    public string FullPath => (Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER") + @"\" + Path;
    public static Registration Parse(string source)
    {
        var parts = source.Split('/', 3);
        if (parts.Length != 3 || !Enum.TryParse<RegistryHive>(parts[0], out var hive) ||
            hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) ||
            !Enum.TryParse<RegistryView>(parts[1], out var view) || view is not (RegistryView.Registry32 or RegistryView.Registry64) ||
            string.IsNullOrWhiteSpace(parts[2]) || parts[2].IndexOfAny(['\\', '/', '"', '\r', '\n']) >= 0)
            throw new InvalidOperationException(L.T("Operation34"));
        return new(hive, view, parts[2]);
    }
    public RegistryKey? Open(bool writable = false)
    {
        using var root = RegistryKey.OpenBaseKey(Hive, View);
        return root.OpenSubKey(Path, writable);
    }
}

internal static class ProductIdentity
{
    // Absence is proven only by direct reads; an access error is never absence.
    public static bool IsRegistered(AppEntry app) => app.Sources.Select(Registration.Parse).Any(r =>
    { using var key = r.Open(); return key != null; });

    public static void RequireUnchanged(AppEntry app)
    {
        if (app.Store || app.Metadata == null || app.Sources.Length == 0) throw new InvalidOperationException(L.T("Operation35"));
        foreach (var source in app.Sources)
        {
            using var key = Registration.Parse(source).Open();
            if (key == null) throw new InvalidOperationException(L.T("Operation36"));
            string Value(string name) => Convert.ToString(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) ?? "";
            var m = app.Metadata;
            if (Value("DisplayName") != m.Name || Value("DisplayVersion") != m.Version || Value("Publisher") != m.Publisher ||
                Value("InstallLocation") != m.Location || Value("UninstallString") != m.Uninstall ||
                Value("WindowsInstaller") != m.WindowsInstaller || Value("NoRemove") != m.NoRemove || Value("SystemComponent") != m.SystemComponent)
                throw new InvalidOperationException(L.T("Operation37"));
        }
    }
}

internal static class UninstallCommand
{
    public static ProcessStartCommand Parse(string raw, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var command = Environment.ExpandEnvironmentVariables(raw).Trim();
        if (command.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new FormatException(L.T("Operation38"));
        string file, args;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) throw new FormatException(L.T("Operation39"));
            file = command[1..end]; args = command[(end + 1)..].TrimStart();
        }
        else
        {
            var match = Regex.Match(command, @"^(.*?\.exe)(?=\s|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                file = match.Groups[1].Value; args = command[match.Length..].TrimStart();
                if (!file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) && !file.Equals(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), StringComparison.OrdinalIgnoreCase))
                {
                    var candidates = Regex.Matches(command, @"\.exe(?=\s|$)", RegexOptions.IgnoreCase)
                        .Select(m => command[..(m.Index + m.Length)]).Where(p => CleanupBoundary.LocalPath(p) && exists(p)).ToList();
                    if (candidates.Count != 1) throw new FormatException(L.T("Operation40"));
                    file = candidates[0]; args = command[file.Length..].TrimStart();
                }
            }
            else if (command.StartsWith("msiexec ", StringComparison.OrdinalIgnoreCase)) { file = "msiexec.exe"; args = command[8..].Trim(); }
            else throw new FormatException(L.T("Operation41"));
        }
        if (file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
            file.Equals(System.IO.Path.Combine(Environment.SystemDirectory, "msiexec.exe"), StringComparison.OrdinalIgnoreCase))
        {
            // Accept product-code uninstall/maintenance only, preserve properties and UI arguments.
            var m = Regex.Match(args, @"^[/\-](?:i|x|uninstall)\s*(\{[0-9A-Fa-f-]{36}\})(.*)$", RegexOptions.IgnoreCase);
            if (!m.Success || !Guid.TryParse(m.Groups[1].Value, out _)) throw new FormatException(L.T("Operation42"));
            args = Regex.Replace(m.Groups[2].Value, @"(?i)(?:^|\s)(?:/forcerestart|/promptrestart|/norestart|REBOOT\s*=\s*\S+)(?=\s|$)", " ");
            return new ProcessStartCommand(System.IO.Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
                "/x " + m.Groups[1].Value + args + " /norestart REBOOT=ReallySuppress");
        }
        if (!CleanupBoundary.LocalPath(file) || !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !exists(file))
            throw new FormatException(L.T("Operation43"));
        // Never resolve an unquoted path through CreateProcess's ambiguous C:\Program.exe fallback.
        var blocked = new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "reg.exe", "shutdown.exe" };
        if (blocked.Contains(System.IO.Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            throw new FormatException(L.T("Operation44"));
        return new ProcessStartCommand(file, args); // BCU command model; args stay verbatim, no shell interpolation.
    }
}

internal sealed record OperationResult(string State, string Message, bool Removed = false, bool Reboot = false);
internal static class OperationLog
{
    public static string DirectoryPath => System.IO.Path.Combine(PortablePaths.Root, "local-data");
    public static void Write(string operation, string target, string result)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.AppendAllText(System.IO.Path.Combine(DirectoryPath, "operations.jsonl"),
            JsonSerializer.Serialize(new { utc = DateTime.UtcNow, operation, target, result }) + Environment.NewLine);
    }
}

internal static class ProductActions
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, DateTime>> Observed = new();
    internal static void Track(AppEntry app, Process process) => Observed.GetOrAdd(app.Id, _ => new())[process.Id] = process.StartTime;
    internal static bool HasKnownRunning(AppEntry app)
    {
        if (!Observed.TryGetValue(app.Id, out var known)) return false;
        foreach (var pair in known)
            try { using var process = Process.GetProcessById(pair.Key); if (process.StartTime == pair.Value && !process.HasExited) return true; }
            catch (ArgumentException) { } catch { return true; }
        return false;
    }
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string product);
    internal static bool MsiFallback(string file, string? code, bool absent, int exit, int productState) =>
        Path.GetFileName(file).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(code, out _)
        && absent && exit is 0 or 3010 or 1641 && productState == -1;
    public static async Task<OperationResult> Uninstall(AppEntry app, CancellationToken cancellation)
    {
        if (app.Protected) throw new InvalidOperationException(L.T("Operation45"));
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        observation.CancelAfter(TimeSpan.FromMinutes(30)); cancellation = observation.Token;
        OperationLog.Write("uninstall-start", app.Id, app.Name);
        OperationResult result;
        try
        {
            if (app.Store) result = await RemoveStore(app, cancellation);
            else
            {
                ProductIdentity.RequireUnchanged(app);
                var command = UninstallCommand.Parse(app.Metadata!.Uninstall);
                if (app.Metadata.WindowsInstaller == "1" && Guid.TryParse(app.Metadata.KeyName, out var registeredCode) &&
                    !command.Arguments.StartsWith("/x " + registeredCode.ToString("B"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("Operation46"));
                using var trace = new ProcessStartEvents();
                var info = command.ToProcessStartInfo();
                info.WorkingDirectory = System.IO.Path.GetDirectoryName(command.FileName)!;
                Process? process;
                cancellation.ThrowIfCancellationRequested();
                try { process = Process.Start(info); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
                { info.Verb = "runas"; process = Process.Start(info); }
                if (process == null) throw new IOException(L.T("Operation47"));
                using (process)
                {
                    Track(app, process);
                    try { await ProcessFamily.Wait(process, trace, cancellation, child => Track(app, child)); }
                    catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
                    {
                        OperationLog.Write("uninstall-observation-error", app.Id, ex.ToString());
                        return new("unconfirmed", L.T("Operation53"));
                    }
                    OperationLog.Write("uninstall-observation", app.Id, JsonSerializer.Serialize(new { trace.Available, trace.FailureReason }));
                    var exit = process.ExitCode;
                    if (exit != 0 && exit != 3010 && exit != 1641) result = Completion(exit, false, trace.Available);
                    else
                    {
                        // A launcher can exit before a broker/detached child; poll registration as a separate fact.
                        var absent = 0;
                        for (var i = 0; i < 30; i++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            absent = ProductIdentity.IsRegistered(app) ? 0 : absent + 1;
                            if (absent >= 3) break;
                            await Task.Delay(500, cancellation);
                        }
                        var fallback = !trace.Available && app.Metadata.WindowsInstaller == "1" && Guid.TryParse(app.Metadata.KeyName, out _)
                            && MsiFallback(command.FileName, app.Metadata.KeyName, absent >= 3, exit, MsiQueryProductState(app.Metadata.KeyName));
                        OperationLog.Write("uninstall-completion-evidence", app.Id, JsonSerializer.Serialize(new { exit, absent, trace.Available, msiFallback = fallback }));
                        result = Completion(exit, absent >= 3, trace.Available || fallback);
                    }
                }
            }
        }
        catch (OperationCanceledException) { result = new("observation-stopped", L.T("Operation56")); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { result = new("cancelled", L.T("Operation57")); }
        OperationLog.Write("uninstall-result", app.Id, JsonSerializer.Serialize(result));
        return result;
    }

    internal static OperationResult Completion(int exit, bool registrationAbsent, bool traceAvailable)
    {
        if (exit == 1602) return new("cancelled", L.T("Operation48"));
        if (exit != 0 && exit != 3010 && exit != 1641) return new("error", L.T("Operation49") + exit + L.T("Operation50"));
        var reboot = exit is 3010 or 1641;
        return registrationAbsent && traceAvailable
            ? new("removed", L.T("Operation51") + (reboot ? L.T("Operation52") : ""), true, reboot)
            : new("unconfirmed", !traceAvailable ? L.T("Operation53") : L.T("Operation54") + (reboot ? L.T("Operation55") : ""), false, reboot);
    }

    private static async Task<OperationResult> RemoveStore(AppEntry app, CancellationToken token)
    {
        var manager = new PackageManager();
        var packages = manager.FindPackagesForUser("").ToList();
        var package = packages.SingleOrDefault(x => x.Id.FullName == app.Id) ?? throw new IOException(L.T("Operation58"));
        if (package.IsFramework || package.IsResourcePackage || package.SignatureKind == PackageSignatureKind.System ||
            packages.Any(x => x.Id.FullName != app.Id && x.Dependencies.Any(d => d.Id.FullName == app.Id)))
            throw new InvalidOperationException(L.T("Operation59"));
        // Windows enforces non-removable package policy; no force/remove-all-users fallback.
        // Same current-user RemovePackageAsync mechanism as BCU AppManager. No AllUsers/provisioned options.
        token.ThrowIfCancellationRequested();
        var operation = manager.RemovePackageAsync(app.Id);
        // Stop observing without forwarding cancellation into Windows deployment.
        var deployment = await operation.AsTask().WaitAsync(token);
        if (deployment.ExtendedErrorCode != null && deployment.ExtendedErrorCode.HResult != 0) throw new IOException(deployment.ErrorText);
        var remains = manager.FindPackagesForUser("").Any(x => x.Id.FullName == app.Id);
        return remains ? new("unconfirmed", L.T("Operation60"))
            : new("removed", L.T("Operation61"), true);
    }
}

internal sealed class ProcessStartEvents : IDisposable
{
    private readonly ManagementEventWatcher _watcher = new(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
    public ConcurrentQueue<(int Id, int Parent)> Starts { get; } = new();
    public bool Available { get; private set; }
    public string? FailureReason { get; private set; }
    public ProcessStartEvents()
    {
        _watcher.EventArrived += (_, e) => { try { Starts.Enqueue((Convert.ToInt32(e.NewEvent["ProcessID"]), Convert.ToInt32(e.NewEvent["ParentProcessID"]))); } catch (Exception ex) { FailureReason = ex.ToString(); Available = false; } };
        try { _watcher.Start(); Available = true; } catch (Exception ex) { FailureReason = ex.ToString(); Available = false; }
    }
    public void Dispose() { try { _watcher.Stop(); } catch { } _watcher.Dispose(); }
}

internal static class ProcessFamily
{
    // A bounded, on-demand Toolhelp snapshot tracks descendants without startup WMI or killing anything.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Id; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static List<(int Id, int Parent)> Snapshot()
    {
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var result = new List<(int, int)>(); var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" };
            if (!Process32FirstW(handle, ref entry)) throw new Win32Exception();
            do { result.Add(((int)entry.Id, (int)entry.Parent)); } while (Process32NextW(handle, ref entry));
            return result;
        }
        finally { CloseHandle(handle); }
    }
    public static async Task Wait(Process root, ProcessStartEvents trace, CancellationToken token, Action<Process>? track = null)
    {
        var known = new HashSet<int> { root.Id }; var processes = new Dictionary<int, Process>();
        var start = root.StartTime; var quiet = 0;
        try
        {
            var history = new List<(int Id, int Parent)>();
            while (quiet < 30)
            {
                token.ThrowIfCancellationRequested();
                var snapshot = Snapshot();
                while (trace.Starts.TryDequeue(out var started)) history.Add(started);
                snapshot.AddRange(history);
                bool changed;
                do
                {
                    changed = false;
                    foreach (var p in snapshot.Where(x => known.Contains(x.Parent) && !known.Contains(x.Id)))
                    {
                        try { var child = Process.GetProcessById(p.Id); if (child.StartTime >= start) { known.Add(p.Id); processes.Add(p.Id, child); track?.Invoke(child); changed = true; } else child.Dispose(); }
                        catch (ArgumentException) { known.Add(p.Id); changed = true; }
                    }
                } while (changed);
                quiet = root.HasExited && processes.Values.All(x => x.HasExited) ? quiet + 1 : 0;
                await Task.Delay(100, token);
            }
        }
        finally { foreach (var p in processes.Values) p.Dispose(); }
    }
}

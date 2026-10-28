using System.Diagnostics;
using System.Text.Json;
using ExpcUninstaller;

// No GUI, no process termination. Request is private staging data, never a shell command.
if (args.Length != 1) return 2;
var requestPath = Path.GetFullPath(args[0]);
var log = requestPath + ".result.txt";
string? backup = null;
bool launched = false;
bool oldExited = false;
ReplacementRequest? request = null;
UpdateJournal? journal = null;
InstanceSession? singleUpdate = null;
string stage = "request-read";
try
{
    SelfUpdatePolicy.NoLinks(requestPath);
    if (new FileInfo(requestPath).Length > 16384) throw new InvalidDataException("Request too large");
    request = JsonSerializer.Deserialize<ReplacementRequest>(File.ReadAllText(requestPath), SelfUpdatePolicy.Json)!;
    if (!request.ReadyEvent.StartsWith(@"Local\EXPCupdate-", StringComparison.Ordinal)) throw new InvalidDataException("Invalid handshake");
    singleUpdate = new InstanceSession(InstanceSession.Prefix + "-self-update");
    if (!singleUpdate.Primary) throw new IOException("Another update is running");
    if (request.JournalId != null)
    {
        journal = new UpdateJournal(request.JournalId);
        log = journal.Path + ".result.txt";
        using var commit = EventWaitHandle.OpenExisting(request.ReadyEvent + "-commit");
        using var accepted = EventWaitHandle.OpenExisting(request.ReadyEvent + "-helper");
        accepted.Set();
        stage = "wait-handoff"; journal.Write(stage);
        if (!commit.WaitOne(15000)) throw new TimeoutException("Client did not commit the handoff; original preserved");
    }
    stage = "wait-old-app"; journal?.Write(stage, targetPid: request.AppPid);
    Replacement.Wait(request.AppPid, request.AppStart, 60000);
    stage = "wait-old-launcher"; journal?.Write(stage, targetPid: request.LauncherPid);
    Replacement.Wait(request.LauncherPid, request.LauncherStart, 60000);
    oldExited = true;
    using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, request.ReadyEvent);
    stage = "replace"; journal?.Write(stage);
    backup = Replacement.Swap(request);
    var start = new ProcessStartInfo(request.Origin) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(request.Origin)! };
    start.Environment["EXPC_UPDATE_READY"] = request.ReadyEvent;
    stage = "start-new"; journal?.Write(stage);
    using var child = Process.Start(start) ?? throw new IOException("New launcher could not start");
    launched = true;
    stage = "handshake"; journal?.Write(stage);
    if (!ready.WaitOne(60000))
    {
        // Running new instance is never killed; leave its verified rollback file and explain.
        if (child.HasExited) { Replacement.Rollback(request.Origin, backup, request.OldHash, request.Manifest.Sha256); backup = null; launched = false; }
        throw new TimeoutException("New version did not acknowledge startup. If still running, rollback retained; no restart attempted");
    }
    stage = "cleanup"; journal?.Write(stage);
    File.Delete(backup); backup = null;
    File.Delete(request.Download);
    File.WriteAllText(log, "Updated and startup acknowledged: " + request.Manifest.Version);
    File.Delete(requestPath);
    journal?.Outcome(true, "complete");
    return 0;
}
catch (Exception ex)
{
    journal?.Write(stage + "-failed", ex);
    journal?.Outcome(false, stage + "-failed");
    // Failure before launch: rollback only if file identities still match, no relaunch loop.
    if (!launched && backup != null && request != null)
        try { Replacement.Rollback(request.Origin, backup, request.OldHash, request.Manifest.Sha256); backup = null; } catch { }
    try { File.WriteAllText(log, ex.Message + (backup == null ? "" : "\nVerified rollback retained: " + backup)); }
    catch (IOException) { } catch (UnauthorizedAccessException) { }
    if (oldExited && !launched && backup == null && request != null)
    {
        try
        {
            if (SelfUpdatePolicy.Hash(request.Origin).Equals(request.OldHash, StringComparison.OrdinalIgnoreCase))
            {
                var restore = new ProcessStartInfo(request.Origin) { UseShellExecute = false, CreateNoWindow = true };
                restore.Environment["EXPC_UPDATE_RESULT"] = log;
                using var restored = Process.Start(restore); // At most once, no update request forwarded.
            }
        }
        catch { /* Result remains in private staging; never force or loop. */ }
    }
    return 1;
}

finally { singleUpdate?.Dispose(); }

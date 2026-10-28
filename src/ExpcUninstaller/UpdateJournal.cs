using System.Text.Json;
namespace ExpcUninstaller;

// Written only for an explicitly requested self-update, never startup telemetry.
internal sealed class UpdateJournal
{
    internal string Id { get; }
    internal string Path { get; }
    internal UpdateJournal(string? id = null, string? testLogs = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(Id, "N", out _)) throw new InvalidDataException("Invalid update journal identity");
        var root = testLogs ?? RuntimeStorage.Logs;
        SelfUpdatePolicy.NoLinks(root); Directory.CreateDirectory(root);
        Path = System.IO.Path.Combine(root, "self-update-" + Id + ".jsonl");
        SelfUpdatePolicy.NoLinks(Path);
        using var writable = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
    }
    internal void Write(string stage, Exception? error = null, int? targetPid = null)
    {
        try
        {
            File.AppendAllText(Path, JsonSerializer.Serialize(new { utc=DateTime.UtcNow, pid=Environment.ProcessId,
                stage, targetPid, error=error?.GetType().Name, code=error?.HResult }) + Environment.NewLine);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal void Outcome(bool success, string stage)
    {
        Write(stage);
        try { File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "self-update-result.json"),
            JsonSerializer.Serialize(new { success, stage, journal=Id })); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

using System.Text.Json;

namespace ExpcUninstaller;

internal sealed record CleanupCheck(List<CleanupEntry> Items, string? Error = null, string? Notice = null, AppEntry? Snapshot = null);

internal sealed class StandardRemoval
{
    private int _running;
    public async Task Run(AppEntry app, OperationServices services, Action<string> status, Func<Task> refresh,
        Action<CleanupCheck> leftovers, Func<AppEntry, bool> confirm, Action<AppEntry>? unconfirmed = null)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        // Capture paths, command metadata and registration identities before external uninstall or list refresh.
        var snapshot = app with { Sources = app.Sources.ToArray(), Metadata = app.Metadata is { } m ? m with { } : null, LinkedDataPaths = app.LinkedDataPaths.ToArray() };
        void Log(string operation, object result)
        { try { OperationLog.Write(operation, snapshot.Id, JsonSerializer.Serialize(result)); } catch { } }
        void Show(CleanupCheck check)
        {
            check = check with { Snapshot = snapshot };
            Log("leftovers-result", new { count = check.Items.Count, check.Error, check.Notice });
            status(check.Error ?? L.T(check.Items.Count == 0 ? "CleanupEmpty" : "RemovalFound"));
            leftovers(check); Log("leftovers-window-shown", new { count = check.Items.Count, check.Error });
        }
        try
        {
            if (!confirm(snapshot)) { Log("uninstall-confirmation", "cancelled-before-launch"); return; }
            string? snapshotWarning = null;
            try { snapshot = snapshot with { LinkedDataPaths = BcuTargetedJunkAdapter.CaptureDataPaths(snapshot) }; }
            catch (Exception ex) { snapshotWarning = ex.Message; Log("uninstall-snapshot-partial", ex.Message); }
            Log("uninstall-snapshot", new { snapshot.Name, snapshot.Version, snapshot.Publisher, snapshot.Sources,
                location = snapshot.Metadata?.Location, key = snapshot.Metadata?.KeyName, snapshot.IconPath, snapshot.Store, snapshot.LinkedDataPaths, uninstall = snapshot.Metadata?.Uninstall, quietUninstall = snapshot.Metadata?.QuietUninstall,
                registryScopesAndViews = snapshot.Sources, productCode = snapshot.Metadata?.WindowsInstaller == "1" ? snapshot.Metadata.KeyName : null });
            status(L.T("RemovalWaiting"));
            var result = await Task.Run(() => services.Uninstall(snapshot, CancellationToken.None));
            string? refreshWarning = null;
            try { await refresh(); }
            catch (Exception ex) { refreshWarning = L.T("OperationRefreshError") + ": " + ex.Message; Log("uninstall-refresh-error", ex.Message); }
            status(result.Message + (refreshWarning == null ? "" : "\n" + refreshWarning));
            if (!result.Removed || result.State != "removed") { Log("leftovers-skipped", new { result.State, result.Message }); if (result.State is "unconfirmed" or "observation-stopped") unconfirmed?.Invoke(snapshot); return; }
            if (result.Reboot) { Show(new([], L.T("CleanupDeferred"))); return; }
            if (snapshot.Store) { Show(new([], L.T("CleanupStoreNotice"))); return; }
            status(L.T("Operation13")); Log("leftovers-start", new { snapshot.Sources, location = snapshot.Metadata?.Location });
            CleanupCheck check;
            try { check = new(await Task.Run(() => CleanupPlan.Expand(services.Find(snapshot))), Notice: refreshWarning); }
            catch (Exception ex) { check = new([], L.T("CleanupCheckError") + ": " + ex.Message, refreshWarning); }
            if (snapshotWarning != null) check = check with { Error = L.T("CleanupCheckError") + ": " + snapshotWarning };
            check = check with { Notice = string.Join("\n", new[] { check.Notice, L.T("TargetedScope") }.Where(x => !string.IsNullOrEmpty(x))) };
            Show(check);
        }
        catch (Exception ex)
        {
            status(L.T("Operation12") + ": " + ex.Message); Log("uninstall-error", ex.ToString());
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}

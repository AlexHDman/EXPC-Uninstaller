namespace ExpcUninstaller;

// No Form dependency: normal uninstall cannot construct an EXPC window before confirmed completion.
internal sealed class StandardRemoval
{
    private int _running;
    public async Task Run(AppEntry app, OperationServices services, Action<string> status, Func<Task> refresh,
        Action<List<CleanupEntry>> leftovers)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try
        {
            status(L.T("RemovalWaiting"));
            var result = await Task.Run(() => services.Uninstall(app, CancellationToken.None));
            await refresh();
            status(result.Message);
            if (!result.Removed || result.State != "removed" || result.Reboot || app.Store) return;
            status(L.T("Operation13"));
            var found = await Task.Run(() => CleanupPlan.Expand(services.Find(app)));
            status(found.Count == 0 ? L.T("Operation20") : L.T("RemovalFound"));
            if (found.Count > 0) leftovers(found);
        }
        catch (Exception ex)
        {
            status(L.T("Operation12") + ": " + ex.Message);
            try { OperationLog.Write("uninstall-error", app.Id, ex.ToString()); } catch { }
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}

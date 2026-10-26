using System.Diagnostics;
using System.Text.Json;

namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private void ShowUpdateResult()
    {
        var result = Environment.GetEnvironmentVariable("EXPC_UPDATE_RESULT");
        Environment.SetEnvironmentVariable("EXPC_UPDATE_RESULT", null);
        if (string.IsNullOrEmpty(result)) return;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EXPC", "SelfUpdate") + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(result).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            SelfUpdatePolicy.NoLinks(result);
            if (new FileInfo(result).Length < 16384) OperationStatus(L.T("SelfUpdate") + ": " + File.ReadAllText(result));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void ShowSelfUpdate()
    {
        if (_operationBusy) { Dialogs.Show(this, L.T("OperationActive")); return; }
        using var dialog = new SelfUpdateWindow(() => _operationBusy, Close);
        dialog.ShowDialog(this);
    }
}
internal sealed class SelfUpdateWindow : Form
{
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly Button update = new ThemedButton { AutoSize = true, Enabled = false };
    private readonly Button cancel = new ThemedButton { AutoSize = true };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<bool> busy;
    private readonly Action exit;
    private SelfRelease? release;
    private bool handedOff;
    private static string TextFor(string ru, string en) => L.Language == "ru" ? ru : en;
    internal SelfUpdateWindow(Func<bool> busy, Action exit)
    {
        this.busy = busy; this.exit = exit;
        Text = L.T("SelfUpdate"); ClientSize = new Size(580, 310); MinimumSize = new Size(460, 260);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Padding = new Padding(12);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        cancel.Text = L.T("Close"); update.Text = TextFor("Обновить", "Update");
        cancel.Click += (_, _) => Close(); update.Click += async (_, _) => await Install();
        bar.Controls.Add(cancel); bar.Controls.Add(update); Controls.Add(details); Controls.Add(bar);
        Theme.Apply(this); Shown += async (_, _) => await Check();
        FormClosing += (_, _) => lifetime.Cancel();
    }
    private async Task Check()
    {
        details.Text = TextFor("Проверка…", "Checking…");
        try
        {
            using var client = new SelfUpdateClient(); release = await client.Check(lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            if (release == null) { details.Text = TextFor("Обновление пока недоступно", "Update is not available yet"); return; }
            var current = SelfUpdateClient.Current; var next = SelfUpdatePolicy.ParseVersion(release.Manifest.Version);
            details.Text = TextFor("Текущая версия: ", "Current version: ") + current + "\r\n";
            if (next <= current) { details.Text += TextFor("Установлена последняя версия", "The latest version is installed"); return; }
            details.Text += TextFor("Новая версия: ", "New version: ") + next + "\r\n\r\n" + release.Notes;
            update.Enabled = !busy();
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) details.Text = TextFor("Не удалось проверить обновление: ", "Update check failed: ") + ex.Message; }
    }
    private async Task Install()
    {
        if (release == null || busy() || handedOff) return;
        update.Enabled = false;
        try
        {
            var (origin, launcher) = UpdateOrigin.Verify(); using var verifiedLauncher = launcher;
            var oldHash = SelfUpdatePolicy.Hash(origin);
            details.Text = TextFor("Загрузка и проверка…", "Downloading and verifying…");
            using var client = new SelfUpdateClient(); var download = await client.Download(release.Manifest, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (busy()) throw new IOException(L.T("OperationActive"));
            // Probe permission only; no silent UAC or modifications to the existing executable.
            var probe = origin + ".expc-access-" + Guid.NewGuid().ToString("N");
            try { using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (UnauthorizedAccessException) { throw new IOException(TextFor("Нет прав записи в папку EXE. Сохраните portable в доступной папке и повторите.", "No write access to the EXE folder. Move the portable to a writable folder and retry.")); }
            using var app = Process.GetCurrentProcess();
            var request = new ReplacementRequest(origin, download, oldHash, release.Manifest, app.Id, app.StartTime.ToUniversalTime().Ticks,
                launcher.Id, launcher.StartTime.ToUniversalTime().Ticks, @"Local\EXPCupdate-" + Guid.NewGuid().ToString("N"));
            var requestPath = Path.Combine(Path.GetDirectoryName(download)!, "request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request, SelfUpdatePolicy.Json));
            var helper = Path.Combine(AppContext.BaseDirectory, "self-update", "Expc.SelfUpdate.dll");
            if (!File.Exists(helper)) throw new IOException("Update helper missing");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(helper); start.ArgumentList.Add(requestPath);
            using var process = Process.Start(start) ?? throw new IOException("Update helper could not start");
            handedOff = true;
            Close(); exit();
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) { details.Text = TextFor("Обновление не выполнено: ", "Update not completed: ") + ex.Message; update.Enabled = true; } }
    }
    protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}

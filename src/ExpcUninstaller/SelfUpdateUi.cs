using System.Diagnostics;
using System.Text.Json;

namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private void ShowUpdateResult()
    {
        var last = Path.Combine(RuntimeStorage.Logs, "self-update-result.json");
        try
        {
            SelfUpdatePolicy.NoLinks(last);
            if (File.Exists(last) && new FileInfo(last).Length < 16384)
            {
                using var outcome = JsonDocument.Parse(File.ReadAllText(last));
                if (!outcome.RootElement.TryGetProperty("success", out var success)
                    || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
                OperationStatus(success.GetBoolean()
                    ? (L.Language == "ru" ? "Программа обновлена" : "Application updated")
                    : (L.Language == "ru" ? "Самообновление не завершено. Журнал находится в пользовательском каталоге Logs." : "Self-update did not complete. See the user Logs folder."));
                File.Delete(last); // One-time notification only; the operation journal is retained.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var result = Environment.GetEnvironmentVariable("EXPC_UPDATE_RESULT");
        Environment.SetEnvironmentVariable("EXPC_UPDATE_RESULT", null);
        if (string.IsNullOrEmpty(result)) return;
        try
        {
            var root = RuntimeStorage.UpdateCache + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(result).StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFullPath(result).StartsWith(RuntimeStorage.Logs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            SelfUpdatePolicy.NoLinks(result);
            if (new FileInfo(result).Length < 16384) OperationStatus(L.Language == "ru"
                ? "Самообновление не завершено. Исходная версия восстановлена; подробности в журнале."
                : "Self-update did not complete. The original version was restored; see the update log.");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void ShowSelfUpdate()
    {
        if (_operationBusy) { Dialogs.Show(this, L.T("OperationActive")); return; }
        using var dialog = new SelfUpdateWindow(() => _operationBusy, () => BeginInvoke(new Action(Close)));
        dialog.ShowDialog(this);
    }
}
internal sealed class SelfUpdateWindow : Form
{
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 50, AutoEllipsis = false };
    private readonly LinkLabel logLink = new() { Dock = DockStyle.Bottom, Height = 24, Visible = false };
    private readonly LinkLabel more = new() { AutoSize = true };
    private readonly Button update = new ThemedButton { AutoSize = true, Enabled = false };
    private readonly Button cancel = new ThemedButton { AutoSize = true };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<bool> busy;
    private readonly Action exit;
    private SelfRelease? release;
    private UpdateJournal? journal;
    private bool handedOff, installing;
    private static string TextFor(string ru, string en) => L.Language == "ru" ? ru : en;
    internal SelfUpdateWindow(Func<bool> busy, Action exit)
    {
        this.busy = busy; this.exit = exit;
        Text = L.T("SelfUpdate"); ClientSize = new Size(580, 310); MinimumSize = new Size(460, 300);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = MaximizeBox = false;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Padding = new Padding(12);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
        cancel.Text = TextFor("Отмена", "Cancel"); update.Text = TextFor("Обновить", "Update");
        more.Text = TextFor("Подробнее о выпуске", "Release details"); more.Enabled = false;
        more.LinkClicked += (_, _) =>
        {
            if (release == null) return;
            try { Process.Start(new ProcessStartInfo($"https://github.com/{SelfUpdatePolicy.Repository}/releases/tag/v{SelfUpdatePolicy.ParseVersion(release.Manifest.Version)}") { UseShellExecute = true }); }
            catch { status.Text = TextFor("Не удалось открыть ссылку", "Could not open the link"); }
        };
        cancel.Click += (_, _) => Close(); update.Click += async (_, _) => await Install();
        bar.Controls.Add(cancel); bar.Controls.Add(update); bar.Controls.Add(more);
        logLink.Text = TextFor("Журнал обновления", "Update log");
        logLink.LinkClicked += (_, _) =>
        {
            if (journal == null) return;
            try { Process.Start(new ProcessStartInfo(journal.Path) { UseShellExecute = true }); }
            catch { status.Text = TextFor("Журнал находится в пользовательском каталоге Logs.", "The journal is in your user Logs folder."); }
        };
        Controls.Add(details); Controls.Add(status); Controls.Add(logLink); Controls.Add(bar);
        Theme.Apply(this); more.LinkColor = more.ActiveLinkColor = logLink.LinkColor = logLink.ActiveLinkColor = GlassTeal.Text;
        Shown += async (_, _) => await Check();
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
            more.Enabled = true;
            if (next <= current) { details.Text = TextFor("Установлена последняя версия: ", "The latest version is installed: ") + current; return; }
            details.Text = TextFor("Доступна новая версия — ", "New version available — ") + next + "\r\n"
                + TextFor("Ваша версия: ", "Your version: ") + current + "\r\n\r\n";
            var summary = release.Summary(L.Language);
            details.Text += summary.Length == 0 ? TextFor("Описание изменений доступно по ссылке ниже.", "Changes are available at the link below.")
                : string.Join("\r\n", summary.Select(x => "• " + x));
            details.Text += "\r\n\r\n" + TextFor("Программа обновится и перезапустится.", "The application will update and restart.");
            update.Enabled = !busy();
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) status.Text = FriendlyError(ex); }
    }
    internal static string FriendlyError(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => TextFor("Превышено время ожидания. Повтори обновление. Рабочий EXE сохранён.", "The operation timed out. Retry the update. Your working EXE is preserved."),
        HttpRequestException => TextFor("Не удалось скачать обновление. Проверь соединение и повтори.", "Could not download the update. Check your connection and retry."),
        UnauthorizedAccessException => TextFor("Нет доступа к файлам обновления. Закрой другие копии и проверь права записи.", "Update files are inaccessible. Close other copies and check write permissions."),
        InvalidDataException => TextFor("Проверка обновления не пройдена. Текущая программа не заменена.", "Update verification failed. The current application was not replaced."),
        _ => TextFor("Обновление не выполнено. Рабочий EXE сохранён; этап и код ошибки — в журнале.", "Update failed. Your working EXE is preserved; see the log for the stage and error code.")
    };
    private async Task Install()
    {
        if (release == null || busy() || handedOff || installing) return;
        installing = true; update.Enabled = false;
        string? download = null;
        try
        {
            journal = new UpdateJournal(); logLink.Visible = true; journal.Write("origin-check");
            var (origin, launcher) = UpdateOrigin.Verify(); using var verifiedLauncher = launcher;
            var oldHash = await Task.Run(() => SelfUpdatePolicy.Hash(origin), lifetime.Token);
            journal.Write("download-start"); status.Text = TextFor("Соединение…", "Connecting…");
            var progress = new Progress<UpdateProgress>(p =>
            {
                if (IsDisposed || lifetime.IsCancellationRequested) return;
                status.Text = p.Stage == "verify" ? TextFor("Проверка файла…", "Verifying file…")
                    : TextFor("Загрузка: ", "Downloading: ") + $"{p.Bytes / 1048576d:F1} / {p.Total / 1048576d:F1} MB";
            });
            using var client = new SelfUpdateClient(); download = await client.Download(release.Manifest, lifetime.Token, progress, journal: journal);
            journal.Write("candidate-verified"); lifetime.Token.ThrowIfCancellationRequested();
            if (busy()) throw new IOException(L.T("OperationActive"));
            using var app = Process.GetCurrentProcess();
            var request = new ReplacementRequest(origin, download, oldHash, release.Manifest, app.Id, app.StartTime.ToUniversalTime().Ticks,
                launcher.Id, launcher.StartTime.ToUniversalTime().Ticks, @"Local\EXPCupdate-" + Guid.NewGuid().ToString("N"), journal.Id);
            var requestPath = Path.Combine(Path.GetDirectoryName(download)!, "request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request, SelfUpdatePolicy.Json));
            var helper = Path.Combine(AppContext.BaseDirectory, "self-update", "Expc.SelfUpdate.dll");
            if (!File.Exists(helper)) throw new IOException("Update helper missing");
            using var commit = new EventWaitHandle(false, EventResetMode.ManualReset, request.ReadyEvent + "-commit");
            using var accepted = new EventWaitHandle(false, EventResetMode.ManualReset, request.ReadyEvent + "-helper");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(helper); start.ArgumentList.Add(requestPath);
            journal.Write("helper-start"); status.Text = TextFor("Подготовка перезапуска…", "Preparing restart…");
            using var process = Process.Start(start) ?? throw new IOException("Update helper could not start");
            var acknowledged = await Task.Run(() => accepted.WaitOne(TimeSpan.FromSeconds(10)));
            if (!acknowledged) throw new TimeoutException("Helper did not accept the request");
            lifetime.Token.ThrowIfCancellationRequested();
            journal.Write("helper-accepted-exit-requested");
            commit.Set();
            handedOff = true;
            Close(); exit();
        }
        catch (Exception ex)
        {
            journal?.Write("client-failed", ex);
            if (!lifetime.IsCancellationRequested) status.Text = FriendlyError(ex);
        }
        finally
        {
            installing = false;
            if (!IsDisposed && !lifetime.IsCancellationRequested) update.Enabled = !handedOff && !busy();
            // A started helper may still own request/download; retain its recovery evidence.
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}

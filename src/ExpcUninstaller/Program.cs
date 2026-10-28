using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ExpcUninstaller;

internal static class Program
{
    private static Preferences? settings;
    internal static Preferences Settings => settings ??= PortableData.Load<Preferences>("settings");
    [STAThread]
    private static void Main()
    {
        using var instance = new InstanceSession();
        if (!instance.Primary) return;
        L.Select(PortableData.LanguageFor("", CultureInfo.InstalledUICulture));
        try { UserDataMigration.Ensure(PortablePaths.LegacyRoot, RuntimeStorage.Data); }
        catch (Exception ex) { MessageBox.Show(L.T("UserDataImportFailed") + "\n" + ex.Message); return; }
        L.Select(PortableData.LanguageFor(Settings.Language, CultureInfo.InstalledUICulture));
        if (!string.IsNullOrEmpty(Settings.Language)) Settings.Language = L.Language;
        Theme.Select(Settings.Theme);
        try { PortablePaths.VerifyWritable(); }
        catch (Exception ex) { MessageBox.Show(L.T("UserDataReadOnly") + "\n" + PortablePaths.Root + "\n" + ex.Message, UiText.Title, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var window = new MainWindow();
        using var activation = new System.Windows.Forms.Timer { Interval = 150 };
        activation.Tick += (_, _) => { if (instance.PollActivation() && !window.IsDisposed) window.ActivateExisting(); };
        activation.Start();
        window.Shown += (_, _) =>
        {
            var handshake = Environment.GetEnvironmentVariable("EXPC_UPDATE_READY");
            Environment.SetEnvironmentVariable("EXPC_UPDATE_READY", null);
            if (handshake?.StartsWith(@"Local\EXPCupdate-", StringComparison.Ordinal) == true)
                try { using var ready = EventWaitHandle.OpenExisting(handshake); ready.Set(); } catch (WaitHandleCannotBeOpenedException) { }
        };
        Application.Run(window);
    }
}

internal static class Measurements
{
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    public static double SinceProcessStart => (DateTime.UtcNow - Started).TotalMilliseconds;
    public static void Record(string stage, int count = 0, double operationMs = 0, int errors = 0, int dpi = 0)
    {
        DiagnosticLog.Append(DiagnosticLog.Enabled, RuntimeStorage.Logs, "startup-metrics.jsonl", () =>
        {
            // Aggregate measurements only. Never persist software names or registry contents.
            return JsonSerializer.Serialize(new
            {
                utc = DateTime.UtcNow,
                pid = Environment.ProcessId,
                runtime = RuntimeInformation.FrameworkDescription,
                runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(),
                stage,
                elapsedMs = Math.Round(SinceProcessStart, 2),
                operationMs = Math.Round(operationMs, 2),
                count,
                errors,
                dpi
            });
        });
    }
}

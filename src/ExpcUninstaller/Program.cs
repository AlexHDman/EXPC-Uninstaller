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
        L.Select(PortableData.LanguageFor(Settings.Language, CultureInfo.InstalledUICulture));
        if (!string.IsNullOrEmpty(Settings.Language)) Settings.Language = L.Language;
        Theme.Select(Settings.Theme);
        try { PortablePaths.VerifyWritable(); }
        catch (Exception ex) { MessageBox.Show(L.T("PortableReadOnly") + "\n" + PortablePaths.Root + "\n" + ex.Message, UiText.Title, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        // Portable folder customization: fixed packaged file only; never touch the repository's desktop.ini.
        try
        {
            var desktop = Path.Combine(AppContext.BaseDirectory, "desktop.ini");
            if (File.Exists(desktop) && File.ReadAllText(desktop).Contains("IconResource=EXPC-Uninstaller.exe,0", StringComparison.Ordinal))
            {
                File.SetAttributes(desktop, File.GetAttributes(desktop) | FileAttributes.Hidden | FileAttributes.System);
                File.SetAttributes(AppContext.BaseDirectory, File.GetAttributes(AppContext.BaseDirectory) | FileAttributes.ReadOnly);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
        try
        {
            // Aggregate measurements only. Never persist software names or registry contents.
            var json = JsonSerializer.Serialize(new
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
            File.AppendAllText(Path.Combine(PortablePaths.Root, "startup-metrics.jsonl"), json + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Read-only portable folder. */ }
    }
}

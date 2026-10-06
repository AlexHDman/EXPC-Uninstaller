using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ExpcUninstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow());
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
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-metrics.jsonl"), json + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Read-only portable folder. */ }
    }
}

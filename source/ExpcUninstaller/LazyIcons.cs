using System.Runtime.InteropServices;
using Klocman.Tools;

namespace ExpcUninstaller;

internal static class LazyIcons
{
    // Called only after rows have appeared, for visible entries, off the UI thread.
    internal static Bitmap? Load(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(metadata.Trim());
            var index = 0;
            var comma = path.LastIndexOf(',');
            if (comma > 0 && int.TryParse(path[(comma + 1)..], out var parsed))
            {
                index = parsed;
                path = path[..comma];
            }
            path = path.Trim().Trim('"');
            // Do not resolve remote icons, commands, shortcuts, or search for missing files.
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
                new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) return null;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                using var stream = File.OpenRead(path);
                using var image = Image.FromStream(stream);
                return new Bitmap(image, 32, 32);
            }
            if (extension is not (".exe" or ".dll" or ".ico")) return null;
            ExtractIconEx(path, index, IntPtr.Zero, out var handle, 1);
            if (handle == IntPtr.Zero) return null;
            using var icon = DrawingTools.CreateOwnedIconFromHandle(handle); // BCU native-handle ownership helper.
            return icon.ToBitmap();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or
                                   System.Security.SecurityException or System.Runtime.InteropServices.ExternalException)
        { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, uint count);
}

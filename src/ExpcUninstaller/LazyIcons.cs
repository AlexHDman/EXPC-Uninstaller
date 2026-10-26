using System.Runtime.InteropServices;
using System.Xml.Linq;
using Klocman.Tools;

namespace ExpcUninstaller;

internal static class LazyIcons
{
    internal static Bitmap? Load(AppEntry app)
    {
        var direct = Load(app.IconPath);
        if (direct != null) return direct;
        try
        {
            if (app.Store)
            {
                foreach (var path in StoreCandidates(app.StoreLocation)) { var icon = Load(path); if (icon != null) return icon; }
            }
            else if (app.Metadata is { } metadata)
            {
                if (Guid.TryParse(metadata.KeyName, out var product))
                {
                    // BCU's read-only ProductIcon lookup, only for a visible row after first paint.
                    var icon = Load(Klocman.IO.MsiTools.MsiGetProductInfo(product, Klocman.Native.MsiWrapper.INSTALLPROPERTY.PRODUCTICON));
                    if (icon != null) return icon;
                }
                try
                {
                    var command = UninstallCommand.Parse(metadata.Uninstall);
                    if (!command.FileName.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return Load(command.FileName);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException) { }
            }
        }
        catch (Exception ex) when (ex is SystemException) { }
        return null;
    }
    internal static (string Path, int Index) Parse(string metadata)
    {
        var path = Environment.ExpandEnvironmentVariables(metadata.Trim()).TrimStart('@');
        if (path.StartsWith('"') && path.EndsWith('"')) path = path[1..^1];
        var index = 0; var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..].Trim(), out var resource)) { index = resource; path = path[..comma]; }
        return (path.Trim().Trim('"'), index);
    }
    internal static IEnumerable<string> StoreCandidates(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !CleanupBoundary.LocalPath(directory)) return [];
        var manifest = Path.Combine(directory, "AppxManifest.xml");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 2_000_000) return [];
        var document = System.Xml.Linq.XDocument.Load(manifest);
        var paths = document.Descendants().Attributes().Where(a => a.Name.LocalName is "Square44x44Logo" or "Square150x150Logo").Select(a => a.Value)
            .Concat(document.Descendants().Where(x => x.Name.LocalName == "Logo").Select(x => x.Value)).Distinct().Take(8);
        var result = new List<string>();
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var relative in paths)
        {
            if (Path.IsPathRooted(relative) || relative.Contains(':')) continue;
            var exact = Path.GetFullPath(Path.Combine(root, relative));
            if (!exact.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(exact);
            var parent = Path.GetDirectoryName(exact)!; var stem = Path.GetFileNameWithoutExtension(exact);
            if (!Directory.Exists(parent)) continue;
            // One declared asset directory only; no recursion, package enumeration or disk search.
            result.AddRange(Directory.EnumerateFiles(parent, stem + ".*.png", SearchOption.TopDirectoryOnly).Take(64)
                .OrderByDescending(p => p.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.Contains("scale-200", StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }
    // Called only after rows have appeared, for visible entries, off the UI thread.
    internal static Bitmap? Load(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            var (path, index) = Parse(metadata);
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
            if (extension == ".ico") { using var file = File.OpenRead(path); using var ico = new Icon(file, 32, 32); return ico.ToBitmap(); }
            if (extension is not (".exe" or ".dll")) return null;
            ExtractIconEx(path, index, out var large, out var small, 1);
            if (large == IntPtr.Zero && small == IntPtr.Zero) return null;
            if (large != IntPtr.Zero && small != IntPtr.Zero) { using var unused = DrawingTools.CreateOwnedIconFromHandle(small); }
            using var icon = DrawingTools.CreateOwnedIconFromHandle(large != IntPtr.Zero ? large : small); // BCU native-handle ownership helper.
            return icon.ToBitmap();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or
                                   System.Security.SecurityException or System.Runtime.InteropServices.ExternalException)
        { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
}

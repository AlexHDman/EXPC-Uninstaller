using System.Security.Cryptography;
using System.Text;
namespace ExpcUninstaller;
internal static class UpdatePolicy
{
    internal static string Identity(AppEntry app) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        app.Id, app.Publisher, app.Store, app.StoreLocation, app.Metadata?.Location,
        string.Join("|", app.Sources.Order(StringComparer.OrdinalIgnoreCase))))));
    internal static bool Driver(AppEntry app) => System.Text.RegularExpressions.Regex.IsMatch(app.Name,
        @"(?i)(\bdrivers?\b|драйвер)", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static string Reason(AppEntry app, VersionResult result) => Driver(app) ? "DriverSeparate" : result.SelectionReason;
    internal static bool Ready(AppEntry app, VersionResult result) => !Driver(app) && result.CanInstall;
}
internal sealed class IgnoredUpdates
{
    private readonly HashSet<string> _ids = PortableData.Load<HashSet<string>>("ignored-updates");
    internal bool Contains(AppEntry app) => _ids.Contains(UpdatePolicy.Identity(app));
    internal void Set(AppEntry app, bool ignored)
    {
        var next = new HashSet<string>(_ids); if (ignored) next.Add(UpdatePolicy.Identity(app)); else next.Remove(UpdatePolicy.Identity(app));
        PortableData.Save("ignored-updates", next); _ids.Clear(); _ids.UnionWith(next);
    }
}

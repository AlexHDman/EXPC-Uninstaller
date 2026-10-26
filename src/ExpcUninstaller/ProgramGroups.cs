using System.Text.RegularExpressions;

namespace ExpcUninstaller;

internal sealed record ProgramRow(string Key, string Title, AppEntry? App, string? Group, bool Expanded, int Count)
{
    internal bool IsGroup => App == null;
    internal bool IsChild => App != null && Group != null;
}

internal static class ProgramGroups
{
    internal static string Family(AppEntry app) => Regex.IsMatch(app.Name,
        @"^Microsoft\s+Visual\s+C\+\+.*\bRedistributable\b", RegexOptions.IgnoreCase)
        ? "family:vc-redistributable" : "name:" + app.Name.Trim().ToUpperInvariant();

    internal static List<ProgramRow> Build(IReadOnlyList<AppEntry> all, string search, int sort, bool descending, ISet<string> expanded)
    {
        var families = all.GroupBy(Family).ToDictionary(g => g.Key, g => g.Count());
        var selected = CatalogView.Select(all, search, sort, descending);
        var result = new List<ProgramRow>();
        // First matching child determines a group's order under the current column sort.
        foreach (var group in selected.GroupBy(Family))
        {
            var children = group.ToList();
            if (families[group.Key] == 1)
            {
                var app = children[0]; result.Add(new("app:" + app.Id, app.Name, app, null, false, 1)); continue;
            }
            bool open = !string.IsNullOrWhiteSpace(search) || expanded.Contains(group.Key);
            var title = group.Key == "family:vc-redistributable" ? "Microsoft Visual C++ Redistributable" : children[0].Name;
            result.Add(new("group:" + group.Key, title + " (" + children.Count + ")", null, group.Key, open, children.Count));
            if (open) result.AddRange(children.Select(app => new ProgramRow("app:" + app.Id, app.Name, app, group.Key, false, 1)));
        }
        return result;
    }

    internal static string Detail(AppEntry app) => string.Join("\n", new[] { app.Name, app.Version, app.Publisher,
        app.Metadata?.Location, app.Metadata?.Uninstall, string.Join("; ", app.Sources) }.Where(x => !string.IsNullOrWhiteSpace(x)));
    internal static bool UninstallOnDoubleClick(ProgramRow row, bool plusHit) => !row.IsGroup && !plusHit;
}

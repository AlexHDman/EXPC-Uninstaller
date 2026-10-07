using System.Globalization;
using System.Text.Json;

namespace ExpcUninstaller;

internal static class L
{
    public static readonly Dictionary<string, string> Languages = new()
    { ["ru"] = "Русский", ["en"] = "English", ["de"] = "Deutsch", ["fr"] = "Français", ["es"] = "Español", ["it"] = "Italiano", ["pt"] = "Português" };
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = Languages.Keys.ToDictionary(x => x, Read);
    public static string Language { get; private set; } = "en";
    private static Dictionary<string, string> Read(string language)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"Expc.Locale.{language}.json");
        var result = stream == null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        using var extra = typeof(L).Assembly.GetManifestResourceStream("Expc.Locale.updates.json");
        if (extra != null)
        {
            var index = Array.IndexOf(new[] { "ru", "en", "de", "fr", "es", "it", "pt" }, language);
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string[]>>(extra)!) result[pair.Key] = pair.Value[index];
        }
        return result;
    }
    public static void Select(string language)
    {
        Language = Languages.ContainsKey(language) ? language : "en";
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(Language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Language);
    }
    public static string T(string key) => Catalogs[Language].GetValueOrDefault(key) ?? Catalogs["en"].GetValueOrDefault(key) ?? key;
    public static string F(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, T(key), values);
    internal static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[language];
}

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
        return stream == null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
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

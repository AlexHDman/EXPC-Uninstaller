using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExpcUninstaller;

internal sealed class Preferences
{
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "Glass Teal";
    public string[] ExpandedGroups { get; set; } = [];
    public int[] ColumnWidths { get; set; } = [];
    public SavedWindow? Window { get; set; }
}

internal static class PortableData
{
    public static string DirectoryPath { get; set; } = Path.Combine(PortablePaths.Root, "local-data");
    public static string? ReadError { get; private set; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, RespectNullableAnnotations = true };
    public static T Load<T>(string name) where T : new()
    {
        try
        {
            var file = Path.Combine(DirectoryPath, name + ".json");
            if (!File.Exists(file)) return new();
            if (new FileInfo(file).Length > 4_000_000) throw new IOException("Local data too large");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { ReadError = ex.Message; return new(); }
    }
    public static void Save<T>(string name, T data)
    {
        Directory.CreateDirectory(DirectoryPath);
        var file = Path.Combine(DirectoryPath, name + ".json");
        // Keep the last readable settings and use an atomic rename on the same volume.
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
        if (File.Exists(file)) File.Copy(file, file + ".bak", true);
        File.Move(temp, file, true);
    }
    public static string Identity(AppEntry app) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', app.Id, app.Name, app.Publisher, app.Store))));
    public static string LanguageFor(string requested, CultureInfo windows) =>
        L.Languages.ContainsKey(requested) ? requested : !string.IsNullOrEmpty(requested) ? "en"
            : windows.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
}

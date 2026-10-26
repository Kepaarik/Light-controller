using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedHub.Services;

/// <summary>
/// Хранит настройки в %AppData%\LedHub\config.json.
/// </summary>
public static class ConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedHub", "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath))
            return new AppConfig();

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
    }
}

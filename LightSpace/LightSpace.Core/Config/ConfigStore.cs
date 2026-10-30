using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LightSpace.Core.Config;

/// <summary>
/// Хранит настройки в %AppData%\LightSpace\config.json и обновляет файлы старых версий при загрузке.
/// </summary>
public static class ConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightSpace", "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Шаги обновления формата: шаг с индексом N переводит файл из версии N в версию N + 1.
    /// </summary>
    private static readonly Action<JsonObject>[] Migrations =
    {
        // 0 → 1: файлы без поля Version; формат совпадает с первой версией
        _ => { },

        // 1 → 2: появился список известных устройств Devices; в старых файлах его нет, он начинается пустым
        _ => { }
    };

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath))
            return new AppConfig();

        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject root)
                return new AppConfig();

            Migrate(root);

            var config = root.Deserialize<AppConfig>(Options) ?? new AppConfig();
            config.Normalize();

            return config;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Сначала во временный файл: если приложение упадёт посреди записи, старые настройки останутся целыми
        var temporary = FilePath + ".tmp";

        File.WriteAllText(temporary, JsonSerializer.Serialize(config, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>
    /// Применяет по очереди все шаги обновления от версии файла до текущей.
    /// </summary>
    private static void Migrate(JsonObject root)
    {
        var version = root[nameof(AppConfig.Version)]?.GetValue<int>() ?? 0;

        // Файл из более новой сборки читаем как есть: незнакомые поля просто пропускаются
        if (version >= AppConfig.CurrentVersion)
            return;

        for (var step = Math.Max(version, 0); step < Migrations.Length && step < AppConfig.CurrentVersion; step++)
            Migrations[step](root);

        root[nameof(AppConfig.Version)] = AppConfig.CurrentVersion;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LightSpace.Core.Config;

/// <summary>
/// Хранит настройки в %AppData%\LightSpace\config.json и обновляет файлы старых версий при загрузке.
/// </summary>
public static class ConfigStore
{
    // Общие для всех устройств яркость и содержимое в файлах версии 3; с версии 4 они хранятся в каждой группе
    private const string SharedBrightnessKey = "Brightness";
    private const string SharedContentKey = "DefaultContent";

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
        _ => { },

        // 2 → 3: появились общая яркость Brightness и содержимое DefaultContent; ручной цвет устройств переезжает в них
        MoveDeviceColorToContent,

        // 3 → 4: появились группы Groups со своей яркостью и содержимым; общие Brightness и DefaultContent переезжают
        // в группы-одиночки всех устройств, поэтому после обновления каждое устройство играет то же, что и до него
        MoveContentToGroups
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

    /// <summary>
    /// Раньше у каждого устройства был свой ручной цвет, теперь на всех устройствах играет один эффект.
    /// Цвет первого устройства, которому его задавали, становится эффектом «Сплошной цвет», его яркость — общей яркостью.
    /// </summary>
    private static void MoveDeviceColorToContent(JsonObject root)
    {
        var device = (root["Devices"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(d => d["Color"] is JsonValue color && color.TryGetValue<string>(out _));

        if (device == null)
            return;

        var hex = device["Color"]!.GetValue<string>();

        if (device["Brightness"] is JsonValue brightness && brightness.TryGetValue<int>(out var value))
            root[SharedBrightnessKey] = value;

        root[SharedContentKey] = new JsonObject
        {
            [nameof(ContentConfig.EffectId)] = "solid",
            [nameof(ContentConfig.Effects)] = new JsonObject
            {
                ["solid"] = new JsonObject
                {
                    [nameof(EffectValuesConfig.Colors)] = new JsonObject { ["color"] = hex }
                }
            }
        };
    }

    /// <summary>
    /// Раньше на всех устройствах играло одно содержимое с одной яркостью, теперь у каждой группы своё.
    /// Каждое известное устройство становится группой-одиночкой с копией прежних содержимого и яркости.
    /// </summary>
    private static void MoveContentToGroups(JsonObject root)
    {
        var brightness = root[SharedBrightnessKey];
        var content = root[SharedContentKey];
        var groups = new JsonArray();

        var devices = (root[nameof(AppConfig.Devices)] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

        foreach (var device in devices)
        {
            if (device[nameof(DeviceConfig.Id)] is not JsonValue id || !id.TryGetValue<string>(out var deviceId) ||
                string.IsNullOrWhiteSpace(deviceId))
                continue;

            var group = new JsonObject
            {
                [nameof(GroupConfig.Id)] = GroupConfig.NewId(),
                [nameof(GroupConfig.IsSolo)] = true,
                [nameof(GroupConfig.DeviceIds)] = new JsonArray { deviceId }
            };

            // Чего не было в файле, группа берёт по умолчанию — как и раньше брали общие настройки
            if (brightness != null)
                group[nameof(GroupConfig.Brightness)] = brightness.DeepClone();

            if (content != null)
                group[nameof(GroupConfig.DefaultContent)] = content.DeepClone();

            groups.Add(group);
        }

        root.Remove(SharedBrightnessKey);
        root.Remove(SharedContentKey);
        root[nameof(AppConfig.Groups)] = groups;
    }
}

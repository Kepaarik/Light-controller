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

    // Поля групп и всей сцены в файлах версии 4; с версии 5 групп и всей сцены нет
    private const string GroupsKey = "Groups";
    private const string SceneKey = "Scene";
    private const string ModeKey = "Mode";
    private const string GroupIdKey = "Id";
    private const string GroupIsSoloKey = "IsSolo";
    private const string GroupDeviceIdsKey = "DeviceIds";
    private const string GroupBrightnessKey = "Brightness";
    private const string GroupContentKey = "DefaultContent";

    // Расстановка устройства на сцене и слои сцены в содержимом в файлах версии 4
    private const string PlacementKey = "Placement";
    private const string LayersKey = "Layers";
    private const string SceneKind = "Scene";

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
        MoveContentToGroups,

        // 4 → 5: группы, вся сцена и расстановка устройств убраны; подсветка устройства хранит одно устройство
        // и его содержимое, поэтому после обновления каждое устройство играет то же, что и до него
        MoveGroupsToDeviceLighting
    };

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath))
            return new AppConfig();

        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Читает настройки из текста файла любой версии и приводит их к текущей.
    /// </summary>
    /// <exception cref="JsonException">Текст — не JSON или не настройки.</exception>
    /// <exception cref="InvalidOperationException">Значение в файле не того типа.</exception>
    public static AppConfig Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
            return new AppConfig();

        Migrate(root);

        var config = root.Deserialize<AppConfig>(Options) ?? new AppConfig();
        config.Normalize();

        return config;
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
                [GroupIdKey] = ProfileConfig.NewId(),
                [GroupIsSoloKey] = true,
                [GroupDeviceIdsKey] = new JsonArray { deviceId }
            };

            // Чего не было в файле, группа берёт по умолчанию — как и раньше брали общие настройки
            if (brightness != null)
                group[GroupBrightnessKey] = brightness.DeepClone();

            if (content != null)
                group[GroupContentKey] = content.DeepClone();

            groups.Add(group);
        }

        root.Remove(SharedBrightnessKey);
        root.Remove(SharedContentKey);
        root[GroupsKey] = groups;
    }

    /// <summary>
    /// Раньше подсветка устройства была группой-одиночкой, а рядом жили группы, вся сцена и расстановка устройств.
    /// Теперь у подсветки одно устройство и его содержимое: записи устройств переводятся в новый вид, устройство без
    /// своей записи берёт содержимое и яркость своей группы, а сцена в содержимом становится обычным эффектом.
    /// </summary>
    private static void MoveGroupsToDeviceLighting(JsonObject root)
    {
        var lighting = ToDeviceLighting(root[nameof(AppConfig.DeviceLighting)] as JsonArray);
        var covered = lighting.OfType<JsonObject>().Select(l => l[nameof(DeviceLightingConfig.DeviceId)]!.GetValue<string>()).ToHashSet();

        // До версии 4 содержимое жило в группах: устройство без своей записи играет то же, что его группа
        foreach (var group in (root[GroupsKey] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            foreach (var deviceId in DeviceIdsOf(group).Where(covered.Add))
                lighting.Add(LightingOf(deviceId, group));
        }

        root[nameof(AppConfig.DeviceLighting)] = lighting;

        foreach (var profile in (root[nameof(AppConfig.Profiles)] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            profile[nameof(ProfileConfig.DeviceLighting)] = ToDeviceLighting(profile[nameof(ProfileConfig.DeviceLighting)] as JsonArray);

        foreach (var device in (root[nameof(AppConfig.Devices)] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            device.Remove(PlacementKey);

        root.Remove(GroupsKey);
        root.Remove(SceneKey);
        root.Remove(ModeKey);
    }

    /// <summary>
    /// Записи подсветки в виде групп-одиночек — в записи нового вида; запись без устройства пропадает.
    /// </summary>
    private static JsonArray ToDeviceLighting(JsonArray? groups)
    {
        var lighting = new JsonArray();

        foreach (var group in groups?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (DeviceIdsOf(group).FirstOrDefault() is { } deviceId)
                lighting.Add(LightingOf(deviceId, group));
        }

        return lighting;
    }

    /// <summary>
    /// Подсветка устройства с яркостью и содержимым группы; чего не было в группе, берётся по умолчанию.
    /// </summary>
    private static JsonObject LightingOf(string deviceId, JsonObject group)
    {
        var lighting = new JsonObject { [nameof(DeviceLightingConfig.DeviceId)] = deviceId };

        if (group[GroupBrightnessKey] is { } brightness)
            lighting[nameof(DeviceLightingConfig.Brightness)] = brightness.DeepClone();

        if (group[GroupContentKey] is JsonObject content)
            lighting[nameof(DeviceLightingConfig.Content)] = WithoutScene(content);

        return lighting;
    }

    /// <summary>
    /// Копия содержимого без слоёв сцены; содержимое-сцена становится обычным эффектом.
    /// </summary>
    private static JsonObject WithoutScene(JsonObject content)
    {
        var copy = (JsonObject)content.DeepClone();

        copy.Remove(LayersKey);

        if (copy[nameof(ContentConfig.Kind)] is JsonValue kind && kind.TryGetValue<string>(out var name) && name == SceneKind)
            copy[nameof(ContentConfig.Kind)] = nameof(ContentKind.Effect);

        return copy;
    }

    /// <summary>
    /// Непустые идентификаторы устройств группы по порядку.
    /// </summary>
    private static IEnumerable<string> DeviceIdsOf(JsonObject group)
    {
        return (group[GroupDeviceIdsKey] as JsonArray)?
            .OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var id) ? id : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!) ?? Enumerable.Empty<string>();
    }
}

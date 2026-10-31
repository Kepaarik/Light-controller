using System.Text.Json;
using System.Text.Json.Nodes;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices.Tuya;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Sources;

namespace LightSpace.Engine.Import;

/// <summary>
/// Итог импорта настроек LedHub: что перенесено и что пропущено, по строке на каждый раздел.
/// </summary>
public sealed class LedHubImportResult
{
    /// <summary>
    /// Что перенесено в настройки Light Space.
    /// </summary>
    public List<string> Imported { get; } = new();

    /// <summary>
    /// Что пропущено: уже настроено в Light Space, не распознано или в Light Space такого нет.
    /// </summary>
    public List<string> Skipped { get; } = new();
}

/// <summary>
/// Перенос настроек из файла старого приложения LedHub (%AppData%\LedHub\config.json) в настройки Light Space:
/// известные устройства с именами и ключами Tuya, расстановка на сцене, группы, настройки эффектов и игровые режимы
/// Dota 2 и CS2 как сценарии. Только добавляет: то, что уже настроено в Light Space, не меняется. Файл LedHub только
/// читается.
/// </summary>
public static class LedHubImport
{
    /// <summary>
    /// Где лежат настройки LedHub.
    /// </summary>
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedHub", "config.json");

    // Начала идентификаторов устройств, которые умеют драйверы Light Space; они такие же, как в LedHub
    private static readonly string[] DevicePrefixes = { "ble:", "govee:", "magichome:", "robobloq:", "rk:", "serial:", "tuya:" };

    // Клавиатуры среди устройств LedHub — для игровой подсветки на клавиатурах
    private const string KeyboardPrefix = "rk:";

    private const string MonitorPrefix = "monitor:";

    // Отступ от края сцены, когда расстановка LedHub вписывается в сцену Light Space
    private const double SceneMargin = 10;

    /// <summary>
    /// Есть ли файл настроек LedHub.
    /// </summary>
    public static bool IsAvailable => File.Exists(FilePath);

    /// <summary>
    /// Переносит настройки LedHub в config. nextGroupName и nextGroupColor дают название и цвет обводки новой группы,
    /// если в LedHub у группы не было названия. Нераспознанное пропускается и попадает в итог.
    /// </summary>
    public static LedHubImportResult Run(AppConfig config, Func<string> nextGroupName, Func<string> nextGroupColor)
    {
        var result = new LedHubImportResult();
        JsonObject root;

        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject
                   ?? throw new JsonException("в файле нет настроек");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            result.Skipped.Add($"Файл настроек LedHub не прочитан: {e.Message}");
            return result;
        }

        var names = ReadDeviceNames(root);

        ImportDevices(config, root, names, result);
        ImportKeys(config, root, result);
        ImportPlacements(config, root, result);
        ImportGroups(config, root, nextGroupName, nextGroupColor, result);

        var effects = ReadEffects(root);

        ImportEffects(config, effects, result);
        ImportGames(config, root, effects, result);
        ReportLeftBehind(root, result);

        // Группы, сценарии и места устройств приводятся к допустимым так же, как при загрузке файла
        config.Normalize();

        return result;
    }

    /// <summary>
    /// Устройства, которые LedHub когда-либо видел: у LedHub нет общего списка, поэтому они собираются по всем разделам,
    /// где упоминаются. Новые устройства появляются в Light Space ещё до поиска, каждое своей группой-одиночкой.
    /// </summary>
    private static void ImportDevices(AppConfig config, JsonObject root, Dictionary<string, string> names, LedHubImportResult result)
    {
        var ids = new List<string>();

        ids.AddRange(Keys(root["Tuya"]).Select(id => "tuya:" + id));
        ids.AddRange(names.Keys);
        ids.AddRange(Objects(root["Groups"]).SelectMany(g => Objects(g["Members"])).Select(m => Text(m["Id"]) ?? ""));
        ids.AddRange(Objects(root["SceneItems"]).Select(i => Text(i["Key"]) ?? ""));
        ids.AddRange(Objects(root["Monitors"] as JsonObject).Select(m => Text(m["DeviceId"]) ?? ""));
        ids.AddRange(Objects(root["Scenes"]).SelectMany(s => Objects(s["Devices"])).Select(d => Text(d["DeviceId"]) ?? ""));
        ids.AddRange(Keys(root["HexLayouts"]));
        ids.AddRange(Keys(root["ScreenLayouts"]));

        var known = ids
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith(MonitorPrefix, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        var unknown = known.Count(id => !IsSupported(id));
        var added = 0;
        var named = 0;

        foreach (var id in known.Where(IsSupported))
        {
            var name = names.GetValueOrDefault(id);

            if (config.Devices.FirstOrDefault(d => d.Id == id) is { } device)
            {
                // Своё имя из LedHub получает и уже известное устройство, если своего имени у него ещё нет
                if (name != null && string.IsNullOrWhiteSpace(device.CustomName))
                {
                    device.CustomName = name;
                    named++;
                }

                continue;
            }

            config.Devices.Add(new DeviceConfig { Id = id, Name = name ?? id, CustomName = name, Address = AddressOf(id) });
            config.EnsureGroup(id);
            added++;
        }

        var existing = known.Count(IsSupported) - added;

        if (added > 0)
            result.Imported.Add($"Устройства: {added}. Они видны сразу и подключатся при следующем поиске.");

        if (existing > 0)
            result.Skipped.Add($"Устройства, которые Light Space уже знает: {existing}.");

        if (unknown > 0)
            result.Skipped.Add($"Устройства неизвестного вида: {unknown}.");

        if (named > 0)
            result.Imported.Add($"Свои имена уже известных устройств: {named}.");
    }

    /// <summary>
    /// Локальные ключи и наборы точек данных Tuya; ключ, уже заданный в Light Space, не меняется.
    /// </summary>
    private static void ImportKeys(AppConfig config, JsonObject root, LedHubImportResult result)
    {
        var added = 0;
        var kept = 0;

        foreach (var (deviceId, node) in Pairs(root["Tuya"]))
        {
            var key = Text(node?["LocalKey"]);
            var device = config.Devices.FirstOrDefault(d => d.Id == "tuya:" + deviceId);

            if (device == null || string.IsNullOrWhiteSpace(key))
                continue;

            if (!string.IsNullOrWhiteSpace(device.LocalKey))
            {
                kept++;
                continue;
            }

            device.LocalKey = key.Trim();

            // Неизвестный набор точек данных оставляем по умолчанию
            if (Text(node?["DpScheme"]) is { } scheme && Enum.TryParse<TuyaDpScheme>(scheme, out var parsed))
                device.DataPoints = parsed.ToString();

            added++;
        }

        if (added > 0)
            result.Imported.Add($"Ключи Tuya: {added}.");

        if (kept > 0)
            result.Skipped.Add($"Ключи Tuya, которые уже заданы в Light Space: {kept}.");
    }

    /// <summary>
    /// Места лент, клавиатур и панелей на сцене. Переносятся, только пока в Light Space не расставлено ни одно
    /// устройство: иначе две расстановки легли бы друг на друга. Вся расстановка LedHub сдвигается в середину сцены
    /// и, если не помещается, уменьшается целиком.
    /// </summary>
    private static void ImportPlacements(AppConfig config, JsonObject root, LedHubImportResult result)
    {
        var items = Objects(root["SceneItems"])
            .Select(i => (Key: Text(i["Key"]) ?? "", Kind: Text(i["Kind"]) ?? "", X: Number(i["X"]), Y: Number(i["Y"]),
                Size: Number(i["Size"]) ?? 0, Rotation: Number(i["Rotation"]) ?? 0, IsReversed: Flag(i["IsReversed"]) ?? false))
            .Where(i => i.X is { } x && i.Y is { } y && double.IsFinite(x) && double.IsFinite(y))
            .ToList();

        var devices = items.Where(i => config.Devices.Any(d => d.Id == i.Key)).ToList();

        if (devices.Count == 0)
            return;

        if (config.Devices.Any(d => d.Placement != null))
        {
            result.Skipped.Add("Расстановка на сцене: в Light Space устройства уже расставлены.");
            return;
        }

        // Мониторы входят в рамку, чтобы устройства вокруг них стояли так же, как в LedHub
        var left = items.Min(i => i.X!.Value);
        var right = items.Max(i => i.X!.Value);
        var top = items.Min(i => i.Y!.Value);
        var bottom = items.Max(i => i.Y!.Value);

        var scale = Math.Min(1, Math.Min(
            (SceneArea.Width - SceneMargin * 2) / Math.Max(right - left, 1),
            (SceneArea.Height - SceneMargin * 2) / Math.Max(bottom - top, 1)));

        foreach (var item in devices)
        {
            // У панели из шестигранников в Light Space нет своей формы — она получает размер по умолчанию
            var size = item.Kind == "Panel" ? 0 : item.Size * scale;

            // Лента «в обратную сторону» — то же, что лента, повёрнутая на пол-оборота
            var rotation = item.Rotation + (item.IsReversed ? 180 : 0);

            config.Devices.First(d => d.Id == item.Key).Placement = new DevicePlacement
            {
                X = SceneArea.Width / 2 + (item.X!.Value - (left + right) / 2) * scale,
                Y = SceneArea.Height / 2 + (item.Y!.Value - (top + bottom) / 2) * scale,
                Size = size,
                Rotation = rotation
            };
        }

        result.Imported.Add($"Места устройств на сцене: {devices.Count}.");
    }

    /// <summary>
    /// Группы LedHub становятся группами Light Space. Группа пропускается, если хоть одно её устройство уже входит
    /// в группу Light Space, — устройство не может быть в двух группах.
    /// </summary>
    private static void ImportGroups(AppConfig config, JsonObject root, Func<string> nextGroupName, Func<string> nextGroupColor,
        LedHubImportResult result)
    {
        var made = 0;
        var skipped = 0;

        foreach (var group in Objects(root["Groups"]))
        {
            var ids = Objects(group["Members"])
                .Select(m => Text(m["Id"]) ?? "")
                .Where(id => config.Devices.Any(d => d.Id == id))
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                continue;

            if (ids.Any(id => config.GroupOf(id) is { IsSolo: false }))
            {
                skipped++;
                continue;
            }

            var name = Text(group["Name"]);

            config.MakeGroup(ids, string.IsNullOrWhiteSpace(name) ? nextGroupName() : name.Trim(), nextGroupColor());
            made++;
        }

        if (made > 0)
            result.Imported.Add($"Группы: {made}.");

        if (skipped > 0)
            result.Skipped.Add($"Группы, устройства которых уже в группах Light Space: {skipped}.");
    }

    /// <summary>
    /// Настройки эффектов LedHub были общими для всего приложения; в Light Space они хранятся в содержимом каждой группы.
    /// Каждая группа получает настройки тех эффектов, которые в ней ещё не меняли.
    /// </summary>
    private static void ImportEffects(AppConfig config, Dictionary<string, EffectValuesConfig> effects, LedHubImportResult result)
    {
        if (effects.Count == 0)
            return;

        foreach (var group in config.Groups)
            AddEffects(group.DefaultContent, effects);

        result.Imported.Add($"Настройки эффектов: {effects.Count}. Они достались эффектам, которые в группах ещё не настраивали.");
    }

    /// <summary>
    /// Игровые режимы Dota 2 и CS2 становятся сценариями «запущен» с содержимым-игрой: фон, яркость фона, полоски
    /// и выключенные события — как были в LedHub. Сценарий встаёт в группы, где LedHub показывал игру: во все, если
    /// игра шла на всех лентах, иначе — в группы подсветки главного монитора и клавиатур.
    /// </summary>
    private static void ImportGames(AppConfig config, JsonObject root, Dictionary<string, EffectValuesConfig> effects,
        LedHubImportResult result)
    {
        if (root["Games"] is not JsonObject games)
            return;

        var targets = GameGroups(config, root, games);

        foreach (var (key, game, exe, title) in new[]
                 {
                     ("Dota", GameKind.Dota2, "dota2.exe", "Dota 2"),
                     ("CounterStrike", GameKind.CounterStrike2, "cs2.exe", "Counter-Strike 2")
                 })
        {
            var profile = games[key] as JsonObject;

            if (profile == null || Flag(profile["IsEnabled"]) == false)
                continue;

            if (config.Scenarios.Any(s => string.Equals(s.Condition.ExeName, exe, StringComparison.OrdinalIgnoreCase)))
            {
                result.Skipped.Add($"Сценарий {title}: в Light Space уже есть сценарий для {exe}.");
                continue;
            }

            var content = new ContentConfig();

            AddEffects(content, effects);

            // Вместо игровой подсветки LedHub мог включать пресет — цвета устройств; сценарий берёт цвет первого горящего
            if (Text(profile["Preset"]) is { Length: > 0 } preset)
            {
                content.Kind = ContentKind.Effect;
                content.EffectId = EffectLibrary.Solid.Id;
                content.Effects[EffectLibrary.Solid.Id] = new EffectValuesConfig
                {
                    Colors = new Dictionary<string, string> { ["color"] = PresetColor(root, preset).ToHex() }
                };

                result.Skipped.Add($"Сценарий {title} включал пресет «{preset}»: в Light Space он стал одним цветом — первого горящего устройства пресета.");
            }
            else
            {
                content.Kind = ContentKind.Game;
                content.Game = GameOf(game, games, root);
            }

            var scenario = new ScenarioConfig
            {
                Id = GroupConfig.NewId(),
                Name = title,
                Condition = new ScenarioCondition { ExePath = exe, Mode = ConditionMode.Running },
                Content = content
            };

            config.Scenarios.Add(scenario);

            foreach (var group in targets)
                group.ScenarioIds.Add(scenario.Id);

            result.Imported.Add(targets.Count > 0
                ? $"Сценарий {title}: включается, пока игра запущена, в группах: {targets.Count}."
                : $"Сценарий {title}: включается, пока игра запущена; группы для него отметьте сами.");
        }

        if (games["OtherGames"] is JsonObject other && Flag(other["IsEnabled"]) != false)
            result.Skipped.Add("Подсветка «Другие игры»: LedHub узнавал их по окну на весь экран, а сценарию Light Space нужен exe-файл игры — создайте сценарий «Игра → Другие игры» для каждой игры.");
    }

    /// <summary>
    /// Группы, где LedHub показывал игру.
    /// </summary>
    private static List<GroupConfig> GameGroups(AppConfig config, JsonObject root, JsonObject games)
    {
        if (Flag(games["UseAllDevices"]) == true)
            return config.Groups.ToList();

        var ids = new List<string>();

        // Подсветка главного монитора; без выбора — основного монитора Windows
        var monitorKey = Monitors.Resolve(Text(root["MainMonitor"]) ?? "")?.Key;

        if (monitorKey != null && Text((root["Monitors"] as JsonObject)?[monitorKey]?["DeviceId"]) is { } backlight)
            ids.Add(backlight);

        if (Flag(games["UseKeyboards"]) != false)
            ids.AddRange(config.Devices.Select(d => d.Id).Where(id => id.StartsWith(KeyboardPrefix, StringComparison.Ordinal)));

        return ids.Select(config.GroupOf).OfType<GroupConfig>().Distinct().ToList();
    }

    /// <summary>
    /// Настройки содержимого-игры из общих игровых настроек LedHub.
    /// </summary>
    private static GameConfig GameOf(GameKind game, JsonObject games, JsonObject root)
    {
        var settings = new GameConfig
        {
            Game = game,
            MonitorKey = Text(root["MainMonitor"]) ?? "",

            // В LedHub полоски на клавиатуре выключались отдельным вариантом, остальные варианты — полоски на всю клавиатуру
            KeyboardBars = Text(games["KeyboardHud"]) != "Off",
            StripBars = Flag(games["BarsOnOtherDevices"]) ?? true,
            DisabledEvents = Strings(games["DisabledTriggers"]).Where(e => e.StartsWith($"{game}:", StringComparison.Ordinal)).ToList()
        };

        if (Text(games["Background"]) is { } background && Enum.TryParse<GameBackground>(background, out var parsed))
            settings.Background = parsed;

        if (Number(games["BackgroundBrightness"]) is { } brightness)
            settings.BackgroundBrightness = (int)Math.Round(brightness);

        settings.Normalize();

        return settings;
    }

    /// <summary>
    /// Цвет первого горящего устройства пресета; если таких нет — чёрный.
    /// </summary>
    private static RgbColor PresetColor(JsonObject root, string preset)
    {
        var scene = Objects(root["Scenes"]).FirstOrDefault(s => Text(s["Name"]) == preset);
        var lit = Objects(scene?["Devices"]).FirstOrDefault(d => Flag(d["IsOn"]) == true);

        return lit != null && ColorOf(lit["Color"]) is { } color ? color : RgbColor.Black;
    }

    /// <summary>
    /// Что из LedHub в Light Space не переносится, потому что такого здесь нет.
    /// </summary>
    private static void ReportLeftBehind(JsonObject root, LedHubImportResult result)
    {
        var presets = Objects(root["Scenes"]).Count();

        if (presets > 0)
            result.Skipped.Add($"Пресеты (сохранённые цвета устройств): {presets} — в Light Space вместо них сценарии и «По умолчанию» групп.");

        if (Keys(root["HexLayouts"]).Any())
            result.Skipped.Add("Формы панелей из шестигранников — в Light Space панель светится как одно устройство на своём месте сцены.");

        if (Keys(root["ScreenLayouts"]).Any() || Objects(root["Monitors"] as JsonObject).Any())
            result.Skipped.Add("Раскладки лент вокруг мониторов и повороты мониторов — в Light Space экран ложится на устройства по их местам на сцене.");

        if (Objects(root["SceneLayers"]).Any())
            result.Skipped.Add("Слои эффектов сцены — создайте сценарий с содержимым «Сцена».");
    }

    /// <summary>
    /// Добавляет в содержимое настройки эффектов, которых в нём ещё нет.
    /// </summary>
    private static void AddEffects(ContentConfig content, Dictionary<string, EffectValuesConfig> effects)
    {
        foreach (var (id, values) in effects)
        {
            if (!content.Effects.ContainsKey(id))
                content.Effects[id] = new EffectValuesConfig
                {
                    Numbers = new Dictionary<string, double>(values.Numbers),
                    Colors = new Dictionary<string, string>(values.Colors)
                };
        }
    }

    /// <summary>
    /// Настройки эффектов LedHub, которые есть и в галерее Light Space; цвета — в формате #RRGGBB.
    /// </summary>
    private static Dictionary<string, EffectValuesConfig> ReadEffects(JsonObject root)
    {
        var effects = new Dictionary<string, EffectValuesConfig>();

        foreach (var (id, node) in Pairs(root["Effects"]))
        {
            if (EffectLibrary.Find(id) == null || node is not JsonObject values)
                continue;

            var numbers = Pairs(values["Numbers"])
                .Select(p => (p.Key, Value: Number(p.Value)))
                .Where(p => p.Value is { } value && double.IsFinite(value))
                .ToDictionary(p => p.Key, p => p.Value!.Value);

            var colors = Pairs(values["Colors"])
                .Select(p => (p.Key, Color: ColorOf(p.Value)))
                .Where(p => p.Color != null)
                .ToDictionary(p => p.Key, p => p.Color!.Value.ToHex());

            if (numbers.Count > 0 || colors.Count > 0)
                effects[id] = new EffectValuesConfig { Numbers = numbers, Colors = colors };
        }

        return effects;
    }

    /// <summary>
    /// Имена устройств: свои имена из LedHub, а для остальных — имена, под которыми устройства добавляли в группы.
    /// </summary>
    private static Dictionary<string, string> ReadDeviceNames(JsonObject root)
    {
        var names = new Dictionary<string, string>();

        foreach (var member in Objects(root["Groups"]).SelectMany(g => Objects(g["Members"])))
        {
            if (Text(member["Id"]) is { Length: > 0 } id && Text(member["Name"]) is { Length: > 0 } name)
                names[id] = name;
        }

        foreach (var (id, node) in Pairs(root["DeviceNames"]))
        {
            if (Text(node) is { Length: > 0 } name)
                names[id] = name;
        }

        return names;
    }

    private static bool IsSupported(string id)
    {
        return DevicePrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal) && id.Length > p.Length);
    }

    /// <summary>
    /// Адрес, который виден по самому идентификатору: Bluetooth-адрес ленты или COM-порт; у остальных он появится при поиске.
    /// </summary>
    private static string AddressOf(string id)
    {
        if (id.StartsWith("ble:", StringComparison.Ordinal) && ulong.TryParse(id[4..], System.Globalization.NumberStyles.HexNumber, null, out var address))
            return string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (i * 8)) & 0xFF).ToString("X2")));

        return id.StartsWith("serial:", StringComparison.Ordinal) ? id["serial:".Length..] : "";
    }

    /// <summary>
    /// Цвет LedHub: объект с полями R, G, B или строка #RRGGBB; null — не цвет.
    /// </summary>
    private static RgbColor? ColorOf(JsonNode? node)
    {
        if (Text(node) is { } hex)
            return RgbColor.TryParse(hex, out var parsed) ? parsed : null;

        if (node is not JsonObject color || Number(color["R"]) is not { } r || Number(color["G"]) is not { } g || Number(color["B"]) is not { } b)
            return null;

        return new RgbColor(Channel(r), Channel(g), Channel(b));
    }

    private static byte Channel(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    private static string? Text(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static double? Number(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;
    }

    private static bool? Flag(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
    }

    private static IEnumerable<string> Strings(JsonNode? node)
    {
        return (node as JsonArray)?.Select(Text).OfType<string>() ?? Enumerable.Empty<string>();
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        return node switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject map => map.Select(p => p.Value).OfType<JsonObject>(),
            _ => Enumerable.Empty<JsonObject>()
        };
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Pairs(JsonNode? node)
    {
        return node as JsonObject ?? Enumerable.Empty<KeyValuePair<string, JsonNode?>>();
    }

    private static IEnumerable<string> Keys(JsonNode? node)
    {
        return Pairs(node).Select(p => p.Key);
    }
}

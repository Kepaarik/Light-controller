using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Устройство в снимке: куда отправлять его кадры и где находятся его светодиоды.
/// </summary>
public sealed record FrameDevice(string Id, DeviceOutput Output, DeviceGeometry Geometry)
{
    /// <summary>
    /// Монитор, вокруг которого висит устройство: экран оно играет по краям этого монитора, а не по месту на сцене.
    /// null — устройство ни к какому монитору не привязано.
    /// </summary>
    public MonitorBinding? Monitor { get; init; }
}

/// <summary>
/// Содержимое, которое играет в группе: эффект с его настройками.
/// </summary>
public sealed record FrameContent(EffectDefinition Effect, EffectValues Values)
{
    // Экран и музыка помнят в часах сглаженные цвета и звук; когда тип меняется, это начинается заново
    private const string ScreenClockId = "screen";
    private const string MusicClockId = "music";

    // Встроенный эффект играют сами устройства — часам считать нечего
    private const string BuiltinClockId = "builtin";

    /// <summary>
    /// Настройки экрана, если содержимое — экран; иначе null.
    /// </summary>
    public ScreenSettings? Screen { get; init; }

    /// <summary>
    /// Настройки музыки, если содержимое — музыка; иначе null.
    /// </summary>
    public MusicSettings? Music { get; init; }

    /// <summary>
    /// Настройки встроенного эффекта, если содержимое — встроенный эффект; иначе null.
    /// </summary>
    public BuiltinSettings? Builtin { get; init; }

    /// <summary>
    /// Скорость эффекта: во сколько раз быстрее обычного идёт его время.
    /// </summary>
    public double Speed => Effect.Number(Values, "speed");

    /// <summary>
    /// Что считают часы содержимого. Когда это меняется, время начинается заново.
    /// </summary>
    public string ClockId => Screen != null ? ScreenClockId
        : Music != null ? MusicClockId
        : Builtin != null ? BuiltinClockId
        : Effect.Id;

    /// <summary>
    /// Неизменяемая копия сохранённого содержимого.
    /// </summary>
    public static FrameContent From(ContentConfig content)
    {
        var effect = EffectLibrary.FindOrSolid(content.EffectId);

        return new FrameContent(effect, EffectValues.From(content.Effects.GetValueOrDefault(effect.Id)))
        {
            Screen = content.Kind == ContentKind.Screen ? ScreenSettings.From(content.Screen) : null,
            Music = content.Kind == ContentKind.Music ? MusicSettings.From(content.Music) : null,
            Builtin = content.Kind == ContentKind.Builtin ? BuiltinSettings.From(content.Builtin) : null
        };
    }
}

/// <summary>
/// Группа в снимке: что в ней сейчас играет, с какой яркостью и на каких устройствах.
/// </summary>
/// <param name="Active">Содержимое группы.</param>
/// <param name="Brightness">Яркость устройств группы, 0–100 %.</param>
/// <param name="Devices">Подключённые устройства группы, готовые принимать кадры.</param>
public sealed record FrameGroup(string Id, FrameContent Active, int Brightness, IReadOnlyList<FrameDevice> Devices);

/// <summary>
/// Неизменяемый снимок всего, что нужно потоку кадров: какие группы есть, что в каждой играет, с какими настройками
/// и яркостью, на каких устройствах. Окно меняет настройки у себя и отдаёт движку новый снимок целиком.
/// </summary>
public sealed record FrameSnapshot(bool IsLightingEnabled, IReadOnlyList<FrameGroup> Groups)
{
    /// <summary>
    /// Идентификатор группы сцены; группы устройств называются идентификаторами своих устройств.
    /// </summary>
    public const string SceneGroupId = "scene";

    /// <summary>
    /// Снимок текущих настроек и подключённых устройств, готовых принимать кадры.
    /// Вызывается в потоке окна: копирует всё, что потом может поменяться в настройках.
    /// </summary>
    public static FrameSnapshot Build(AppConfig config, IEnumerable<ManagedDevice> devices)
    {
        var ready = new Dictionary<string, FrameDevice>();
        var groups = new List<FrameGroup>();
        var screens = config.Monitors.Count > 0 ? Monitors.List() : Array.Empty<MonitorInfo>();

        foreach (var device in devices)
        {
            if (device.Output is not { Device.IsReady: true } output)
                continue;

            var geometry = DeviceGeometry.For(device.Id, output.Device.Capabilities);

            ready[device.Id] = new FrameDevice(device.Id, output, geometry)
            {
                Monitor = BindingOf(config, device.Id, geometry, screens)
            };
        }

        // Устройства сцены играют её вместо своей подсветки
        var scene = config.Scene;
        var inScene = scene?.Devices.Select(d => d.DeviceId).ToHashSet() ?? new HashSet<string>();

        foreach (var lighting in config.DeviceLighting.Where(l => !inScene.Contains(l.DeviceId)))
        {
            var targets = ready.TryGetValue(lighting.DeviceId, out var target) ? new[] { target } : Array.Empty<FrameDevice>();

            groups.Add(new FrameGroup(lighting.DeviceId, FrameContent.From(lighting.Content), Dimmed(lighting.Brightness, config), targets));
        }

        if (scene is { Devices.Count: > 0 })
            groups.Add(SceneGroup(scene, ready, config));

        return new FrameSnapshot(config.LightingEnabled, groups);
    }

    /// <summary>
    /// Группа сцены: один эффект с общими часами; у каждого устройства — его пространство в сцене.
    /// </summary>
    private static FrameGroup SceneGroup(SceneConfig scene, Dictionary<string, FrameDevice> ready, AppConfig config)
    {
        var devices = new List<FrameDevice>();

        foreach (var placement in scene.Devices)
        {
            if (ready.TryGetValue(placement.DeviceId, out var device))
                devices.Add(device with { Geometry = SceneSpace.Of(device.Geometry, placement, scene.Spread, scene.Direction) });
        }

        return new FrameGroup(SceneGroupId, FrameContent.From(scene.Effect), Dimmed(scene.Brightness, config), devices);
    }

    /// <summary>
    /// Яркость с учётом общей: общая яркость приглушает все устройства разом, не трогая их собственную яркость.
    /// </summary>
    private static int Dimmed(int brightness, AppConfig config)
    {
        return (int)Math.Round(brightness * config.MasterBrightness / 100.0);
    }

    /// <summary>
    /// Подсветка монитора, к которому привязано устройство; null — устройство не привязано.
    /// </summary>
    private static MonitorBinding? BindingOf(AppConfig config, string deviceId, DeviceGeometry geometry, IReadOnlyList<MonitorInfo> screens)
    {
        var monitor = config.Monitors.FirstOrDefault(m => m.DeviceId == deviceId);

        if (monitor == null)
            return null;

        // Отключённый сейчас монитор считаем обычным 16:9 — картинки у него всё равно не будет
        var screen = screens.FirstOrDefault(s => s.Key == monitor.MonitorKey);

        return ScreenEdges.Bind(monitor, geometry.Points.Count, screen?.Width ?? 16, screen?.Height ?? 9);
    }
}

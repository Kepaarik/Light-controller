using LightSpace.Core.Config;
using LightSpace.Engine.Apps;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Scenarios;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Устройство в снимке: куда отправлять его кадры и где находятся его светодиоды.
/// </summary>
/// <param name="ScenePoints">Места светодиодов на сцене в единицах сцены, с теми же номерами, что в Geometry;
/// у устройства одного цвета — одна точка в его середине.</param>
public sealed record FrameDevice(string Id, DeviceOutput Output, DeviceGeometry Geometry, IReadOnlyList<EffectPixel?> ScenePoints);

/// <summary>
/// Содержимое, которое играет в группе: эффект с его настройками.
/// </summary>
/// <param name="ScenarioId">Сценарий, которому принадлежит содержимое; null — «По умолчанию» группы.</param>
/// <param name="Layers">Видимые слои сцены снизу вверх; null — содержимое-эффект.</param>
public sealed record FrameContent(string? ScenarioId, EffectDefinition Effect, EffectValues Values, IReadOnlyList<SceneLayer>? Layers)
{
    // Часы сцены считают время каждого её слоя отдельно, поэтому у всей сцены свой постоянный «эффект» для часов
    private const string SceneClockId = "scene";

    // Экран и музыка помнят в часах сглаженные цвета и звук; когда тип меняется, это начинается заново
    private const string ScreenClockId = "screen";
    private const string MusicClockId = "music";

    // Игра помнит в часах урон, бомбу и сглаженные цвета экрана; другая игра начинает это заново
    private const string GameClockId = "game";

    /// <summary>
    /// Настройки экрана, если содержимое — экран; иначе null.
    /// </summary>
    public ScreenSettings? Screen { get; init; }

    /// <summary>
    /// Настройки музыки, если содержимое — музыка; иначе null.
    /// </summary>
    public MusicSettings? Music { get; init; }

    /// <summary>
    /// Настройки игры, если содержимое — игра; иначе null.
    /// </summary>
    public GameSettings? Game { get; init; }

    /// <summary>
    /// Скорость эффекта: во сколько раз быстрее обычного идёт его время.
    /// </summary>
    public double Speed => Layers == null ? Effect.Number(Values, "speed") : 1;

    /// <summary>
    /// Что считают часы содержимого: эффект или сцену. Когда это меняется, время начинается заново.
    /// </summary>
    public string ClockId => Screen != null ? ScreenClockId
        : Music != null ? MusicClockId
        : Game != null ? $"{GameClockId}:{Game.Game}"
        : Layers == null ? Effect.Id : SceneClockId;

    /// <summary>
    /// Неизменяемая копия сохранённого содержимого.
    /// </summary>
    public static FrameContent From(string? scenarioId, ContentConfig content)
    {
        var effect = EffectLibrary.FindOrSolid(content.EffectId);
        var layers = content.Kind == ContentKind.Scene ? SceneLayer.ListOf(content) : null;

        return new FrameContent(scenarioId, effect, EffectValues.From(content.Effects.GetValueOrDefault(effect.Id)), layers)
        {
            Screen = content.Kind == ContentKind.Screen ? ScreenSettings.From(content.Screen) : null,
            Music = content.Kind == ContentKind.Music ? MusicSettings.From(content.Music) : null,
            Game = content.Kind == ContentKind.Game ? GameSettings.From(content.Game) : null
        };
    }
}

/// <summary>
/// Группа в снимке: что в ней сейчас играет, с какой яркостью и на каких устройствах.
/// </summary>
/// <param name="Active">Содержимое активного сценария группы.</param>
/// <param name="Brightness">Яркость устройств группы, 0–100 %.</param>
/// <param name="Devices">Подключённые устройства группы, готовые принимать кадры.</param>
public sealed record FrameGroup(string Id, FrameContent Active, int Brightness, IReadOnlyList<FrameDevice> Devices);

/// <summary>
/// Неизменяемый снимок всего, что нужно потоку кадров: какие группы есть, что в каждой играет, с какими настройками
/// и яркостью, на каких устройствах и как часто. Окно меняет настройки у себя и отдаёт движку новый снимок целиком.
/// </summary>
/// <param name="TransitionMilliseconds">Длительность плавного перехода, когда в группе сменился сценарий; 0 — сразу.</param>
public sealed record FrameSnapshot(
    bool IsLightingEnabled,
    int FrameRate,
    int TransitionMilliseconds,
    IReadOnlyList<FrameGroup> Groups)
{
    /// <summary>
    /// Снимок текущих настроек и подключённых устройств, готовых принимать кадры; в каждой группе — её активный сценарий
    /// по тому, какие приложения запущены и в фокусе сейчас.
    /// Вызывается в потоке окна: копирует всё, что потом может поменяться в настройках.
    /// </summary>
    public static FrameSnapshot Build(AppConfig config, IEnumerable<ManagedDevice> devices, AppWatcher apps)
    {
        var ready = new Dictionary<string, FrameDevice>();
        var groups = new List<FrameGroup>();

        foreach (var device in devices)
        {
            if (device.Output is not { Device.IsReady: true } output)
                continue;

            var geometry = DeviceGeometry.For(device.Id, output.Device.Capabilities);

            // Устройство, которое ещё не расставили, стоит в середине сцены
            var points = SceneProjection.Project(geometry, device.Settings.Placement ?? new DevicePlacement());

            ready[device.Id] = new FrameDevice(device.Id, output, geometry, points);
        }

        foreach (var group in config.Groups)
        {
            var targets = group.DeviceIds.Where(ready.ContainsKey).Select(id => ready[id]).ToList();

            groups.Add(new FrameGroup(group.Id, ActiveContentOf(config, group, apps), group.Brightness, targets));
        }

        return new FrameSnapshot(config.LightingEnabled, config.FrameRate, config.TransitionMilliseconds, groups);
    }

    /// <summary>
    /// Содержимое активного сценария группы: первого из её списка, у которого выполнено условие, а если таких нет —
    /// «По умолчанию».
    /// </summary>
    private static FrameContent ActiveContentOf(AppConfig config, GroupConfig group, AppWatcher apps)
    {
        var scenario = ScenarioSelector.Choose(config, group, apps).Scenario;

        return scenario == null
            ? FrameContent.From(null, group.DefaultContent)
            : FrameContent.From(scenario.Id, scenario.Content);
    }
}

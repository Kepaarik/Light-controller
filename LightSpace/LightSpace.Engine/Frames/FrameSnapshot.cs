using LightSpace.Core.Config;
using LightSpace.Engine.Apps;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Scenarios;
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

    /// <summary>
    /// Скорость эффекта: во сколько раз быстрее обычного идёт его время.
    /// </summary>
    public double Speed => Layers == null ? Effect.Number(Values, "speed") : 1;

    /// <summary>
    /// Что считают часы содержимого: эффект или сцену. Когда это меняется, время начинается заново.
    /// </summary>
    public string ClockId => Layers == null ? Effect.Id : SceneClockId;

    /// <summary>
    /// Неизменяемая копия сохранённого содержимого.
    /// </summary>
    public static FrameContent From(string? scenarioId, ContentConfig content)
    {
        var effect = EffectLibrary.FindOrSolid(content.EffectId);
        var layers = content.Kind == ContentKind.Scene ? SceneLayer.ListOf(content) : null;

        return new FrameContent(scenarioId, effect, EffectValues.From(content.Effects.GetValueOrDefault(effect.Id)), layers);
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

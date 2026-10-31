using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Устройство в снимке: куда отправлять его кадры и где находятся его светодиоды.
/// </summary>
public sealed record FrameDevice(string Id, DeviceOutput Output, DeviceGeometry Geometry);

/// <summary>
/// Неизменяемый снимок всего, что нужно потоку кадров: что играет, с какими настройками и яркостью, на каких устройствах
/// и как часто. Окно меняет настройки у себя и отдаёт движку новый снимок целиком.
/// </summary>
public sealed record FrameSnapshot(
    bool IsLightingEnabled,
    int FrameRate,
    EffectDefinition Effect,
    EffectValues Values,
    int Brightness,
    IReadOnlyList<FrameDevice> Devices)
{
    /// <summary>
    /// Скорость эффекта: во сколько раз быстрее обычного идёт его время.
    /// </summary>
    public double Speed => Effect.Number(Values, "speed");

    /// <summary>
    /// Снимок текущих настроек и подключённых устройств, готовых принимать кадры.
    /// Вызывается в потоке окна: копирует всё, что потом может поменяться в настройках.
    /// </summary>
    public static FrameSnapshot Build(AppConfig config, IEnumerable<ManagedDevice> devices)
    {
        var content = config.DefaultContent;
        var effect = EffectLibrary.FindOrSolid(content.EffectId);
        var values = EffectValues.From(content.Effects.GetValueOrDefault(effect.Id));
        var targets = new List<FrameDevice>();

        foreach (var device in devices)
        {
            if (device.Output is not { Device.IsReady: true } output)
                continue;

            targets.Add(new FrameDevice(device.Id, output, DeviceGeometry.For(device.Id, output.Device.Capabilities)));
        }

        return new FrameSnapshot(config.LightingEnabled, config.FrameRate, effect, values, config.Brightness, targets);
    }
}

using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine.Output;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Неизменяемые настройки встроенного эффекта: номер эффекта по идентификатору устройства, скорость 0–100 % и цвет;
/// без цвета эффекты играют своими цветами.
/// </summary>
public sealed record BuiltinSettings(IReadOnlyDictionary<string, int> Effects, int Speed, RgbColor? Color)
{
    public static BuiltinSettings From(BuiltinConfig builtin)
    {
        var color = builtin.UseColor && RgbColor.TryParse(builtin.Color, out var parsed) ? parsed : (RgbColor?)null;

        return new BuiltinSettings(new Dictionary<string, int>(builtin.Effects), builtin.Speed, color);
    }

    /// <summary>
    /// Запуск эффекта на устройстве с яркостью brightness: выбранного для него эффекта, а если такого у устройства нет —
    /// первого из его эффектов. null — у устройства нет встроенных эффектов.
    /// </summary>
    public BuiltinEffectStart? StartFor(string deviceId, LedDevice device, int brightness)
    {
        if (device is not IBuiltinEffectsDevice { BuiltinEffects.Count: > 0 } builtin)
            return null;

        var effects = builtin.BuiltinEffects;
        var id = Effects.TryGetValue(deviceId, out var chosen) && effects.Any(e => e.Id == chosen) ? chosen : effects[0].Id;

        return new BuiltinEffectStart(id, Speed, brightness, Color);
    }
}

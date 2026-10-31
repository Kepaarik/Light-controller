using LightSpace.Core;

namespace LightSpace.Engine.Output;

/// <summary>
/// Запуск встроенного эффекта на одном устройстве: номер эффекта у устройства, скорость и яркость 0–100 % и цвет;
/// без цвета эффект играет своими цветами.
/// </summary>
public sealed record BuiltinEffectStart(int EffectId, int Speed, int Brightness, RgbColor? Color);

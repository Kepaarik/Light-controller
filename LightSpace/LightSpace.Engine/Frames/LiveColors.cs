using LightSpace.Core;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Снимок цветов, которые поток кадров только что отправил на устройства, — для живой сцены в окне.
/// </summary>
/// <param name="IsLightingEnabled">Подсветка включена; выключенные устройства не светятся, и цветов у них нет.</param>
/// <param name="Devices">Цвета устройств по их идентификаторам.</param>
public sealed record LiveColors(bool IsLightingEnabled, IReadOnlyDictionary<string, LiveDeviceColors> Devices);

/// <summary>
/// Цвета, которые только что ушли на одно устройство.
/// </summary>
/// <param name="Colors">Цвета светодиодов до яркости; у устройства одного цвета — один цвет.</param>
/// <param name="Brightness">Яркость группы устройства, с которой цвета ушли на него, 0–100 %.</param>
public sealed record LiveDeviceColors(IReadOnlyList<RgbColor> Colors, int Brightness);

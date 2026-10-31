using LightSpace.Core;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Снимок цветов, которые поток кадров только что отправил на устройства, — для живой сцены в окне.
/// </summary>
/// <param name="IsLightingEnabled">Подсветка включена; выключенные устройства не светятся, и цветов у них нет.</param>
/// <param name="Brightness">Яркость, с которой цвета ушли на устройства, 0–100 %.</param>
/// <param name="Devices">Цвета светодиодов по идентификаторам устройств, до яркости; у устройства одного цвета — один цвет.</param>
public sealed record LiveColors(bool IsLightingEnabled, int Brightness, IReadOnlyDictionary<string, IReadOnlyList<RgbColor>> Devices);

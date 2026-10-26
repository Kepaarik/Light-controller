using LedHub.Core;

namespace LedHub.Services;

/// <summary>
/// Источник кадров для лент: программный эффект, экран, музыка.
/// </summary>
public interface ILightSource
{
    /// <summary>
    /// Готовит кадр; вызывается один раз за кадр перед отрисовкой для всех лент.
    /// </summary>
    void BeginFrame(double phase, EffectSettings settings);

    /// <summary>
    /// Один цвет для обычной ленты.
    /// </summary>
    RgbColor RenderSolid();

    /// <summary>
    /// Цвета всех светодиодов адресной ленты по порядку; лента передаётся, если источнику важна её раскладка.
    /// </summary>
    IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount);
}

namespace LedHub.Core;

/// <summary>
/// Адресная лента, у которой можно задать цвет каждого светодиода отдельно.
/// </summary>
public interface IAddressableLedDevice
{
    /// <summary>
    /// Число светодиодов; 0, если лента не сообщила его.
    /// </summary>
    int LedCount { get; }

    /// <summary>
    /// Устанавливает цвета всех светодиодов по порядку с заданной яркостью (0–100 %).
    /// </summary>
    Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default);
}

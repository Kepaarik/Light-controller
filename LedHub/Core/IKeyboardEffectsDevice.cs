namespace LedHub.Core;

/// <summary>
/// Клавиатура с эффектами подсветки, зашитыми в её прошивку: они идут плавно и работают даже при закрытом приложении.
/// </summary>
public interface IKeyboardEffectsDevice
{
    IReadOnlyList<HardwareEffect> KeyboardEffects { get; }

    /// <summary>
    /// Запускает эффект прошивки с яркостью и скоростью 0–100 %; без цвета эффект переливается всеми цветами.
    /// </summary>
    Task StartKeyboardEffectAsync(int effectId, int brightness, int speed, RgbColor? color, CancellationToken ct = default);
}

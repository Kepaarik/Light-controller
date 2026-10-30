namespace LightSpace.Devices;

/// <summary>
/// Возможности устройства: число адресных светодиодов (0 — вся лента светится одним цветом),
/// самый короткий промежуток между кадрами и клавиши с подсветкой, если это клавиатура.
/// </summary>
public sealed record DeviceCapabilities(int LedCount, TimeSpan MinFrameInterval, IReadOnlyList<KeyboardKey> Keys)
{
    /// <summary>
    /// Каждому светодиоду можно задать свой цвет.
    /// </summary>
    public bool IsAddressable => LedCount > 0;

    /// <summary>
    /// Устройство — клавиатура с раскладкой клавиш.
    /// </summary>
    public bool IsKeyboard => Keys.Count > 0;

    /// <summary>
    /// Сколько кадров в секунду устройство принимает самое большее.
    /// </summary>
    public int MaxFramesPerSecond => (int)Math.Round(1 / Math.Max(MinFrameInterval.TotalSeconds, 0.001));
}

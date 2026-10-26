namespace LedHub.Core;

/// <summary>
/// Лента со встроенным микрофоном: контроллер сам зажигает светодиоды в такт звуку рядом с ним.
/// </summary>
public interface IMicrophoneDevice
{
    /// <summary>
    /// Музыкальные режимы, в которых контроллер показывает звук с микрофона.
    /// </summary>
    IReadOnlyList<HardwareEffect> MicrophoneModes { get; }

    Task SetMicrophoneEnabledAsync(bool isEnabled, CancellationToken ct = default);

    Task SetMicrophoneModeAsync(int modeId, CancellationToken ct = default);

    /// <summary>
    /// Чувствительность микрофона, 0–100 %.
    /// </summary>
    Task SetMicrophoneSensitivityAsync(int sensitivity, CancellationToken ct = default);
}

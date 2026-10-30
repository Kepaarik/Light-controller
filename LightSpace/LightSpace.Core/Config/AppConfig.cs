namespace LightSpace.Core.Config;

/// <summary>
/// Пользовательские настройки Light Space, сохраняемые между запусками.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Версия формата настроек, которую понимает эта сборка приложения.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// Частоты кадров, из которых можно выбрать.
    /// </summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 20, 30, 60 };

    /// <summary>
    /// Версия формата, в котором записан файл настроек.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Подсветка всех устройств включена.
    /// </summary>
    public bool LightingEnabled { get; set; } = true;

    /// <summary>
    /// Сколько кадров в секунду уходит на устройства: 20, 30 или 60.
    /// </summary>
    public int FrameRate { get; set; } = 30;

    /// <summary>
    /// Длительность плавного перехода между сценариями, в миллисекундах.
    /// </summary>
    public int TransitionMilliseconds { get; set; } = 400;

    /// <summary>
    /// Через сколько секунд после потери фокуса снимается фокусный сценарий.
    /// </summary>
    public double FocusReleaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Запускать приложение вместе с Windows.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Закрытие окна прячет его в трей, а не завершает приложение.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Устройства, которые уже находились хотя бы раз, в порядке их появления.
    /// </summary>
    public List<DeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (!FrameRates.Contains(FrameRate))
            FrameRate = 30;

        TransitionMilliseconds = Math.Clamp(TransitionMilliseconds, 0, 5000);
        FocusReleaseDelaySeconds = Math.Clamp(FocusReleaseDelaySeconds, 0, 30);

        // Устройство без идентификатора не узнать при поиске, а повторы одного устройства путали бы его настройки
        Devices = Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var device in Devices)
            device.Brightness = Math.Clamp(device.Brightness, 0, 100);
    }
}

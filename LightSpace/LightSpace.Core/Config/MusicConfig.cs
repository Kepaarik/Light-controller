namespace LightSpace.Core.Config;

/// <summary>
/// Откуда берётся звук для содержимого-музыки.
/// </summary>
public enum AudioInputKind
{
    /// <summary>Звук, который играет на компьютере: колонки, наушники.</summary>
    Output,

    /// <summary>Микрофон или другой вход записи.</summary>
    Microphone
}

/// <summary>
/// Как свет следует за музыкой.
/// </summary>
public enum MusicMode
{
    /// <summary>Полосы спектра: басы, средние и высокие частоты разложены по устройству.</summary>
    Spectrum,

    /// <summary>Вспышка на каждом ударе, которая затем гаснет.</summary>
    Rhythm,

    /// <summary>Яркость следует за громкостью; на адресном устройстве — шкала от середины к краям.</summary>
    Pulse
}

/// <summary>
/// Какими цветами светится музыка.
/// </summary>
public enum MusicColors
{
    /// <summary>Цвета радуги: в спектре — по частотам, в ритме — новый тон на каждом ударе.</summary>
    Rainbow,

    /// <summary>Один цвет, который выбрал пользователь.</summary>
    Single
}

/// <summary>
/// Настройки содержимого-музыки: откуда звук, как свет за ним следует и какими цветами.
/// </summary>
public sealed class MusicConfig
{
    /// <summary>
    /// Цвет по умолчанию для режима одного цвета.
    /// </summary>
    public const string DefaultColor = "#7C6CFF";

    /// <summary>
    /// Откуда берётся звук: системный звук или микрофон.
    /// </summary>
    public AudioInputKind Input { get; set; } = AudioInputKind.Output;

    /// <summary>
    /// Системный идентификатор устройства звука; пусто — устройство Windows по умолчанию.
    /// Если такого устройства сейчас нет, звук берётся с устройства по умолчанию.
    /// </summary>
    public string DeviceId { get; set; } = "";

    public MusicMode Mode { get; set; } = MusicMode.Spectrum;

    public MusicColors Colors { get; set; } = MusicColors.Rainbow;

    /// <summary>
    /// Цвет в формате #RRGGBB, когда музыка светится одним цветом.
    /// </summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>
    /// Чувствительность, 25–400 %: больше — ярче реагирует на тихую музыку.
    /// </summary>
    public int Sensitivity { get; set; } = 100;

    /// <summary>
    /// Сглаживание, 0–95 %: сколько света остаётся за 1/20 секунды после громкого места. Больше — плавнее гаснет.
    /// </summary>
    public int Smoothing { get; set; } = 85;

    /// <summary>
    /// Независимая копия настроек: правки копии не меняют оригинал.
    /// </summary>
    public MusicConfig Clone()
    {
        return new MusicConfig
        {
            Input = Input,
            DeviceId = DeviceId,
            Mode = Mode,
            Colors = Colors,
            Color = Color,
            Sensitivity = Sensitivity,
            Smoothing = Smoothing
        };
    }

    /// <summary>
    /// Приводит настройки к допустимым: файл могли поправить руками или записать в другой версии.
    /// </summary>
    public void Normalize()
    {
        DeviceId ??= "";

        if (!Enum.IsDefined(Input))
            Input = AudioInputKind.Output;

        if (!Enum.IsDefined(Mode))
            Mode = MusicMode.Spectrum;

        if (!Enum.IsDefined(Colors))
            Colors = MusicColors.Rainbow;

        // Испорченный цвет не прочитается — вместо него цвет по умолчанию
        if (!RgbColor.TryParse(Color, out _))
            Color = DefaultColor;

        Sensitivity = Math.Clamp(Sensitivity, 25, 400);
        Smoothing = Math.Clamp(Smoothing, 0, 95);
    }
}

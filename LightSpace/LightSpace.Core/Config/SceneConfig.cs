namespace LightSpace.Core.Config;

/// <summary>
/// Как эффект сцены ложится на её устройства.
/// </summary>
public enum SceneSpread
{
    /// <summary>Один эффект на всём холсте: он переходит с устройства на устройство по их местам.</summary>
    Space,

    /// <summary>У каждого устройства эффект целиком, но с общими временем и настройками.</summary>
    Synced
}

/// <summary>
/// Куда идёт эффект сцены.
/// </summary>
public enum SceneDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop,
    FromCenter
}

/// <summary>
/// Как устройство выглядит на холсте сцены. От вида зависит, где на холсте стоят его светодиоды, а значит, какие
/// цвета эффекта оно получает.
/// </summary>
public enum SceneDeviceShape
{
    /// <summary>По самому устройству: клавиатура — клавиатурой, остальное — лентой.</summary>
    Auto,

    /// <summary>Полоса: светодиоды идут вдоль линии.</summary>
    Strip,

    /// <summary>Клавиатура: клавиши рядами; у настоящей клавиатуры — её раскладка.</summary>
    Keyboard,

    /// <summary>Монитор: лента по краям экрана; у ленты за монитором — её раскладка со страницы «Мониторы».</summary>
    Monitor,

    /// <summary>Шестигранники, собранные сотами: светодиоды поровну делятся между ними.</summary>
    Hexagons
}

/// <summary>
/// Сцена профиля: один эффект, который играют сразу несколько устройств — по пространству холста или синхронно.
/// Устройства сцены играют её вместо своей подсветки.
/// </summary>
public sealed class SceneConfig
{
    /// <summary>
    /// Высота холста сцены; ширина холста — 1.
    /// </summary>
    public const double CanvasHeight = 0.6;

    public SceneSpread Spread { get; set; } = SceneSpread.Space;

    public SceneDirection Direction { get; set; } = SceneDirection.LeftToRight;

    /// <summary>
    /// Яркость всех устройств сцены, 0–100 %.
    /// </summary>
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Эффект сцены и его настройки; другие типы содержимого сцена не играет.
    /// </summary>
    public ContentConfig Effect { get; set; } = new();

    /// <summary>
    /// Устройства сцены и их места на холсте.
    /// </summary>
    public List<SceneDeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Независимая копия: правки копии не меняют оригинал.
    /// </summary>
    public SceneConfig Clone()
    {
        return new SceneConfig
        {
            Spread = Spread,
            Direction = Direction,
            Brightness = Brightness,
            Effect = Effect.Clone(),
            Devices = Devices.Select(d => d.Clone()).ToList()
        };
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Spread))
            Spread = SceneSpread.Space;

        if (!Enum.IsDefined(Direction))
            Direction = SceneDirection.LeftToRight;

        Brightness = Math.Clamp(Brightness, 0, 100);

        Effect ??= new ContentConfig();
        Effect.Normalize();
        Effect.Kind = ContentKind.Effect;

        // Устройство входит в сцену один раз
        Devices = (Devices ?? new List<SceneDeviceConfig>())
            .Where(d => d != null && !string.IsNullOrWhiteSpace(d.DeviceId))
            .GroupBy(d => d.DeviceId)
            .Select(g => g.First())
            .ToList();

        foreach (var device in Devices)
            device.Normalize();
    }
}

/// <summary>
/// Устройство сцены: его место и вид на холсте, число шестигранников и своя яркость.
/// </summary>
public sealed class SceneDeviceConfig
{
    public const double MinSize = 0.05;
    public const double MaxSize = 1;

    public const int MinPanels = 1;
    public const int MaxPanels = 30;
    public const int DefaultPanels = 7;

    public string DeviceId { get; set; } = "";

    /// <summary>
    /// Середина устройства по горизонтали: 0 — левый край холста, 1 — правый.
    /// </summary>
    public double X { get; set; } = 0.5;

    /// <summary>
    /// Середина устройства по вертикали: 0 — верхний край холста, 1 — нижний.
    /// </summary>
    public double Y { get; set; } = 0.5;

    /// <summary>
    /// Длина ленты или ширина клавиатуры, монитора, сот шестигранников в долях ширины холста.
    /// </summary>
    public double Size { get; set; } = 0.4;

    /// <summary>
    /// Поворот устройства по часовой стрелке в градусах, 0–360.
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// Как устройство выглядит на холсте; по умолчанию — по самому устройству.
    /// </summary>
    public SceneDeviceShape Shape { get; set; } = SceneDeviceShape.Auto;

    /// <summary>
    /// Сколько шестигранников у устройства с видом «Шестигранники».
    /// </summary>
    public int Panels { get; set; } = DefaultPanels;

    /// <summary>
    /// Яркость устройства в сцене, 0–100 %: умножается на яркость сцены.
    /// </summary>
    public int Brightness { get; set; } = 100;

    public SceneDeviceConfig Clone()
    {
        return new SceneDeviceConfig
        {
            DeviceId = DeviceId,
            X = X,
            Y = Y,
            Size = Size,
            Angle = Angle,
            Shape = Shape,
            Panels = Panels,
            Brightness = Brightness
        };
    }

    public void Normalize()
    {
        X = double.IsFinite(X) ? Math.Clamp(X, 0, 1) : 0.5;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : 0.5;
        Size = double.IsFinite(Size) ? Math.Clamp(Size, MinSize, MaxSize) : 0.4;
        Angle = double.IsFinite(Angle) ? (Angle % 360 + 360) % 360 : 0;

        if (!Enum.IsDefined(Shape))
            Shape = SceneDeviceShape.Auto;

        Panels = Math.Clamp(Panels, MinPanels, MaxPanels);
        Brightness = Math.Clamp(Brightness, 0, 100);
    }
}

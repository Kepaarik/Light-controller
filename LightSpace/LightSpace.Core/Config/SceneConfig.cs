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
/// Устройство сцены и его место на холсте.
/// </summary>
public sealed class SceneDeviceConfig
{
    public const double MinSize = 0.05;
    public const double MaxSize = 1;

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
    /// Длина ленты или ширина клавиатуры в долях ширины холста.
    /// </summary>
    public double Size { get; set; } = 0.4;

    /// <summary>
    /// Поворот устройства по часовой стрелке в градусах, 0–360.
    /// </summary>
    public double Angle { get; set; }

    public SceneDeviceConfig Clone()
    {
        return new SceneDeviceConfig { DeviceId = DeviceId, X = X, Y = Y, Size = Size, Angle = Angle };
    }

    public void Normalize()
    {
        X = double.IsFinite(X) ? Math.Clamp(X, 0, 1) : 0.5;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : 0.5;
        Size = double.IsFinite(Size) ? Math.Clamp(Size, MinSize, MaxSize) : 0.4;
        Angle = double.IsFinite(Angle) ? (Angle % 360 + 360) % 360 : 0;
    }
}

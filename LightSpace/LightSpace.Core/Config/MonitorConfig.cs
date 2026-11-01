namespace LightSpace.Core.Config;

/// <summary>
/// Насколько монитор физически повёрнут относительно того, как Windows выводит на него картинку.
/// </summary>
public enum MonitorRotation
{
    /// <summary>
    /// Как в Windows — ориентация в системе совпадает с реальной.
    /// </summary>
    None,

    /// <summary>
    /// Монитор повёрнут на 90° по часовой стрелке, а Windows об этом не знает.
    /// </summary>
    Clockwise90,

    /// <summary>
    /// Монитор стоит вверх ногами.
    /// </summary>
    Upside180,

    /// <summary>
    /// Монитор повёрнут на 90° против часовой стрелки, а Windows об этом не знает.
    /// </summary>
    CounterClockwise270
}

/// <summary>
/// Угол монитора, если смотреть на экран спереди.
/// </summary>
public enum ScreenCorner
{
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft
}

/// <summary>
/// Подсветка монитора: какая лента висит вокруг него, как он повёрнут и как проложена лента.
/// Когда группа ленты играет экран, лента берёт цвета краёв именно этого монитора.
/// </summary>
public sealed class MonitorConfig
{
    /// <summary>
    /// Системное имя монитора (например, \\.\DISPLAY1).
    /// </summary>
    public string MonitorKey { get; set; } = "";

    /// <summary>
    /// Лента вокруг монитора; null — монитор без подсветки.
    /// </summary>
    public string? DeviceId { get; set; }

    public MonitorRotation Rotation { get; set; }

    /// <summary>
    /// Как адресная лента проложена по краям монитора.
    /// </summary>
    public ScreenLayoutConfig Layout { get; set; } = new();

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        MonitorKey ??= "";
        Layout ??= new ScreenLayoutConfig();
        Layout.Normalize();

        if (string.IsNullOrWhiteSpace(DeviceId))
            DeviceId = null;

        // Поворот, которого нет в этой сборке, мог попасть в файл из правки руками
        if (!Enum.IsDefined(Rotation))
            Rotation = MonitorRotation.None;
    }
}

/// <summary>
/// Как адресная лента проложена вокруг монитора: какие стороны заняты, сколько на них светодиодов, где у ленты
/// первый светодиод и куда она идёт.
/// </summary>
public sealed class ScreenLayoutConfig
{
    public bool UseLeft { get; set; } = true;

    public bool UseTop { get; set; } = true;

    public bool UseRight { get; set; } = true;

    public bool UseBottom { get; set; }

    /// <summary>
    /// Светодиодов на левой стороне; 0 — посчитать автоматически по длине стороны.
    /// </summary>
    public int LeftCount { get; set; }

    public int TopCount { get; set; }

    public int RightCount { get; set; }

    public int BottomCount { get; set; }

    /// <summary>
    /// Угол, где у ленты первый светодиод.
    /// </summary>
    public ScreenCorner StartCorner { get; set; } = ScreenCorner.BottomRight;

    /// <summary>
    /// Лента идёт от первого светодиода по часовой стрелке.
    /// </summary>
    public bool IsClockwise { get; set; }

    /// <summary>
    /// Сдвиг картинки вдоль ленты в светодиодах — если первый светодиод стоит не точно в углу.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Раскладка описана так, как её видно сзади монитора (при монтаже): левая и правая стороны меняются местами.
    /// </summary>
    public bool IsViewedFromBack { get; set; }

    /// <summary>
    /// Независимая копия раскладки: правки копии не меняют оригинал.
    /// </summary>
    public ScreenLayoutConfig Clone()
    {
        return (ScreenLayoutConfig)MemberwiseClone();
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        LeftCount = Math.Max(0, LeftCount);
        TopCount = Math.Max(0, TopCount);
        RightCount = Math.Max(0, RightCount);
        BottomCount = Math.Max(0, BottomCount);
        Offset = Math.Max(0, Offset);

        if (!Enum.IsDefined(StartCorner))
            StartCorner = ScreenCorner.BottomRight;
    }
}

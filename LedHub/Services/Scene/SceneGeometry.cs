namespace LedHub.Services.Scene;

public enum SceneItemKind
{
    Monitor,
    Strip,
    Panel
}

/// <summary>
/// Элемент на сцене — монитор или устройство — и его место на стене. Единицы сцены — примерно сантиметры.
/// </summary>
public sealed class SceneItemConfig
{
    public const string MonitorPrefix = "monitor:";

    /// <summary>
    /// Идентификатор устройства или «monitor:» и системное имя монитора.
    /// </summary>
    public string Key { get; set; } = "";

    public SceneItemKind Kind { get; set; }

    /// <summary>
    /// Центр элемента на сцене.
    /// </summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Длина ленты или ширина монитора.
    /// </summary>
    public double Size { get; set; } = 50;

    /// <summary>
    /// Поворот в градусах по часовой стрелке; у ленты 0 — слева направо.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Лента идёт в обратную сторону: первый светодиод на другом конце.
    /// </summary>
    public bool IsReversed { get; set; }

    /// <summary>
    /// Радиус одного шестигранника панели.
    /// </summary>
    public double HexSize { get; set; } = 9;

    /// <summary>
    /// Сколько шестигранников рисовать у панели, форма которой не собрана в редакторе.
    /// </summary>
    public int HexCount { get; set; } = 7;

    /// <summary>
    /// Своя форма панели, собранная на сцене; пусто — соты из HexCount шестигранников.
    /// </summary>
    public List<SceneHexCell> Cells { get; set; } = new();
}

/// <summary>
/// Шестигранник панели в осевых координатах сетки.
/// </summary>
public sealed class SceneHexCell
{
    public int Q { get; set; }

    public int R { get; set; }
}

public readonly record struct ScenePoint(double X, double Y);

/// <summary>
/// Устройство для расчёта формы: сколько у него светодиодов (0 — обычная лента одного цвета) и его форма из шестигранников.
/// </summary>
public sealed record SceneDeviceInfo(string Id, int LedCount, HexLayoutConfig? HexLayout);

/// <summary>
/// Монитор для расчёта формы: пропорции, лента подсветки и её раскладка вокруг экрана.
/// </summary>
public sealed record SceneMonitorInfo(MonitorInfo Monitor, double Aspect, SceneDeviceInfo? Backlight, ScreenLayoutConfig? Layout);

/// <summary>
/// Форма элемента на сцене в координатах сцены: что рисовать и где стоит каждый светодиод.
/// </summary>
public sealed class SceneShape
{
    public SceneShape(SceneItemConfig config)
    {
        Config = config;
    }

    public SceneItemConfig Config { get; }

    /// <summary>
    /// Чьи светодиоды стоят на этом элементе; у монитора — его лента подсветки.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Экран монитора: левый верхний угол и размер.
    /// </summary>
    public (ScenePoint TopLeft, double Width, double Height)? Screen { get; init; }

    /// <summary>
    /// Концы ленты: от первого светодиода к последнему.
    /// </summary>
    public (ScenePoint Start, ScenePoint End)? Line { get; init; }

    /// <summary>
    /// Центры шестигранников панели.
    /// </summary>
    public IReadOnlyList<ScenePoint> Hexes { get; init; } = Array.Empty<ScenePoint>();

    public double HexRadius { get; init; }

    /// <summary>
    /// Поворот шестигранников в градусах.
    /// </summary>
    public double HexRotation { get; init; }

    /// <summary>
    /// Места светодиодов адресной ленты по порядку; null — светодиод никуда не попал. Пусто у обычной ленты.
    /// </summary>
    public IReadOnlyList<ScenePoint?> Leds { get; init; } = Array.Empty<ScenePoint?>();

    /// <summary>
    /// Точка, по которой берётся цвет для обычной ленты, — середина элемента.
    /// </summary>
    public ScenePoint Center { get; init; }

    public (ScenePoint Min, ScenePoint Max) Bounds { get; init; }
}

/// <summary>
/// Считает формы элементов сцены и места светодиодов на них.
/// </summary>
public static class SceneGeometry
{
    // Светодиоды подсветки рисуются чуть снаружи экрана
    private const double BacklightGap = 2.5;

    private const double LedRingRatio = 0.55;

    private static readonly double Sqrt3 = Math.Sqrt(3);
    private static readonly (int Q, int R)[] HexDirections = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

    public static SceneShape BuildMonitor(SceneItemConfig config, SceneMonitorInfo monitor)
    {
        var width = config.Size;
        var height = config.Size / monitor.Aspect;
        var left = config.X - width / 2;
        var top = config.Y - height / 2;
        var leds = Array.Empty<ScenePoint?>();

        if (monitor.Backlight is { LedCount: > 0 } strip && monitor.Layout != null)
        {
            var points = ScreenLayoutMath.Build(monitor.Layout, strip.LedCount, monitor.Aspect);
            leds = new ScenePoint?[points.Length];

            for (var i = 0; i < points.Length; i++)
            {
                if (points[i] is not { } point)
                    continue;

                // Раскладку могли описать со спины монитора — на сцене мы смотрим на него спереди
                var isBack = monitor.Layout.IsViewedFromBack;
                var u = isBack ? 1 - point.X : point.X;
                var side = isBack ? MirrorSide(point.Side) : point.Side;

                var x = left + u * width;
                var y = top + point.Y * height;

                leds[i] = side switch
                {
                    ScreenSide.Left => new ScenePoint(left - BacklightGap, y),
                    ScreenSide.Right => new ScenePoint(left + width + BacklightGap, y),
                    ScreenSide.Top => new ScenePoint(x, top - BacklightGap),
                    _ => new ScenePoint(x, top + height + BacklightGap)
                };
            }
        }

        return new SceneShape(config)
        {
            DeviceId = monitor.Backlight?.Id,
            Screen = (new ScenePoint(left, top), width, height),
            Leds = leds,
            Center = new ScenePoint(config.X, config.Y),
            Bounds = (new ScenePoint(left - BacklightGap, top - BacklightGap),
                new ScenePoint(left + width + BacklightGap, top + height + BacklightGap))
        };
    }

    public static SceneShape BuildStrip(SceneItemConfig config, SceneDeviceInfo device)
    {
        var angle = config.Rotation * Math.PI / 180;
        var dx = Math.Cos(angle) * config.Size / 2;
        var dy = Math.Sin(angle) * config.Size / 2;

        var first = new ScenePoint(config.X - dx, config.Y - dy);
        var last = new ScenePoint(config.X + dx, config.Y + dy);

        if (config.IsReversed)
            (first, last) = (last, first);

        var leds = new ScenePoint?[device.LedCount];
        for (var i = 0; i < leds.Length; i++)
        {
            var t = (i + 0.5) / leds.Length;
            leds[i] = new ScenePoint(first.X + (last.X - first.X) * t, first.Y + (last.Y - first.Y) * t);
        }

        return new SceneShape(config)
        {
            DeviceId = device.Id,
            Line = (first, last),
            Leds = leds,
            Center = new ScenePoint(config.X, config.Y),
            Bounds = (new ScenePoint(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y)),
                new ScenePoint(Math.Max(first.X, last.X), Math.Max(first.Y, last.Y)))
        };
    }

    public static SceneShape BuildPanel(SceneItemConfig config, SceneDeviceInfo device)
    {
        var size = config.HexSize;
        var angle = config.Rotation * Math.PI / 180;

        // Собранная в редакторе форма адресной ленты повторяет и порядок светодиодов; иначе — форма со сцены или соты нужного размера
        var hasLayout = device.LedCount > 0 && device.HexLayout is { Cells.Count: > 0 };
        var cells = hasLayout
            ? device.HexLayout!.Cells.Select(c => (c.Q, c.R)).ToList()
            : PanelCells(config).ToList();

        var local = cells.Select(c => (X: size * Sqrt3 * (c.Item1 + c.Item2 / 2.0), Y: size * 1.5 * c.Item2)).ToList();
        var centerX = (local.Min(p => p.X) + local.Max(p => p.X)) / 2;
        var centerY = (local.Min(p => p.Y) + local.Max(p => p.Y)) / 2;

        var hexes = local
            .Select(p => Place(config, p.X - centerX, p.Y - centerY, angle))
            .ToList();

        var leds = new ScenePoint?[device.LedCount];

        if (hasLayout)
        {
            for (var i = 0; i < leds.Length && i / HexCellConfig.LedCount < local.Count; i++)
            {
                var cell = local[i / HexCellConfig.LedCount];
                var (ox, oy) = LedOffset(i % HexCellConfig.LedCount, size);
                leds[i] = Place(config, cell.X - centerX + ox, cell.Y - centerY + oy, angle);
            }
        }

        return new SceneShape(config)
        {
            DeviceId = device.Id,
            Hexes = hexes,
            HexRadius = size,
            HexRotation = config.Rotation,
            Leds = hasLayout ? leds : Array.Empty<ScenePoint?>(),
            Center = new ScenePoint(config.X, config.Y),
            Bounds = (new ScenePoint(hexes.Min(h => h.X) - size, hexes.Min(h => h.Y) - size),
                new ScenePoint(hexes.Max(h => h.X) + size, hexes.Max(h => h.Y) + size))
        };
    }

    /// <summary>
    /// Шестигранники панели без формы от адресной ленты: собранные на сцене или соты из HexCount штук.
    /// </summary>
    public static IEnumerable<(int Q, int R)> PanelCells(SceneItemConfig config)
    {
        return config.Cells.Count > 0
            ? config.Cells.Select(c => (c.Q, c.R))
            : Spiral(Math.Max(1, config.HexCount));
    }

    /// <summary>
    /// Шестигранники по кругу от центра: сначала центральный, затем кольцо из шести, затем из двенадцати…
    /// </summary>
    public static IEnumerable<(int Q, int R)> Spiral(int count)
    {
        yield return (0, 0);
        var produced = 1;

        for (var radius = 1; produced < count; radius++)
        {
            var q = HexDirections[4].Q * radius;
            var r = HexDirections[4].R * radius;

            for (var side = 0; side < 6; side++)
            {
                for (var step = 0; step < radius; step++)
                {
                    if (produced >= count)
                        yield break;

                    yield return (q, r);
                    produced++;

                    q += HexDirections[side].Q;
                    r += HexDirections[side].R;
                }
            }
        }
    }

    /// <summary>
    /// Смещение светодиода от центра шестигранника: 0 — центр, 1–6 — по часовой стрелке, начиная с верхнего.
    /// </summary>
    private static (double X, double Y) LedOffset(int index, double size)
    {
        if (index == 0)
            return (0, 0);

        var angle = (90 - 60 * (index - 1)) * Math.PI / 180;

        return (Math.Cos(angle) * size * LedRingRatio, -Math.Sin(angle) * size * LedRingRatio);
    }

    /// <summary>
    /// Поворачивает точку вокруг центра элемента и ставит на сцену.
    /// </summary>
    private static ScenePoint Place(SceneItemConfig config, double x, double y, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        return new ScenePoint(config.X + x * cos - y * sin, config.Y + x * sin + y * cos);
    }

    private static ScreenSide MirrorSide(ScreenSide side) => side switch
    {
        ScreenSide.Left => ScreenSide.Right,
        ScreenSide.Right => ScreenSide.Left,
        _ => side
    };
}

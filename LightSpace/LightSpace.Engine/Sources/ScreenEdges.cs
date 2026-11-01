using LightSpace.Core;
using LightSpace.Core.Config;

namespace LightSpace.Engine.Sources;

public enum ScreenSide
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>
/// Место светодиода на краю экрана: X и Y от 0 до 1, сторона — чтобы знать, куда «смотреть» внутрь картинки.
/// </summary>
public readonly record struct LedPoint(double X, double Y, ScreenSide Side);

/// <summary>
/// Неизменяемая копия подсветки монитора для потока кадров: какой монитор, как он повёрнут и как вокруг него
/// проложена лента.
/// </summary>
/// <param name="Layout">Копия раскладки: настройки в окне могут поменяться, а снимок — нет.</param>
/// <param name="Points">Место на краю экрана для каждого светодиода ленты; пусто — у устройства один цвет.</param>
public sealed record MonitorBinding(string MonitorKey, MonitorRotation Rotation, ScreenLayoutConfig Layout, IReadOnlyList<LedPoint?> Points);

/// <summary>
/// Раскладывает светодиоды ленты по краям монитора и берёт для каждого цвет картинки у края.
/// </summary>
public static class ScreenEdges
{
    // Насколько вглубь картинки (в клетках кадра) смотреть от края для светодиода
    private const double EdgeDepth = 3;

    // Углы по часовой стрелке; сторона i лежит между углом i и углом i + 1
    private static readonly (double X, double Y)[] Corners = { (0, 0), (1, 0), (1, 1), (0, 1) };
    private static readonly ScreenSide[] Sides = { ScreenSide.Top, ScreenSide.Right, ScreenSide.Bottom, ScreenSide.Left };

    /// <summary>
    /// Отношение ширины к высоте монитора так, как он стоит на самом деле: повёрнутый на 90° монитор — книжный.
    /// </summary>
    public static double PhysicalAspect(int width, int height, MonitorRotation rotation)
    {
        var aspect = (double)width / Math.Max(1, height);

        return rotation is MonitorRotation.Clockwise90 or MonitorRotation.CounterClockwise270 ? 1 / aspect : aspect;
    }

    /// <summary>
    /// Неизменяемая подсветка монитора для ленты из ledCount светодиодов (0 — устройство одного цвета).
    /// width и height — размер монитора в Windows; если монитора сейчас нет, берётся 16:9.
    /// </summary>
    public static MonitorBinding Bind(MonitorConfig monitor, int ledCount, int width, int height)
    {
        var layout = monitor.Layout.Clone();
        var points = ledCount > 0 ? Build(layout, ledCount, PhysicalAspect(width, height, monitor.Rotation)) : Array.Empty<LedPoint?>();

        return new MonitorBinding(monitor.MonitorKey, monitor.Rotation, layout, points);
    }

    /// <summary>
    /// Место на экране для каждого светодиода ленты по порядку; null — светодиод не попал ни на одну сторону.
    /// aspect — отношение ширины экрана к высоте.
    /// </summary>
    public static LedPoint?[] Build(ScreenLayoutConfig layout, int ledCount, double aspect)
    {
        var path = BuildPath(layout);
        var counts = DistributeCounts(layout, path, ledCount, aspect);
        var points = new List<LedPoint>();

        for (var s = 0; s < path.Count; s++)
        {
            var (side, from, to) = path[s];

            for (var k = 0; k < counts[s]; k++)
            {
                var t = (k + 0.5) / counts[s];
                points.Add(new LedPoint(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, side));
            }
        }

        var result = new LedPoint?[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            var index = ((i - layout.Offset) % ledCount + ledCount) % ledCount;
            result[i] = index < points.Count ? points[index] : null;
        }

        return result;
    }

    /// <summary>
    /// Цвет картинки рядом со светодиодом: точку раскладки переводим в координаты кадра с учётом вида сзади
    /// и поворота монитора, затем смотрим немного внутрь картинки — к её центру.
    /// </summary>
    public static RgbColor Sample(ScreenPicture picture, LedPoint point, MonitorBinding binding)
    {
        var u = binding.Layout.IsViewedFromBack ? 1 - point.X : point.X;
        var v = point.Y;

        var (x, y) = binding.Rotation switch
        {
            MonitorRotation.Clockwise90 => (v, 1 - u),
            MonitorRotation.Upside180 => (1 - u, 1 - v),
            MonitorRotation.CounterClockwise270 => (1 - v, u),
            _ => (u, v)
        };

        // Шаг внутрь картинки в долях кадра: на EdgeDepth клеток к центру
        var toCenterX = (0.5 - x) * picture.Columns;
        var toCenterY = (0.5 - y) * picture.Rows;
        var distance = Math.Sqrt(toCenterX * toCenterX + toCenterY * toCenterY);
        var step = Math.Min(EdgeDepth, distance);

        if (distance > 0)
        {
            x += toCenterX / distance * step / picture.Columns;
            y += toCenterY / distance * step / picture.Rows;
        }

        return picture.Sample(x, y);
    }

    /// <summary>
    /// Средний цвет всей картинки — для ленты одного цвета или адресной ленты без сторон.
    /// </summary>
    public static RgbColor Average(ScreenPicture picture)
    {
        long r = 0, g = 0, b = 0;

        foreach (var cell in picture.Cells)
        {
            r += cell.R;
            g += cell.G;
            b += cell.B;
        }

        var count = Math.Max(1, picture.Cells.Count);

        return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <summary>
    /// Стороны в порядке, в каком по ним идёт лента, начиная с выбранного угла.
    /// </summary>
    private static List<(ScreenSide Side, (double X, double Y) From, (double X, double Y) To)> BuildPath(ScreenLayoutConfig layout)
    {
        var path = new List<(ScreenSide, (double, double), (double, double))>();
        var start = (int)layout.StartCorner;

        for (var step = 0; step < 4; step++)
        {
            int from, to, side;

            if (layout.IsClockwise)
            {
                from = (start + step) % 4;
                to = (from + 1) % 4;
                side = from;
            }
            else
            {
                from = (start - step + 4) % 4;
                to = (from + 3) % 4;
                side = to;
            }

            if (IsUsed(layout, Sides[side]))
                path.Add((Sides[side], Corners[from], Corners[to]));
        }

        return path;
    }

    /// <summary>
    /// Сколько светодиодов на каждой стороне: заданные вручную остаются как есть,
    /// остальные светодиоды делятся между «автоматическими» сторонами пропорционально их длине.
    /// </summary>
    private static int[] DistributeCounts(ScreenLayoutConfig layout,
        List<(ScreenSide Side, (double X, double Y) From, (double X, double Y) To)> path, int ledCount, double aspect)
    {
        var counts = path.Select(p => ManualCount(layout, p.Side)).ToArray();
        var autoSides = Enumerable.Range(0, path.Count).Where(i => counts[i] == 0).ToList();
        var remaining = Math.Max(0, ledCount - counts.Sum());

        if (autoSides.Count == 0 || remaining == 0)
            return counts;

        double Length(ScreenSide side) => side is ScreenSide.Top or ScreenSide.Bottom ? aspect : 1;

        var totalLength = autoSides.Sum(i => Length(path[i].Side));
        var shares = autoSides.Select(i => remaining * Length(path[i].Side) / totalLength).ToList();

        for (var j = 0; j < autoSides.Count; j++)
            counts[autoSides[j]] = (int)Math.Floor(shares[j]);

        // Остаток от округления — сторонам с наибольшими дробными частями
        var leftover = remaining - autoSides.Sum(i => counts[i]);

        foreach (var j in Enumerable.Range(0, autoSides.Count).OrderByDescending(j => shares[j] - Math.Floor(shares[j])).Take(leftover))
            counts[autoSides[j]]++;

        return counts;
    }

    private static bool IsUsed(ScreenLayoutConfig layout, ScreenSide side) => side switch
    {
        ScreenSide.Left => layout.UseLeft,
        ScreenSide.Top => layout.UseTop,
        ScreenSide.Right => layout.UseRight,
        _ => layout.UseBottom
    };

    private static int ManualCount(ScreenLayoutConfig layout, ScreenSide side) => Math.Max(0, side switch
    {
        ScreenSide.Left => layout.LeftCount,
        ScreenSide.Top => layout.TopCount,
        ScreenSide.Right => layout.RightCount,
        _ => layout.BottomCount
    });
}

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LedHub.Services;

public enum ScreenSide
{
    Left,
    Top,
    Right,
    Bottom
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
/// Насколько монитор физически повёрнут относительно того, как Windows выводит на него картинку.
/// </summary>
public enum MonitorRotation
{
    /// <summary>Как в Windows — ориентация в системе совпадает с реальной.</summary>
    None,

    /// <summary>Монитор повёрнут на 90° по часовой стрелке, а Windows об этом не знает.</summary>
    Clockwise90,

    Upside180,

    /// <summary>Монитор повёрнут на 90° против часовой стрелки, а Windows об этом не знает.</summary>
    CounterClockwise270
}

/// <summary>
/// Место светодиода на краю экрана: X и Y от 0 до 1, сторона — чтобы знать, куда «смотреть» внутрь картинки.
/// </summary>
public readonly record struct LedPoint(double X, double Y, ScreenSide Side);

/// <summary>
/// Как адресная лента проложена вокруг монитора. Сохраняется для каждой ленты отдельно.
/// </summary>
public sealed partial class ScreenLayoutConfig : ObservableObject
{
    [ObservableProperty]
    private bool _useLeft = true;

    [ObservableProperty]
    private bool _useTop = true;

    [ObservableProperty]
    private bool _useRight = true;

    [ObservableProperty]
    private bool _useBottom;

    /// <summary>
    /// Светодиодов на левой стороне; 0 — посчитать автоматически по длине стороны.
    /// </summary>
    [ObservableProperty]
    private int _leftCount;

    [ObservableProperty]
    private int _topCount;

    [ObservableProperty]
    private int _rightCount;

    [ObservableProperty]
    private int _bottomCount;

    [ObservableProperty]
    private ScreenCorner _startCorner = ScreenCorner.BottomRight;

    [ObservableProperty]
    private bool _isClockwise;

    /// <summary>
    /// Сдвиг картинки вдоль ленты в светодиодах — если первый светодиод стоит не точно в углу.
    /// </summary>
    [ObservableProperty]
    private int _offset;

    /// <summary>
    /// Раскладка описана так, как её видно сзади монитора (при монтаже): левая и правая стороны меняются местами.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewedFromFront))]
    private bool _isViewedFromBack;

    /// <summary>
    /// Обратная сторона IsViewedFromBack для переключателя ориентации ленты.
    /// </summary>
    [JsonIgnore]
    public bool IsViewedFromFront
    {
        get => !IsViewedFromBack;
        set
        {
            if (value)
                IsViewedFromBack = false;
        }
    }
}

/// <summary>
/// Раскладывает светодиоды ленты по краям экрана.
/// </summary>
public static class ScreenLayoutMath
{
    // Углы по часовой стрелке; сторона i лежит между углом i и углом i + 1
    private static readonly (double X, double Y)[] Corners = { (0, 0), (1, 0), (1, 1), (0, 1) };
    private static readonly ScreenSide[] Sides = { ScreenSide.Top, ScreenSide.Right, ScreenSide.Bottom, ScreenSide.Left };

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

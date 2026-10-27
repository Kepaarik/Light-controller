using LedHub.Core;
using static LedHub.Services.Effects.EffectMath;

namespace LedHub.Services.Effects;

/// <summary>
/// Место экрана главного монитора в пространстве эффекта: левый верхний угол и размер.
/// </summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height);

/// <summary>
/// Картинка главного монитора на текущем кадре, уже сглаженная и разобранная для эффектов.
/// </summary>
public sealed class ScreenFrame
{
    public const int MoodCount = 4;

    public ScreenFrame(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        Cells = new RgbColor[columns * rows];
        Array.Fill(Mood, Black);
    }

    /// <summary>
    /// Размер уменьшенной картинки экрана.
    /// </summary>
    public int Columns { get; }

    public int Rows { get; }

    /// <summary>
    /// Уменьшенная картинка экрана по строкам сверху вниз.
    /// </summary>
    public RgbColor[] Cells { get; }

    /// <summary>
    /// Главный цвет картинки: самый заметный насыщенный тон, а на серой картинке — средний цвет.
    /// </summary>
    public RgbColor Dominant { get; set; }

    /// <summary>
    /// Несколько главных цветов картинки, медленно плывущих за ней.
    /// </summary>
    public RgbColor[] Mood { get; } = new RgbColor[MoodCount];

    /// <summary>
    /// Сила вспышки от 0 до 1: подскакивает, когда экран резко светлеет, и затем гаснет.
    /// </summary>
    public double Flash { get; set; }

    public RgbColor FlashColor { get; set; } = RgbColor.White;

    /// <summary>
    /// Где экран стоит на сцене; null — эффект идёт вдоль ленты или монитора на сцене нет.
    /// </summary>
    public ScreenRect? Screen { get; set; }

    public RgbColor At(int column, int row)
    {
        return Cells[Math.Clamp(row, 0, Rows - 1) * Columns + Math.Clamp(column, 0, Columns - 1)];
    }

    /// <summary>
    /// Цвет экрана в точке: u и v от 0 до 1 слева направо и сверху вниз, между клетками — плавно.
    /// </summary>
    public RgbColor Sample(double u, double v)
    {
        var x = Math.Clamp(u, 0, 1) * Columns - 0.5;
        var y = Math.Clamp(v, 0, 1) * Rows - 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);

        var top = Mix(At(x0, y0), At(x0 + 1, y0), x - x0);
        var bottom = Mix(At(x0, y0 + 1), At(x0 + 1, y0 + 1), x - x0);

        return Mix(top, bottom, y - y0);
    }

    /// <summary>
    /// Цвет края экрана: t от 0 до 1 обходит экран по часовой стрелке от левого нижнего угла — вверх, вправо, вниз и обратно влево.
    /// </summary>
    public RgbColor Edge(double t)
    {
        // Берём чуть внутрь от края, там картинка содержательнее, чем самая кромка
        const double inset = 0.08;

        t = Frac(t) * 4;
        var side = (int)t;
        var along = t - side;

        return side switch
        {
            0 => Sample(inset, 1 - along),
            1 => Sample(along, inset),
            2 => Sample(1 - inset, along),
            _ => Sample(1 - along, 1 - inset)
        };
    }
}

/// <summary>
/// Снимает главный монитор и на каждом кадре готовит для эффектов «по экрану» картинку, главный цвет, настроение и вспышки.
/// </summary>
public sealed class ScreenTracker : IDisposable
{
    private const int LongSide = 32;

    // Доля нового кадра в сглаженной картинке: меньше — плавнее, но медленнее
    private const double CellFollow = 0.35;
    private const double DominantFollow = 0.12;
    private const double MoodFollow = 0.015;
    private const double AverageFollow = 0.05;
    private const double FlashDecay = 0.86;

    // Вспышка — когда экран светлее своего недавнего среднего на столько (при чувствительности 1×)
    private const double FlashThreshold = 0.14;

    private const int HueBins = 24;

    // Монитор переспрашивается раз в пару секунд: его могли сменить в настройках или отключить
    private const int FramesBetweenMonitorChecks = 40;

    private ScreenGrabber? _grabber;
    private MonitorInfo? _monitor;
    private double[] _smooth = Array.Empty<double>();
    private double _averageLight;
    private int _framesSinceCheck = FramesBetweenMonitorChecks;
    private bool _hasFrame;

    public ScreenFrame Frame { get; private set; } = new(1, 1);

    /// <summary>
    /// Снимает экран для кадра; gain — чувствительность вспышек.
    /// </summary>
    public void Update(double gain)
    {
        EnsureMonitor();

        if (_grabber == null || _monitor == null)
            return;

        var pixels = _grabber.Capture(_monitor);
        var count = Frame.Columns * Frame.Rows;
        var light = 0.0;

        for (var i = 0; i < count; i++)
        {
            // Кадр в формате BGRA
            var b = pixels[i * 4];
            var g = pixels[i * 4 + 1];
            var r = pixels[i * 4 + 2];
            var follow = _hasFrame ? CellFollow : 1;

            _smooth[i * 3] += (r - _smooth[i * 3]) * follow;
            _smooth[i * 3 + 1] += (g - _smooth[i * 3 + 1]) * follow;
            _smooth[i * 3 + 2] += (b - _smooth[i * 3 + 2]) * follow;
            Frame.Cells[i] = new RgbColor(ToByte(_smooth[i * 3]), ToByte(_smooth[i * 3 + 1]), ToByte(_smooth[i * 3 + 2]));

            // Яркость берётся по сырому кадру, чтобы вспышка не размазывалась сглаживанием
            light += (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        }

        light /= count;

        var hues = HueHistogram(Frame.Cells);
        UpdateDominant(hues);
        UpdateMood(hues);
        UpdateFlash(pixels, light, gain);

        _hasFrame = true;
    }

    public void Dispose()
    {
        _grabber?.Dispose();
    }

    private void EnsureMonitor()
    {
        if (++_framesSinceCheck < FramesBetweenMonitorChecks)
            return;

        _framesSinceCheck = 0;

        var monitor = MainMonitor.Resolve();
        if (monitor == _monitor)
            return;

        _monitor = monitor;
        _grabber?.Dispose();
        _grabber = null;

        if (monitor == null)
            return;

        var aspect = (double)monitor.Width / Math.Max(1, monitor.Height);
        var columns = aspect >= 1 ? LongSide : Math.Max(8, (int)Math.Round(LongSide * aspect));
        var rows = aspect >= 1 ? Math.Max(8, (int)Math.Round(LongSide / aspect)) : LongSide;

        _grabber = new ScreenGrabber(columns, rows);
        _smooth = new double[columns * rows * 3];
        _hasFrame = false;

        var frame = new ScreenFrame(columns, rows) { Screen = Frame.Screen };
        Array.Copy(Frame.Mood, frame.Mood, ScreenFrame.MoodCount);
        Frame = frame;
    }

    /// <summary>
    /// Насколько заметен каждый тон на экране и какой у него средний цвет; серые и тёмные места почти не считаются.
    /// </summary>
    private static (double Weight, double R, double G, double B)[] HueHistogram(RgbColor[] cells)
    {
        var bins = new (double Weight, double R, double G, double B)[HueBins];

        foreach (var cell in cells)
        {
            var (hue, saturation, value) = cell.ToHsv();
            var weight = saturation * saturation * value;
            var bin = (int)(hue / 360 * HueBins) % HueBins;

            bins[bin].Weight += weight;
            bins[bin].R += cell.R * weight;
            bins[bin].G += cell.G * weight;
            bins[bin].B += cell.B * weight;
        }

        return bins;
    }

    private void UpdateDominant((double Weight, double R, double G, double B)[] hues)
    {
        var best = hues.MaxBy(h => h.Weight);

        // На почти серой картинке главного тона нет — берём средний цвет
        var target = best.Weight > Frame.Cells.Length * 0.03
            ? ColorOf(best)
            : Average(Frame.Cells);

        Frame.Dominant = _hasFrame ? Mix(Frame.Dominant, target, DominantFollow) : target;
    }

    private void UpdateMood((double Weight, double R, double G, double B)[] hues)
    {
        var top = hues
            .Where(h => h.Weight > 0.01)
            .OrderByDescending(h => h.Weight)
            .Take(ScreenFrame.MoodCount)
            .Select(ColorOf)
            .ToList();

        if (top.Count == 0)
            top.Add(Average(Frame.Cells));

        for (var i = 0; i < ScreenFrame.MoodCount; i++)
        {
            // Если тонов меньше, чем мест, оставшиеся повторяют главные
            var target = top[i % top.Count];
            Frame.Mood[i] = _hasFrame ? Mix(Frame.Mood[i], target, MoodFollow) : target;
        }
    }

    private void UpdateFlash(byte[] pixels, double light, double gain)
    {
        Frame.Flash *= FlashDecay;

        if (_hasFrame && light - _averageLight > FlashThreshold / Math.Max(gain, 0.1))
        {
            Frame.Flash = 1;
            Frame.FlashColor = BrightestColor(pixels, light);
        }

        _averageLight = _hasFrame ? _averageLight + (light - _averageLight) * AverageFollow : light;
    }

    /// <summary>
    /// Средний цвет мест, которые светлее средней яркости кадра, — цвет самой вспышки.
    /// </summary>
    private RgbColor BrightestColor(byte[] pixels, double light)
    {
        long r = 0, g = 0, b = 0, count = 0;

        for (var i = 0; i < Frame.Cells.Length; i++)
        {
            var pb = pixels[i * 4];
            var pg = pixels[i * 4 + 1];
            var pr = pixels[i * 4 + 2];

            if ((0.2126 * pr + 0.7152 * pg + 0.0722 * pb) / 255 < light)
                continue;

            r += pr;
            g += pg;
            b += pb;
            count++;
        }

        return count == 0 ? RgbColor.White : new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static RgbColor ColorOf((double Weight, double R, double G, double B) bin)
    {
        return bin.Weight <= 0
            ? Black
            : new RgbColor(ToByte(bin.R / bin.Weight), ToByte(bin.G / bin.Weight), ToByte(bin.B / bin.Weight));
    }

    private static RgbColor Average(RgbColor[] cells)
    {
        return new RgbColor(
            ToByte(cells.Average(c => c.R)),
            ToByte(cells.Average(c => c.G)),
            ToByte(cells.Average(c => c.B)));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}

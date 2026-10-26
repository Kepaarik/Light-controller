using LedHub.Core;

namespace LedHub.Services;

/// <summary>
/// К какому монитору привязана лента: сам монитор, его поворот и раскладка ленты (только у адресных лент).
/// </summary>
public sealed record MonitorBinding(MonitorInfo Monitor, MonitorRotation Rotation, ScreenLayoutConfig? Layout);

/// <summary>
/// Подсветка мониторов: каждая лента повторяет цвета своего монитора. Обычная лента светится средним цветом
/// картинки, адресная — цветами краёв по своей раскладке.
/// </summary>
public sealed class MonitorSyncSource : ILightSource, IDisposable
{
    // Длинная сторона уменьшенного кадра; короткая — по пропорциям монитора
    private const int FrameLongSide = 64;

    // Насколько вглубь картинки (в пикселях кадра) смотреть от края для светодиода
    private const int EdgeDepth = 6;

    // Доля нового кадра в цвете: меньше — плавнее, но с запаздыванием
    private const double Smoothing = 0.45;

    // Средний цвет экрана обычно блёклый — немного поднимаем насыщенность
    private const double SaturationBoost = 1.35;

    private readonly Func<LedDevice, MonitorBinding?> _bindingFor;
    private readonly Dictionary<string, MonitorFrame> _frames = new();
    private readonly HashSet<string> _capturedThisFrame = new();
    private readonly Dictionary<string, (double R, double G, double B)> _solidStates = new();
    private readonly Dictionary<string, (double R, double G, double B)[]> _stripStates = new();

    /// <summary>
    /// bindingFor — монитор для ленты; читается на каждом кадре, поэтому поворот и раскладку можно менять на ходу.
    /// </summary>
    public MonitorSyncSource(Func<LedDevice, MonitorBinding?> bindingFor)
    {
        _bindingFor = bindingFor;
    }

    public void BeginFrame(double phase, EffectSettings settings)
    {
        // Мониторы снимаются по требованию, но не больше одного раза за кадр
        _capturedThisFrame.Clear();
    }

    public RgbColor RenderSolid(LedDevice device)
    {
        if (_bindingFor(device) is not { } binding)
            return new RgbColor(0, 0, 0);

        var frame = Capture(binding.Monitor);
        var target = Boost(frame.Average(0, frame.Width - 1, 0, frame.Height - 1));

        var state = _solidStates.TryGetValue(device.Id, out var previous) ? previous : default;
        state = Smooth(state, target);
        _solidStates[device.Id] = state;

        return ToColor(state);
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        var colors = new RgbColor[ledCount];

        if (_bindingFor(device) is not { } binding)
            return colors;

        // Адресная лента без раскладки светится средним цветом своего монитора
        if (binding.Layout == null)
        {
            Array.Fill(colors, RenderSolid(device));
            return colors;
        }

        if (!_stripStates.TryGetValue(device.Id, out var states) || states.Length != ledCount)
        {
            states = new (double R, double G, double B)[ledCount];
            _stripStates[device.Id] = states;
        }

        var frame = Capture(binding.Monitor);
        var points = ScreenLayoutMath.Build(binding.Layout, ledCount, PhysicalAspect(binding));

        for (var i = 0; i < ledCount; i++)
        {
            // Светодиоды, не попавшие ни на одну сторону, гасим
            var target = points[i] is { } point
                ? Boost(SamplePoint(frame, point, binding))
                : new RgbColor(0, 0, 0);

            states[i] = Smooth(states[i], target);
            colors[i] = ToColor(states[i]);
        }

        return colors;
    }

    public void Dispose()
    {
        foreach (var frame in _frames.Values)
            frame.Dispose();

        _frames.Clear();
    }

    /// <summary>
    /// Отношение ширины к высоте монитора так, как он стоит на самом деле, с учётом поворота.
    /// </summary>
    public static double PhysicalAspect(MonitorBinding binding)
    {
        var aspect = (double)binding.Monitor.Width / Math.Max(1, binding.Monitor.Height);

        return binding.Rotation is MonitorRotation.Clockwise90 or MonitorRotation.CounterClockwise270
            ? 1 / aspect
            : aspect;
    }

    private MonitorFrame Capture(MonitorInfo monitor)
    {
        if (!_frames.TryGetValue(monitor.Key, out var frame))
        {
            frame = new MonitorFrame(monitor);
            _frames[monitor.Key] = frame;
        }

        if (_capturedThisFrame.Add(monitor.Key))
            frame.Capture(monitor);

        return frame;
    }

    /// <summary>
    /// Цвет картинки рядом со светодиодом: точку раскладки переводим в координаты кадра с учётом
    /// вида сзади и поворота монитора, затем смотрим немного внутрь картинки — к её центру.
    /// </summary>
    private static RgbColor SamplePoint(MonitorFrame frame, LedPoint point, MonitorBinding binding)
    {
        var u = binding.Layout!.IsViewedFromBack ? 1 - point.X : point.X;
        var v = point.Y;

        var (x, y) = binding.Rotation switch
        {
            MonitorRotation.Clockwise90 => (v, 1 - u),
            MonitorRotation.Upside180 => (1 - u, 1 - v),
            MonitorRotation.CounterClockwise270 => (1 - v, u),
            _ => (u, v)
        };

        var px = x * (frame.Width - 1);
        var py = y * (frame.Height - 1);

        var toCenterX = (frame.Width - 1) / 2.0 - px;
        var toCenterY = (frame.Height - 1) / 2.0 - py;
        var distance = Math.Sqrt(toCenterX * toCenterX + toCenterY * toCenterY);
        var step = Math.Min(EdgeDepth / 2.0, distance);

        if (distance > 0)
        {
            px += toCenterX / distance * step;
            py += toCenterY / distance * step;
        }

        var cx = (int)Math.Round(px);
        var cy = (int)Math.Round(py);

        return frame.Average(cx - 2, cx + 2, cy - 2, cy + 2);
    }

    private static RgbColor Boost(RgbColor color)
    {
        var (hue, saturation, value) = color.ToHsv();

        return RgbColor.FromHsv(hue, Math.Min(1, saturation * SaturationBoost), value);
    }

    private static (double R, double G, double B) Smooth((double R, double G, double B) state, RgbColor target)
    {
        return (
            state.R + (target.R - state.R) * Smoothing,
            state.G + (target.G - state.G) * Smoothing,
            state.B + (target.B - state.B) * Smoothing);
    }

    private static RgbColor ToColor((double R, double G, double B) state)
    {
        return new RgbColor((byte)Math.Round(state.R), (byte)Math.Round(state.G), (byte)Math.Round(state.B));
    }

    /// <summary>
    /// Уменьшенный кадр одного монитора в его пропорциях.
    /// </summary>
    private sealed class MonitorFrame : IDisposable
    {
        private readonly ScreenGrabber _grabber;
        private byte[] _pixels = Array.Empty<byte>();

        public MonitorFrame(MonitorInfo monitor)
        {
            var aspect = (double)monitor.Width / Math.Max(1, monitor.Height);

            Width = aspect >= 1 ? FrameLongSide : Math.Max(8, (int)Math.Round(FrameLongSide * aspect));
            Height = aspect >= 1 ? Math.Max(8, (int)Math.Round(FrameLongSide / aspect)) : FrameLongSide;
            _grabber = new ScreenGrabber(Width, Height);
        }

        public int Width { get; }

        public int Height { get; }

        public void Capture(MonitorInfo monitor)
        {
            _pixels = _grabber.Capture(monitor);
        }

        public RgbColor Average(int x0, int x1, int y0, int y1)
        {
            x0 = Math.Clamp(x0, 0, Width - 1);
            x1 = Math.Clamp(x1, 0, Width - 1);
            y0 = Math.Clamp(y0, 0, Height - 1);
            y1 = Math.Clamp(y1, 0, Height - 1);

            long r = 0, g = 0, b = 0, count = 0;

            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    // Кадр в формате BGRA
                    var i = (y * Width + x) * 4;
                    b += _pixels[i];
                    g += _pixels[i + 1];
                    r += _pixels[i + 2];
                    count++;
                }
            }

            return count == 0
                ? new RgbColor(0, 0, 0)
                : new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }

        public void Dispose()
        {
            _grabber.Dispose();
        }
    }
}

using LedHub.Core;

namespace LedHub.Services;

/// <summary>
/// Подсветка под экран: обычные ленты светятся средним цветом экрана, адресные — цветами его краёв
/// по своей раскладке вокруг монитора.
/// </summary>
public sealed class ScreenSource : ILightSource, IDisposable
{
    private const int FrameWidth = 64;
    private const int FrameHeight = 36;

    // Сколько пикселей уменьшенного кадра от края берётся для светодиода у этого края
    private const int EdgeDepth = 6;

    // Доля нового кадра в цвете: меньше — плавнее, но с запаздыванием
    private const double Smoothing = 0.45;

    // Средний цвет экрана обычно блёклый — немного поднимаем насыщенность
    private const double SaturationBoost = 1.35;

    private readonly ScreenGrabber _grabber = new(FrameWidth, FrameHeight);
    private readonly Func<LedDevice, ScreenLayoutConfig> _layoutFor;
    private readonly Dictionary<string, (double R, double G, double B)[]> _stripStates = new();
    private byte[] _pixels = Array.Empty<byte>();
    private (double R, double G, double B) _solidState;

    /// <summary>
    /// layoutFor — раскладка ленты вокруг монитора; читается на каждом кадре, поэтому её можно менять на ходу.
    /// </summary>
    public ScreenSource(Func<LedDevice, ScreenLayoutConfig> layoutFor)
    {
        _layoutFor = layoutFor;
    }

    public void BeginFrame(double phase, EffectSettings settings)
    {
        _pixels = _grabber.Capture();
    }

    public RgbColor RenderSolid()
    {
        var average = Boost(Average(0, FrameWidth - 1, 0, FrameHeight - 1));
        _solidState = Smooth(_solidState, average);

        return ToColor(_solidState);
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        if (!_stripStates.TryGetValue(device.Id, out var states) || states.Length != ledCount)
        {
            states = new (double R, double G, double B)[ledCount];
            _stripStates[device.Id] = states;
        }

        var points = ScreenLayoutMath.Build(_layoutFor(device), ledCount, (double)FrameWidth / FrameHeight);
        var colors = new RgbColor[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            // Светодиоды, не попавшие ни на одну сторону, гасим
            var target = points[i] is { } point ? Boost(SamplePoint(point)) : new RgbColor(0, 0, 0);

            states[i] = Smooth(states[i], target);
            colors[i] = ToColor(states[i]);
        }

        return colors;
    }

    public void Dispose()
    {
        _grabber.Dispose();
    }

    /// <summary>
    /// Цвет картинки у края экрана рядом со светодиодом: полоса от края внутрь кадра.
    /// </summary>
    private RgbColor SamplePoint(LedPoint point)
    {
        var x = (int)Math.Round(point.X * (FrameWidth - 1));
        var y = (int)Math.Round(point.Y * (FrameHeight - 1));

        return point.Side switch
        {
            ScreenSide.Left => Average(0, EdgeDepth - 1, y - 2, y + 2),
            ScreenSide.Right => Average(FrameWidth - EdgeDepth, FrameWidth - 1, y - 2, y + 2),
            ScreenSide.Top => Average(x - 2, x + 2, 0, EdgeDepth - 1),
            _ => Average(x - 2, x + 2, FrameHeight - EdgeDepth, FrameHeight - 1)
        };
    }

    private RgbColor Average(int x0, int x1, int y0, int y1)
    {
        x0 = Math.Clamp(x0, 0, FrameWidth - 1);
        x1 = Math.Clamp(x1, 0, FrameWidth - 1);
        y0 = Math.Clamp(y0, 0, FrameHeight - 1);
        y1 = Math.Clamp(y1, 0, FrameHeight - 1);

        long r = 0, g = 0, b = 0, count = 0;

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                // Кадр в формате BGRA
                var i = (y * FrameWidth + x) * 4;
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
}

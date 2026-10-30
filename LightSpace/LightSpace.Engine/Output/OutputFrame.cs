using LightSpace.Core;

namespace LightSpace.Engine.Output;

/// <summary>
/// Кадр для одного устройства: один цвет на всё устройство или свой цвет каждого светодиода, и яркость 0–100 %.
/// </summary>
public sealed record OutputFrame(RgbColor Color, IReadOnlyList<RgbColor>? Leds, int Brightness)
{
    /// <summary>
    /// Кадр, в котором всё устройство светится одним цветом.
    /// </summary>
    public static OutputFrame Solid(RgbColor color, int brightness) => new(color, null, brightness);

    /// <summary>
    /// Кадр со своим цветом каждого светодиода по порядку.
    /// </summary>
    public static OutputFrame PerLed(IReadOnlyList<RgbColor> leds, int brightness) => new(Average(leds), leds, brightness);

    /// <summary>
    /// Средний цвет кадра: его показывает устройство, у которого нет отдельных светодиодов.
    /// </summary>
    private static RgbColor Average(IReadOnlyList<RgbColor> leds)
    {
        if (leds.Count == 0)
            return RgbColor.Black;

        long r = 0, g = 0, b = 0;

        foreach (var led in leds)
        {
            r += led.R;
            g += led.G;
            b += led.B;
        }

        return new RgbColor((byte)(r / leds.Count), (byte)(g / leds.Count), (byte)(b / leds.Count));
    }
}

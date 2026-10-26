namespace LedHub.Core;

/// <summary>
/// Цвет в пространстве RGB, 8 бит на канал.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor White = new(255, 255, 255);

    public static RgbColor Parse(string hex)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);

        return new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>
    /// Возвращает цвет, приглушённый до указанной яркости (0–100 %).
    /// </summary>
    public RgbColor Scale(int brightness)
    {
        var factor = Math.Clamp(brightness, 0, 100) / 100.0;

        return new RgbColor(
            (byte)Math.Round(R * factor),
            (byte)Math.Round(G * factor),
            (byte)Math.Round(B * factor));
    }

    /// <summary>
    /// Возвращает цвет в HSV: тон 0–360, насыщенность и значение 0–1.
    /// </summary>
    public (double Hue, double Saturation, double Value) ToHsv()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == r)
                hue = 60 * ((g - b) / delta % 6);
            else if (max == g)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0)
            hue += 360;

        return (hue, max == 0 ? 0 : delta / max, max);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

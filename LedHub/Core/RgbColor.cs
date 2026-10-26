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
    /// Создаёт цвет из HSV: тон 0–360, насыщенность и значение 0–1.
    /// </summary>
    public static RgbColor FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;

        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };

        return new RgbColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
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

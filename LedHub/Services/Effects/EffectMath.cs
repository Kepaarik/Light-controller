using LedHub.Core;

namespace LedHub.Services.Effects;

/// <summary>
/// Шум и смешивание цветов для эффектов. Шум детерминированный: одна и та же точка в одно и то же время
/// всегда даёт одно значение, поэтому эффектам не нужно хранить состояние каждого светодиода.
/// </summary>
public static class EffectMath
{
    public static readonly RgbColor Black = new(0, 0, 0);

    /// <summary>
    /// Псевдослучайное число от 0 до 1 для набора целых чисел.
    /// </summary>
    public static double Hash(int a, int b = 0, int c = 0)
    {
        unchecked
        {
            var h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u + 3266489917u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;

            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>
    /// Гладкий шум от 0 до 1 в трёх измерениях; третье обычно — время.
    /// </summary>
    public static double Noise(double x, double y, double z)
    {
        var xi = (int)Math.Floor(x);
        var yi = (int)Math.Floor(y);
        var zi = (int)Math.Floor(z);

        var xf = Fade(x - xi);
        var yf = Fade(y - yi);
        var zf = Fade(z - zi);

        var c000 = Hash(xi, yi, zi);
        var c100 = Hash(xi + 1, yi, zi);
        var c010 = Hash(xi, yi + 1, zi);
        var c110 = Hash(xi + 1, yi + 1, zi);
        var c001 = Hash(xi, yi, zi + 1);
        var c101 = Hash(xi + 1, yi, zi + 1);
        var c011 = Hash(xi, yi + 1, zi + 1);
        var c111 = Hash(xi + 1, yi + 1, zi + 1);

        var near = Lerp(Lerp(c000, c100, xf), Lerp(c010, c110, xf), yf);
        var far = Lerp(Lerp(c001, c101, xf), Lerp(c011, c111, xf), yf);

        return Lerp(near, far, zf);
    }

    /// <summary>
    /// Шум из нескольких слоёв разного масштаба — выглядит естественнее, как облака или пламя.
    /// </summary>
    public static double Fbm(double x, double y, double z, int octaves = 3)
    {
        double sum = 0, amplitude = 0.5, total = 0;

        for (var i = 0; i < octaves; i++)
        {
            sum += Noise(x, y, z) * amplitude;
            total += amplitude;

            x *= 2;
            y *= 2;
            z *= 1.5;
            amplitude /= 2;
        }

        return sum / total;
    }

    public static double Frac(double value)
    {
        return value - Math.Floor(value);
    }

    /// <summary>
    /// Плавный переход от 0 к 1, когда value идёт от 0 к 1; за пределами — 0 или 1.
    /// </summary>
    public static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);

        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// «Треугольник»: 0 → 1 → 0 за каждую единицу времени, для движения туда и обратно.
    /// </summary>
    public static double PingPong(double value)
    {
        return 1 - Math.Abs(Frac(value) * 2 - 1);
    }

    public static RgbColor Mix(RgbColor from, RgbColor to, double t)
    {
        t = Math.Clamp(t, 0, 1);

        return new RgbColor(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    public static RgbColor Dim(RgbColor color, double factor)
    {
        return Mix(Black, color, factor);
    }

    /// <summary>
    /// Ярче из двух по каждому каналу — чтобы пересекающиеся круги и вспышки складывались.
    /// </summary>
    public static RgbColor Max(RgbColor a, RgbColor b)
    {
        return new RgbColor(Math.Max(a.R, b.R), Math.Max(a.G, b.G), Math.Max(a.B, b.B));
    }

    /// <summary>
    /// Цвет по палитре из нескольких точек; t от 0 (первый цвет) до 1 (последний).
    /// </summary>
    public static RgbColor Palette(double t, params RgbColor[] colors)
    {
        t = Math.Clamp(t, 0, 1) * (colors.Length - 1);
        var index = Math.Min((int)t, colors.Length - 2);

        return Mix(colors[index], colors[index + 1], t - index);
    }

    /// <summary>
    /// Цвет по замкнутой палитре: после последнего цвета снова идёт первый.
    /// </summary>
    public static RgbColor LoopPalette(double t, params RgbColor[] colors)
    {
        t = Frac(t) * colors.Length;
        var index = (int)t % colors.Length;

        return Mix(colors[index], colors[(index + 1) % colors.Length], Smooth(t - (int)t));
    }

    private static double Fade(double t)
    {
        return t * t * (3 - 2 * t);
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }
}

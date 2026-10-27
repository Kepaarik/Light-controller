using System.Windows;
using System.Windows.Media;

namespace LedHub.Services;

/// <summary>
/// Готовый вид стекла: с него удобно начать и потом подстроить ползунками.
/// </summary>
public sealed record GlassPreset(string Title, string Tint, double Transparency, double TintStrength, double Frost, double Shine)
{
    /// <summary>
    /// Образец цвета для кнопки пресета.
    /// </summary>
    public Brush Swatch { get; } = GlassTheme.SwatchOf(Tint);
}

/// <summary>
/// Перекрашивает стекло окна: тонировку под всем окном, карточки, блик по краю и слой содержимого.
/// Кисти лежат в ресурсах приложения, поэтому всё, что ссылается на них динамически, меняется сразу.
/// </summary>
public static class GlassTheme
{
    // Цвет, к которому стекло темнеет, — тот же, что у окна без стекла
    private static readonly Color Dark = Color.FromRgb(0x0E, 0x0F, 0x16);

    public static IReadOnlyList<GlassPreset> Presets { get; } = new GlassPreset[]
    {
        new("Фиолетовое", "#7C6CFF", 55, 30, 30, 50),
        new("Дымчатое", "#2A2E3C", 50, 25, 28, 40),
        new("Синее", "#3B7BFF", 55, 35, 30, 50),
        new("Изумрудное", "#21D19F", 55, 30, 28, 50),
        new("Розовое", "#FF4D8B", 55, 28, 30, 55),
        new("Графит", "#000000", 20, 0, 22, 30),
        new("Чистое", "#FFFFFF", 85, 6, 18, 65)
    };

    public static void Apply(GlassConfig glass)
    {
        var resources = Application.Current.Resources;
        var tint = ParseColor(glass.Tint);

        var transparency = Math.Clamp(glass.Transparency, 0, 100) / 100;
        var strength = Math.Clamp(glass.TintStrength, 0, 100) / 100;
        var frost = Math.Clamp(glass.Frost, 0, 100) / 100;
        var shine = Math.Clamp(glass.Shine, 0, 100) / 100;

        // Чем прозрачнее стекло, тем слабее тёмный слой; совсем без цвета оно не остаётся, пока задана тонировка
        var density = (1 - transparency) * 0.9 + strength * 0.15;
        var tinted = Mix(Dark, tint, strength * 0.6);

        // Как у жидкого стекла: сверху слева светлее и цветнее, к правому нижнему углу — глубже
        resources["GlassTintBrush"] = Gradient(new Point(0, 0), new Point(1, 1),
            (0, WithAlpha(Mix(tinted, tint, strength * 0.25), density * 0.85)),
            (1, WithAlpha(Mix(tinted, Dark, 0.35), density)));

        var cardTop = 0.04 + frost * 0.2;
        var cardColor = Mix(Colors.White, tint, strength * 0.2);

        resources["CardBrush"] = Gradient(new Point(0, 0), new Point(0, 1),
            (0, WithAlpha(cardColor, cardTop)),
            (1, WithAlpha(cardColor, cardTop * 0.45)));

        resources["GlassEdgeBrush"] = Gradient(new Point(0, 0), new Point(1, 1),
            (0, WithAlpha(Colors.White, 0.08 + shine * 0.42)),
            (0.45, WithAlpha(Colors.White, 0.03 + shine * 0.07)),
            (1, WithAlpha(Mix(Colors.White, tint, 0.5), 0.05 + shine * 0.25)));

        resources["LayerBrush"] = Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.02 + frost * 0.07)));
    }

    public static Brush SwatchOf(string hex)
    {
        var color = ParseColor(hex);

        return Gradient(new Point(0, 0), new Point(1, 1),
            (0, Mix(color, Colors.White, 0.25)),
            (1, Mix(color, Dark, 0.35)));
    }

    public static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Dark;
        }
    }

    private static LinearGradientBrush Gradient(Point start, Point end, params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };

        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));

        return Freeze(brush);
    }

    private static Color Mix(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);

        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private static Color WithAlpha(Color color, double alpha)
    {
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

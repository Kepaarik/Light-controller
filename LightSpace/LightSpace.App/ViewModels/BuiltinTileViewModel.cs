using System.Windows;
using System.Windows.Media;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Плитка встроенного эффекта в галерее: название и картинка, подобранная по названию. Живого превью нет —
/// эффект играет сам контроллер устройства.
/// </summary>
public sealed class BuiltinTileViewModel
{
    // Цвета картинок плиток: это не цвета темы, а цвета самих эффектов, как в живых превью обычных эффектов
    private static readonly Color[] Rainbow =
    {
        Color.FromRgb(0xFF, 0x30, 0x30), Color.FromRgb(0xFF, 0xA0, 0x20), Color.FromRgb(0xFF, 0xE8, 0x30),
        Color.FromRgb(0x40, 0xE0, 0x60), Color.FromRgb(0x30, 0xA0, 0xFF), Color.FromRgb(0x90, 0x50, 0xFF),
        Color.FromRgb(0xFF, 0x40, 0xC0)
    };

    private static readonly Color Dark = Color.FromRgb(0x08, 0x08, 0x10);

    public BuiltinTileViewModel(BuiltinEffectGroup group)
    {
        Group = group;
        Preview = CreatePreview(group.Title);
    }

    public BuiltinEffectGroup Group { get; }

    public string Title => Group.Title;

    /// <summary>
    /// Картинка эффекта на плитке.
    /// </summary>
    public Brush Preview { get; }

    /// <summary>
    /// Рисует картинку по смыслу названия: радуга, огонь, свечение, вспышки или бегущие полосы.
    /// </summary>
    private static Brush CreatePreview(string title)
    {
        var name = title.ToLowerInvariant();
        var color = Rainbow[(int)((uint)StableHash(name) % Rainbow.Length)];

        Brush brush;

        if (Has(name, "огонь"))
            brush = Vertical(Color.FromRgb(0xFF, 0xE0, 0x40), Color.FromRgb(0xFF, 0x70, 0x10), Color.FromRgb(0xC0, 0x10, 0x00), Dark);
        else if (Has(name, "лимон"))
            brush = Glow(Color.FromRgb(0xE8, 0xFF, 0x30));
        else if (Has(name, "син"))
            brush = Horizontal(Dark, Color.FromRgb(0x20, 0x60, 0xFF), Color.FromRgb(0x60, 0xC0, 0xFF), Dark);
        else if (Has(name, "дыхани", "пульс", "затухани", "капли", "круги", "тающ", "реакци", "распуск", "постоянн"))
            brush = Glow(color);
        else if (Has(name, "строб", "вспышк", "мигающ", "скачк", "электро", "мерцани", "ритм"))
            brush = Bands(Rainbow[0], Dark, Rainbow[3], Dark, Rainbow[4]);
        else if (Has(name, "бегущ", "погон", "лифт", "занавес", "змейк", "сканер", "полос"))
            brush = Bands(color, Dark, color, Dark, color, Dark);
        else if (Has(name, "вниз", "водопад"))
            brush = Vertical(Rainbow);
        else
            brush = Horizontal(Rainbow);

        brush.Freeze();
        return brush;
    }

    private static bool Has(string name, params string[] words) => words.Any(name.Contains);

    /// <summary>
    /// Хеш, который не меняется между запусками: у одного названия всегда один цвет.
    /// </summary>
    private static int StableHash(string text)
    {
        var hash = 17;

        foreach (var c in text)
            hash = unchecked(hash * 31 + c);

        return hash;
    }

    private static LinearGradientBrush Horizontal(params Color[] colors) => Gradient(new Point(0, 0.5), new Point(1, 0.5), colors);

    private static LinearGradientBrush Vertical(params Color[] colors) => Gradient(new Point(0.5, 0), new Point(0.5, 1), colors);

    private static LinearGradientBrush Gradient(Point start, Point end, Color[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };

        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], (double)i / (colors.Length - 1)));

        return brush;
    }

    /// <summary>
    /// Свечение из середины, гаснущее к краям.
    /// </summary>
    private static RadialGradientBrush Glow(Color color)
    {
        var brush = new RadialGradientBrush { RadiusX = 0.7, RadiusY = 0.9 };

        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, color.R, color.G, color.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Dark, 1));

        return brush;
    }

    /// <summary>
    /// Чёткие полосы одинаковой ширины слева направо.
    /// </summary>
    private static LinearGradientBrush Bands(params Color[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };

        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(colors[i], (double)i / colors.Length));
            brush.GradientStops.Add(new GradientStop(colors[i], (double)(i + 1) / colors.Length));
        }

        return brush;
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedHub.Core;
using LedHub.Services.Effects;

namespace LedHub.Controls;

/// <summary>
/// Картинка-превью эффекта на плитке: кадр эффекта, отрисованный на маленькой сцене с настройками по умолчанию.
/// </summary>
public sealed class EffectPreview : FrameworkElement
{
    public static new readonly DependencyProperty EffectProperty = DependencyProperty.Register(
        nameof(Effect), typeof(EffectDefinition), typeof(EffectPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(EffectPreview),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int Columns = 48;
    private const int Rows = 16;

    // Момент эффекта, который показывает превью: не самое начало, чтобы картинка была «живой»
    private const double PreviewTime = 3.3;

    // Превью одинаковы для всех плиток одного эффекта — считаем каждое один раз
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    public new EffectDefinition? Effect
    {
        get => (EffectDefinition?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Effect == null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        if (!Cache.TryGetValue(Effect.Id, out var bitmap))
        {
            bitmap = RenderPreview(Effect);
            Cache[Effect.Id] = bitmap;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        dc.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        dc.DrawImage(bitmap, bounds);
        dc.Pop();
    }

    private static BitmapSource RenderPreview(EffectDefinition effect)
    {
        const double height = (double)Rows / Columns;

        var frame = new EffectFrame(effect, new EffectValues(), false, 0.5, height / 2, 1, height, SampleAudio())
        {
            Time = PreviewTime,
            Screen = SampleScreen(height)
        };

        var pixels = new byte[Columns * Rows * 4];

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var pixel = new EffectPixel((column + 0.5) / Columns, (row + 0.5) / Rows * height, row * Columns + column);
                var color = effect.Render(frame, pixel);
                var i = (row * Columns + column) * 4;

                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(Columns, Rows, 96, 96, PixelFormats.Bgra32, null, pixels, Columns * 4);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>
    /// Придуманный звук для превью музыкальных эффектов, чтобы на плитке было видно, как они выглядят.
    /// </summary>
    private static AudioFrame SampleAudio()
    {
        var audio = new AudioFrame { Level = 0.65, Bass = 0.8, Hue = 210 };

        for (var i = 0; i < AudioFrame.BandCount; i++)
            audio.Bands[i] = Math.Clamp(0.85 - i * 0.035 + 0.15 * Math.Sin(i * 1.7), 0, 1);

        audio.Beats.Add(new AudioBeat(PreviewTime - 1.1, 150));
        audio.Beats.Add(new AudioBeat(PreviewTime - 0.45, 260));

        return audio;
    }

    /// <summary>
    /// Придуманная картинка монитора для превью эффектов «по экрану»: закат над морем, монитор в середине плитки.
    /// </summary>
    private static ScreenFrame SampleScreen(double height)
    {
        const int columns = 8;
        const int rows = 5;

        var screen = new ScreenFrame(columns, rows)
        {
            Dominant = new RgbColor(255, 120, 40),
            Flash = 0.6,
            FlashColor = new RgbColor(255, 220, 160),
            Screen = new ScreenRect(0.36, height * 0.15, 0.28, height * 0.7)
        };

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                // Сверху тёплое небо, снизу синее море
                var sky = RgbColor.FromHsv(20 + column * 6, 0.9, 1 - row * 0.12);
                var sea = RgbColor.FromHsv(215 - column * 4, 0.85, 0.75);
                screen.Cells[row * columns + column] = row < 3 ? sky : sea;
            }
        }

        screen.Mood[0] = new RgbColor(255, 110, 40);
        screen.Mood[1] = new RgbColor(255, 60, 110);
        screen.Mood[2] = new RgbColor(40, 110, 255);
        screen.Mood[3] = new RgbColor(120, 60, 220);

        return screen;
    }
}

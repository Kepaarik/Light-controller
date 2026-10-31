using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightSpace.Core;

namespace LightSpace.App.Controls;

/// <summary>
/// Кисть из цветов светодиодов: сетка Columns × Rows, растянутая на всю закрашиваемую область с плавными переходами
/// между точками. Цвета можно менять на каждом кадре — кисть обновляется сама. Создаётся и меняется в потоке окна.
/// </summary>
public sealed class ColorBitmap
{
    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _pixels;

    public ColorBitmap(int columns, int rows)
    {
        Columns = Math.Max(columns, 1);
        Rows = Math.Max(rows, 1);

        _bitmap = new WriteableBitmap(Columns, Rows, 96, 96, PixelFormats.Bgr32, null);
        _pixels = new byte[Columns * Rows * 4];

        var brush = new ImageBrush(_bitmap) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
        Brush = brush;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>
    /// Кисть с текущими цветами.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Записывает цвета по строкам слева направо, приглушив их до яркости 0–100 %; недостающие точки — чёрные.
    /// </summary>
    public void Write(IReadOnlyList<RgbColor> colors, int brightness = 100)
    {
        for (var i = 0; i < Columns * Rows; i++)
        {
            var color = i < colors.Count ? colors[i] : RgbColor.Black;

            if (brightness < 100)
                color = color.Scale(brightness);

            _pixels[i * 4] = color.B;
            _pixels[i * 4 + 1] = color.G;
            _pixels[i * 4 + 2] = color.R;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, Columns, Rows), _pixels, Columns * 4, 0);
    }
}

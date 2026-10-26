using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedHub.Core;

namespace LedHub.Controls;

/// <summary>
/// Круг выбора цвета: тон задаётся углом, насыщенность — расстоянием от центра.
/// Яркость колесом не задаётся, для неё есть отдельный слайдер.
/// </summary>
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(RgbColor),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(
            RgbColor.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            OnSelectedColorChanged));

    private const double ThumbRadius = 11;

    private WriteableBitmap? _wheel;
    private double _hue;
    private double _saturation;
    private bool _isDragging;

    public ColorWheel()
    {
        Cursor = Cursors.Cross;
    }

    public RgbColor SelectedColor
    {
        get => (RgbColor)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private double Diameter => Math.Max(0, Math.Min(ActualWidth, ActualHeight) - ThumbRadius * 2);

    private Point Center => new(ActualWidth / 2, ActualHeight / 2);

    protected override void OnRender(DrawingContext dc)
    {
        var diameter = Diameter;
        if (diameter <= 0)
            return;

        var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var pixelSize = (int)Math.Round(diameter * scale);

        if (_wheel == null || _wheel.PixelWidth != pixelSize)
            _wheel = RenderWheel(pixelSize);

        var center = Center;
        var radius = diameter / 2;

        dc.DrawImage(_wheel, new Rect(center.X - radius, center.Y - radius, diameter, diameter));

        // Ползунок рисуем полностью насыщенным цветом точки, без учёта яркости
        var angle = _hue * Math.PI / 180;
        var thumb = new Point(center.X + Math.Cos(angle) * _saturation * radius, center.Y - Math.Sin(angle) * _saturation * radius);
        var thumbColor = RgbColor.FromHsv(_hue, _saturation, 1);
        var fill = new SolidColorBrush(Color.FromRgb(thumbColor.R, thumbColor.G, thumbColor.B));

        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 4), thumb, ThumbRadius, ThumbRadius);
        dc.DrawEllipse(fill, new Pen(Brushes.White, 3), thumb, ThumbRadius, ThumbRadius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _isDragging = true;
        CaptureMouse();
        PickAt(e.GetPosition(this));

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
            PickAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _isDragging = false;
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheel)d;

        // Во время перетаскивания позицию задаёт мышь: обратный пересчёт из RGB дал бы дрожание ползунка
        if (wheel._isDragging)
            return;

        var (hue, saturation, _) = ((RgbColor)e.NewValue).ToHsv();
        wheel._hue = hue;
        wheel._saturation = saturation;
    }

    private void PickAt(Point position)
    {
        var radius = Diameter / 2;
        if (radius <= 0)
            return;

        var dx = position.X - Center.X;
        var dy = Center.Y - position.Y;

        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / radius);

        SetCurrentValue(SelectedColorProperty, RgbColor.FromHsv(_hue, _saturation, 1));
        InvalidateVisual();
    }

    private static WriteableBitmap RenderWheel(int size)
    {
        var pixels = new byte[size * size * 4];
        var radius = size / 2.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = radius - (y + 0.5);
                var distance = Math.Sqrt(dx * dx + dy * dy);

                // Мягкий край на один пиксель вместо зубчатой границы
                var alpha = Math.Clamp(radius - distance, 0, 1);
                if (alpha <= 0)
                    continue;

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var color = RgbColor.FromHsv(hue, Math.Min(1, distance / radius), 1);
                var i = (y * size + x) * 4;

                // Формат Pbgra32 требует цвет, уже умноженный на прозрачность
                pixels[i] = (byte)(color.B * alpha);
                pixels[i + 1] = (byte)(color.G * alpha);
                pixels[i + 2] = (byte)(color.R * alpha);
                pixels[i + 3] = (byte)(255 * alpha);
            }
        }

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

        return bitmap;
    }
}

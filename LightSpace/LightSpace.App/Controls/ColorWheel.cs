using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LightSpace.Core;

namespace LightSpace.App.Controls;

/// <summary>
/// Круг выбора цвета: тон задаётся углом, насыщенность — расстоянием от центра.
/// Яркость колесом не задаётся, для неё есть отдельный ползунок.
/// </summary>
public sealed class ColorWheel : Canvas
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(RgbColor),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(
            RgbColor.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    private const double ThumbRadius = 11;
    private const double ThumbStroke = 3;

    private readonly Image _wheel = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Ellipse _thumb = new()
    {
        Width = ThumbRadius * 2,
        Height = ThumbRadius * 2,
        Stroke = Brushes.White,
        StrokeThickness = ThumbStroke,
        IsHitTestVisible = false
    };

    private double _hue;
    private double _saturation;
    private bool _isDragging;

    public ColorWheel()
    {
        // Прозрачный фон нужен, чтобы клики ловились по всей площади, а не только по картинке
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;

        _thumb.SetResourceReference(EffectProperty, "ShadowKnob");

        Children.Add(_wheel);
        Children.Add(_thumb);

        SizeChanged += (_, _) => UpdateLayoutParts(rebuildWheel: true);
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => _isDragging = false;
    }

    public RgbColor SelectedColor
    {
        get => (RgbColor)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private double Diameter => Math.Max(0, Math.Min(ActualWidth, ActualHeight) - ThumbRadius * 2);

    private Point Center => new(ActualWidth / 2, ActualHeight / 2);

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheel)d;

        // Во время перетаскивания позицию задаёт мышь: обратный пересчёт из RGB дал бы дрожание ползунка
        if (wheel._isDragging)
            return;

        var (hue, saturation, _) = ((RgbColor)e.NewValue).ToHsv();
        wheel._hue = hue;
        wheel._saturation = saturation;

        wheel.UpdateLayoutParts(rebuildWheel: false);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        CaptureMouse();
        PickAt(e.GetPosition(this));

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            PickAt(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
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
        UpdateLayoutParts(rebuildWheel: false);
    }

    /// <summary>
    /// Расставляет круг и ручку по текущему размеру; круг перерисовывается только при смене размера или масштаба экрана.
    /// </summary>
    private void UpdateLayoutParts(bool rebuildWheel)
    {
        var diameter = Diameter;
        if (diameter <= 0)
            return;

        var center = Center;
        var radius = diameter / 2;

        if (rebuildWheel)
        {
            var scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            _wheel.Source = RenderWheel((int)Math.Round(diameter * scale));
            _wheel.Width = diameter;
            _wheel.Height = diameter;
            SetLeft(_wheel, center.X - radius);
            SetTop(_wheel, center.Y - radius);
        }

        // Ручку рисуем полностью насыщенным цветом точки, без учёта яркости
        var angle = _hue * Math.PI / 180;
        var thumbColor = RgbColor.FromHsv(_hue, _saturation, 1);

        _thumb.Fill = new SolidColorBrush(Color.FromRgb(thumbColor.R, thumbColor.G, thumbColor.B));
        SetLeft(_thumb, center.X + Math.Cos(angle) * _saturation * radius - ThumbRadius);
        SetTop(_thumb, center.Y - Math.Sin(angle) * _saturation * radius - ThumbRadius);
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

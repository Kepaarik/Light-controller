using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LightSpace.App.ViewModels;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.App.Controls;

/// <summary>
/// Холст сцены: устройства на своих местах с живыми цветами эффекта. Лента — полоса своей длины: адресная светится
/// каждым светодиодом, лента одного цвета — целиком. Клавиатура — корпус с клавишами. Под каждым устройством —
/// его значок и название. Устройство перетаскивают мышью, колесо над выбранным меняет его размер.
/// </summary>
public sealed class SceneCanvas : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(ProfileSceneViewModel), typeof(SceneCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSceneChanged));

    // Как часто обновляется превью: как кадры устройств
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    // Шаг размера устройства на одно деление колеса, в процентах ширины холста
    private const double WheelSizeStep = 5;

    // Насколько далеко от устройства щелчок ещё попадает в него, в пикселях
    private const double HitDistance = 8;

    // Примерно столько клавиш в ряду у полноразмерной клавиатуры: по нему считается размер клавиши на холсте
    private const double KeysPerRow = 22;

    private static readonly Brush CanvasFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)));
    private static readonly Pen CanvasPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x4C))), 1.5));
    private static readonly Brush BodyFill = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x38)));
    private static readonly Pen BodyPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4F, 0x60))), 1));
    private static readonly Pen SelectedPen = Freeze(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Brush LabelFill = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x11, 0x16)));
    private static readonly Brush LabelText = Freeze(new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEF)));
    private static readonly Typeface LabelFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly Typeface IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private SceneDeviceViewModel? _dragged;
    private Vector _dragOffset;

    public SceneCanvas()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _timer.Tick += OnTick;

        IsVisibleChanged += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => UpdateTimer();

        Focusable = false;
        ClipToBounds = true;
    }

    public ProfileSceneViewModel? Scene
    {
        get => (ProfileSceneViewModel?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private static void OnSceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (SceneCanvas)d;

        if (e.OldValue is ProfileSceneViewModel oldScene)
            oldScene.Rendered -= canvas.InvalidateVisual;

        if (e.NewValue is ProfileSceneViewModel newScene)
            newScene.Rendered += canvas.InvalidateVisual;

        canvas._dragged = null;
    }

    /// <summary>
    /// Превью идёт, только пока холст виден.
    /// </summary>
    private void UpdateTimer()
    {
        if (IsVisible && IsLoaded)
        {
            _clock.Restart();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var seconds = _clock.Elapsed.TotalSeconds;

        _clock.Restart();
        Scene?.Advance(seconds);
    }

    /// <summary>
    /// Прямоугольник холста в контроле: ширина 1, высота CanvasHeight, вписан с сохранением пропорций.
    /// </summary>
    private Rect CanvasRect()
    {
        var width = Math.Min(ActualWidth, ActualHeight / SceneConfig.CanvasHeight);
        var height = width * SceneConfig.CanvasHeight;

        return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
    }

    private static Point ToScreen(EffectPixel pixel, Rect canvas)
    {
        return ToScreen(new Point(pixel.X, pixel.Y), canvas);
    }

    /// <summary>
    /// Точка холста (X от 0 до 1, Y от 0 до высоты холста) — в точку контрола.
    /// </summary>
    private static Point ToScreen(Point point, Rect canvas)
    {
        return new Point(canvas.X + point.X * canvas.Width, canvas.Y + point.Y * canvas.Width);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var canvas = CanvasRect();

        if (canvas.Width <= 0)
            return;

        dc.DrawRoundedRectangle(CanvasFill, CanvasPen, canvas, 8, 8);

        if (Scene == null)
            return;

        foreach (var device in Scene.Devices)
            DrawDevice(dc, device, canvas);
    }

    private void DrawDevice(DrawingContext dc, SceneDeviceViewModel device, Rect canvas)
    {
        var bounds = device.Own.IsLinear ? DrawStrip(dc, device, canvas) : DrawKeyboard(dc, device, canvas);

        if (device.IsSelected)
        {
            var outline = bounds;

            outline.Inflate(5, 5);
            dc.DrawRoundedRectangle(null, SelectedPen, outline, 6, 6);
        }

        DrawLabel(dc, device.Icon, device.Name, new Point(bounds.X + bounds.Width / 2, bounds.Bottom + 16));
    }

    /// <summary>
    /// Толщина полосы ленты на холсте.
    /// </summary>
    private static double StripThickness(Rect canvas)
    {
        return Math.Clamp(canvas.Width / 70, 8, 14);
    }

    /// <summary>
    /// Лента — полоса своей длины и поворота: адресная светится каждым светодиодом, лента одного цвета — целиком.
    /// Возвращает прямоугольник вокруг полосы.
    /// </summary>
    private static Rect DrawStrip(DrawingContext dc, SceneDeviceViewModel device, Rect canvas)
    {
        var (start, end) = device.CanvasLine;
        var from = ToScreen(start, canvas);
        var to = ToScreen(end, canvas);
        var thickness = StripThickness(canvas);
        var (points, _) = device.CanvasPoints;

        // Корпус ленты: светодиоды лежат на нём, у ленты одного цвета он виден ободком
        dc.DrawLine(RoundPen(BodyFill, thickness + 4), from, to);

        if (points.Count == 0)
        {
            dc.DrawLine(RoundPen(BrushOf(device.Colors, 0), thickness), from, to);
        }
        else
        {
            var radius = thickness / 2 - 1;

            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is { } pixel)
                    dc.DrawEllipse(BrushOf(device.Colors, i), null, ToScreen(pixel, canvas), radius, radius);
            }
        }

        var bounds = new Rect(from, to);

        bounds.Inflate(thickness / 2 + 2, thickness / 2 + 2);

        return bounds;
    }

    /// <summary>
    /// Клавиатура — корпус вокруг клавиш, каждая клавиша светится своим цветом. Возвращает прямоугольник корпуса.
    /// </summary>
    private static Rect DrawKeyboard(DrawingContext dc, SceneDeviceViewModel device, Rect canvas)
    {
        var (points, center) = device.CanvasPoints;
        var keySize = Math.Max(device.Placement.Size * canvas.Width / KeysPerRow * 0.8, 2);
        var placed = points.OfType<EffectPixel>().Select(p => ToScreen(p, canvas)).ToList();

        if (placed.Count == 0)
            placed.Add(ToScreen(center, canvas));

        var body = new Rect(placed[0], placed[0]);

        foreach (var point in placed)
            body.Union(point);

        body.Inflate(keySize, keySize);
        dc.DrawRoundedRectangle(BodyFill, BodyPen, body, 6, 6);

        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is not { } pixel)
                continue;

            var point = ToScreen(pixel, canvas);
            var key = new Rect(point.X - keySize / 2, point.Y - keySize / 2, keySize, keySize);

            dc.DrawRoundedRectangle(BrushOf(device.Colors, i), null, key, keySize / 5, keySize / 5);
        }

        return body;
    }

    private static Pen RoundPen(Brush brush, double thickness)
    {
        return Freeze(new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    }

    private static Brush BrushOf(IReadOnlyList<RgbColor> colors, int index)
    {
        if (index >= colors.Count)
            return Brushes.Black;

        var color = colors[index];

        return Freeze(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));
    }

    /// <summary>
    /// Подпись устройства на подложке: значок и название, по середине center.
    /// </summary>
    private void DrawLabel(DrawingContext dc, string icon, string name, Point center)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var glyph = new FormattedText(icon, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, IconFont, 12, LabelText, pixelsPerDip);
        var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, 11, LabelText, pixelsPerDip);

        const double gap = 5;
        const double padding = 6;

        var width = glyph.Width + gap + text.Width + padding * 2;
        var height = Math.Max(glyph.Height, text.Height) + 4;
        var pill = new Rect(center.X - width / 2, center.Y - height / 2, width, height);

        dc.DrawRoundedRectangle(LabelFill, null, pill, height / 2, height / 2);
        dc.DrawText(glyph, new Point(pill.X + padding, center.Y - glyph.Height / 2));
        dc.DrawText(text, new Point(pill.X + padding + glyph.Width + gap, center.Y - text.Height / 2));
    }

    /// <summary>
    /// Устройство под точкой: лента — если точка рядом с её полосой, клавиатура — если внутри корпуса.
    /// Из нескольких — ближайшее к своей середине; null — мимо.
    /// </summary>
    private SceneDeviceViewModel? DeviceAt(Point position, Rect canvas)
    {
        if (Scene == null)
            return null;

        SceneDeviceViewModel? nearest = null;
        var nearestDistance = double.MaxValue;

        foreach (var device in Scene.Devices)
        {
            var (_, center) = device.CanvasPoints;
            var isHit = device.Own.IsLinear ? IsNearStrip(device, position, canvas) : IsInsideKeyboard(device, position, canvas);

            if (!isHit)
                continue;

            var distance = (ToScreen(center, canvas) - position).Length;

            if (distance < nearestDistance)
            {
                nearest = device;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static bool IsNearStrip(SceneDeviceViewModel device, Point position, Rect canvas)
    {
        var (start, end) = device.CanvasLine;
        var from = ToScreen(start, canvas);
        var line = ToScreen(end, canvas) - from;
        var along = line.LengthSquared > 0 ? Math.Clamp(Vector.Multiply(position - from, line) / line.LengthSquared, 0, 1) : 0;
        var nearestOnLine = from + line * along;

        return (position - nearestOnLine).Length <= StripThickness(canvas) / 2 + HitDistance;
    }

    private static bool IsInsideKeyboard(SceneDeviceViewModel device, Point position, Rect canvas)
    {
        var (points, center) = device.CanvasPoints;
        var keySize = Math.Max(device.Placement.Size * canvas.Width / KeysPerRow * 0.8, 2);
        var body = new Rect(ToScreen(center, canvas), ToScreen(center, canvas));

        foreach (var pixel in points.OfType<EffectPixel>())
            body.Union(ToScreen(pixel, canvas));

        body.Inflate(keySize + HitDistance, keySize + HitDistance);

        return body.Contains(position);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (Scene == null)
            return;

        var canvas = CanvasRect();
        var position = e.GetPosition(this);
        var device = DeviceAt(position, canvas);

        Scene.SelectedDevice = device;
        InvalidateVisual();

        if (device == null)
            return;

        // Устройство тянется за ту точку, за которую его взяли
        var center = new Point(canvas.X + device.Placement.X * canvas.Width, canvas.Y + device.Placement.Y * canvas.Height);

        _dragged = device;
        _dragOffset = center - position;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragged == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var canvas = CanvasRect();
        var target = e.GetPosition(this) + _dragOffset;

        _dragged.MoveTo((target.X - canvas.X) / canvas.Width, (target.Y - canvas.Y) / canvas.Height);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragged == null)
            return;

        _dragged = null;
        ReleaseMouseCapture();

        Scene?.OnPlacementChanged();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // Без выбранного устройства колесо листает страницу
        if (Scene?.SelectedDevice is not { } device)
            return;

        // Новый размер сам пересчитывает превью и сохраняется
        device.SizePercent += Math.Sign(e.Delta) * WheelSizeStep;

        e.Handled = true;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

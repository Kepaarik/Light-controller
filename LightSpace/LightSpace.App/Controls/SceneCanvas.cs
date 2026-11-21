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
/// Холст сцены: устройства на своих местах с живыми цветами эффекта. Лента — линия точек, клавиатура — точки клавиш,
/// устройство одного цвета — круг. Устройство перетаскивают мышью, колесо над выбранным меняет его размер.
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

    // Насколько далеко от точки устройства щелчок ещё попадает в него, в пикселях
    private const double HitDistance = 14;

    private static readonly Brush CanvasFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)));
    private static readonly Pen CanvasPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x4C))), 1.5));
    private static readonly Pen StripPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x60, 0x8A, 0x8F, 0xA3))), 1));
    private static readonly Pen SelectedPen = Freeze(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Brush LabelText = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD8)));

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
        return new Point(canvas.X + pixel.X * canvas.Width, canvas.Y + pixel.Y * canvas.Width);
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
        var (points, center) = device.CanvasPoints;
        var colors = device.Colors;
        var centerPoint = ToScreen(center, canvas);

        // Устройство одного цвета — круг в своей середине
        if (points.Count == 0)
        {
            var radius = Math.Max(device.Placement.Size * canvas.Width / 6, 8);

            dc.DrawEllipse(BrushOf(colors, 0), null, centerPoint, radius, radius);

            if (device.IsSelected)
                dc.DrawEllipse(null, SelectedPen, centerPoint, radius + 4, radius + 4);

            DrawLabel(dc, device.Name, centerPoint + new Vector(0, radius + 12));
            return;
        }

        var screenPoints = points.Select(p => p is { } pixel ? ToScreen(pixel, canvas) : (Point?)null).ToArray();
        var placed = screenPoints.OfType<Point>().ToArray();

        if (placed.Length == 0)
            return;

        var dotRadius = Math.Clamp(canvas.Width / 160, 2, 5);

        // Лента — линия через свои светодиоды
        if (device.Own.IsLinear && placed.Length > 1)
            dc.DrawLine(StripPen, placed[0], placed[^1]);

        for (var i = 0; i < screenPoints.Length; i++)
        {
            if (screenPoints[i] is { } point)
                dc.DrawEllipse(BrushOf(colors, i), null, point, dotRadius, dotRadius);
        }

        var bounds = new Rect(placed[0], placed[0]);

        foreach (var point in placed)
            bounds.Union(point);

        bounds.Inflate(dotRadius + 4, dotRadius + 4);

        if (device.IsSelected)
            dc.DrawRoundedRectangle(null, SelectedPen, bounds, 4, 4);

        DrawLabel(dc, device.Name, new Point(centerPoint.X, bounds.Bottom + 10));
    }

    private static Brush BrushOf(IReadOnlyList<RgbColor> colors, int index)
    {
        if (index >= colors.Count)
            return Brushes.Black;

        var color = colors[index];

        return new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }

    private void DrawLabel(DrawingContext dc, string text, Point center)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"),
            11,
            LabelText,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    /// <summary>
    /// Устройство под точкой: ближайшее, у которого середина или хоть один светодиод рядом; null — мимо.
    /// </summary>
    private SceneDeviceViewModel? DeviceAt(Point position, Rect canvas)
    {
        if (Scene == null)
            return null;

        SceneDeviceViewModel? nearest = null;
        var nearestDistance = double.MaxValue;

        foreach (var device in Scene.Devices)
        {
            var (points, center) = device.CanvasPoints;
            var reach = points.Count == 0 ? Math.Max(device.Placement.Size * canvas.Width / 6, 8) + 4 : HitDistance;

            foreach (var pixel in points.OfType<EffectPixel>().Append(center))
            {
                var distance = (ToScreen(pixel, canvas) - position).Length;

                if (distance <= reach && distance < nearestDistance)
                {
                    nearest = device;
                    nearestDistance = distance;
                }
            }
        }

        return nearest;
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

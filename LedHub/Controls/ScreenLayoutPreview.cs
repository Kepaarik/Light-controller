using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LedHub.Core;
using LedHub.Services;

namespace LedHub.Controls;

/// <summary>
/// Схема ленты вокруг монитора в его пропорциях: точки светодиодов окрашены радугой от начала ленты к концу,
/// первый светодиод крупнее и подписан «1».
/// </summary>
public sealed class ScreenLayoutPreview : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(ScreenLayoutConfig), typeof(ScreenLayoutPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LedCountProperty = DependencyProperty.Register(
        nameof(LedCount), typeof(int), typeof(ScreenLayoutPreview),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Меняется при любой правке раскладки, чтобы схема перерисовалась.
    /// </summary>
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(ScreenLayoutPreview),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Отношение ширины монитора к высоте так, как он стоит на самом деле.
    /// </summary>
    public static readonly DependencyProperty AspectProperty = DependencyProperty.Register(
        nameof(Aspect), typeof(double), typeof(ScreenLayoutPreview),
        new FrameworkPropertyMetadata(16.0 / 9, FrameworkPropertyMetadataOptions.AffectsRender));

    // Поле вокруг нарисованного экрана под точки светодиодов
    private const double EdgeSpace = 22;

    private static readonly Brush ScreenFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)));
    private static readonly Pen ScreenPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x4C))), 1.5));
    private static readonly Brush MutedText = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0xA3)));

    public ScreenLayoutConfig? Layout
    {
        get => (ScreenLayoutConfig?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public int LedCount
    {
        get => (int)GetValue(LedCountProperty);
        set => SetValue(LedCountProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public double Aspect
    {
        get => (double)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Layout == null || LedCount <= 0)
            return;

        // Экран вписываем в контрол с полями под светодиоды по краям
        var width = Math.Min(ActualWidth - EdgeSpace * 2, (ActualHeight - EdgeSpace * 2) * Aspect);
        var height = width / Aspect;
        var screen = new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);

        // Схема показывает раскладку с той стороны, с какой её описали
        var label = Layout.IsViewedFromBack ? "монитор, вид сзади" : "монитор, вид спереди";

        dc.DrawRoundedRectangle(ScreenFill, ScreenPen, screen, 8, 8);
        DrawText(dc, label, new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2), 12, MutedText);

        var points = ScreenLayoutMath.Build(Layout, LedCount, Aspect);
        var dotRadius = Math.Clamp(Math.Min(width, height) / 70, 2, 5);
        Point? first = null;

        for (var i = 0; i < points.Length; i++)
        {
            if (points[i] is not { } point)
                continue;

            var position = Place(point, screen, dotRadius * 2.2);
            var color = RgbColor.FromHsv(300.0 * i / points.Length, 1, 1);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), null, position, dotRadius, dotRadius);

            if (i == 0)
                first = position;
        }

        // Начало ленты: крупная точка с номером, чтобы было видно, откуда считать
        if (first is { } start)
        {
            dc.DrawEllipse(null, new Pen(Brushes.White, 2), start, dotRadius * 2.2, dotRadius * 2.2);
            DrawText(dc, "1", start + new Vector(0, -dotRadius * 4.2), 11, Brushes.White);
        }
    }

    /// <summary>
    /// Переводит место светодиода на краю экрана в точку снаружи нарисованного экрана.
    /// </summary>
    private static Point Place(LedPoint point, Rect screen, double gap)
    {
        var x = screen.X + point.X * screen.Width;
        var y = screen.Y + point.Y * screen.Height;

        return point.Side switch
        {
            ScreenSide.Left => new Point(screen.Left - gap, y),
            ScreenSide.Right => new Point(screen.Right + gap, y),
            ScreenSide.Top => new Point(x, screen.Top - gap),
            _ => new Point(x, screen.Bottom + gap)
        };
    }

    private void DrawText(DrawingContext dc, string text, Point center, double size, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

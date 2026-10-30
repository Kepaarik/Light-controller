using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LedHub.Core;
using LedHub.Services.Scene;
using LedHub.ViewModels;

namespace LedHub.Controls;

/// <summary>
/// Схема сцены: мониторы и устройства на стене. Элементы перетаскиваются мышью, колесо поворачивает выбранный,
/// за концы ленты можно потянуть, чтобы изменить длину и наклон. Светодиоды показывают живые цвета эффекта.
/// </summary>
public sealed class SceneCanvas : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(SceneViewModel), typeof(SceneCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Меняется при каждой правке и каждом кадре эффекта, чтобы схема перерисовалась.
    /// </summary>
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(SceneCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Padding = 14;
    private const double GridStep = 20;
    private const double SnapStep = 5;
    private const double HandleRadius = 7;

    private static readonly Brush WorldFill = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen WorldPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Brush GridDot = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ScreenFill = Freeze(new LinearGradientBrush(Color.FromRgb(0x23, 0x26, 0x34), Color.FromRgb(0x12, 0x14, 0x1C), 90));
    private static readonly Pen ScreenPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x45, 0x4A, 0x5C))), 1.5));
    private static readonly Brush HexFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x26)));
    private static readonly Pen HexPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x42, 0x52))), 1.2));
    private static readonly Brush OffLed = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x31, 0x3E)));
    private static readonly Brush MutedText = Freeze(new SolidColorBrush(Color.FromRgb(0x9B, 0xA0, 0xB5)));
    private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xFF)));
    private static readonly Pen SelectionPen = Freeze(new Pen(AccentBrush, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen HandlePen = Freeze(new Pen(AccentBrush, 2.5));
    private static readonly Brush LayerFill = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0x8B, 0x7C, 0xFF)));
    private static readonly Pen LayerPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x8B, 0x7C, 0xFF))), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen SelectedLayerPen = Freeze(new Pen(AccentBrush, 2));

    // Перевод координат сцены в точки на экране, рассчитанный при последней отрисовке
    private double _scale = 1;
    private Point _origin;

    private SceneItemViewModel? _dragItem;
    private Vector _dragOffset;
    private bool? _dragFirstEnd;
    private bool _isDragging;

    // Слой, который двигают мышью или тянут за правый нижний угол
    private SceneLayerViewModel? _dragLayer;
    private bool _isResizingLayer;

    public SceneCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    public SceneViewModel? Scene
    {
        get => (SceneViewModel?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        UpdateTransform();

        var world = new Rect(ToView(0, 0), ToView(SceneViewModel.WorldWidth, SceneViewModel.WorldHeight));
        dc.DrawRoundedRectangle(WorldFill, WorldPen, world, 12, 12);

        for (var x = GridStep; x < SceneViewModel.WorldWidth; x += GridStep)
        {
            for (var y = GridStep; y < SceneViewModel.WorldHeight; y += GridStep)
                dc.DrawEllipse(GridDot, null, ToView(x, y), 1.1, 1.1);
        }

        if (Scene == null)
            return;

        foreach (var layer in Scene.Layers)
            DrawLayer(dc, layer);

        foreach (var item in Scene.Items)
        {
            if (item.Shape is not { } shape)
                continue;

            if (shape.Screen != null)
                DrawMonitor(dc, item, shape);
            else if (shape.Line != null)
                DrawStrip(dc, item, shape);
            else if (shape.Body != null)
                DrawKeyboard(dc, item, shape);
            else
                DrawPanel(dc, item, shape);
        }

        if (Scene.SelectedItem?.Shape is { } selected)
            DrawSelection(dc, Scene.SelectedItem, selected);

        if (Scene.SelectedLayer is { } selectedLayer)
            DrawLayerSelection(dc, selectedLayer);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();

        if (Scene == null)
            return;

        var position = e.GetPosition(this);

        // Сначала — ручки на концах выбранной ленты
        if (Scene.SelectedItem is { Shape.Line: { } line } selected)
        {
            if ((position - ToView(line.Start)).Length <= HandleRadius + 3)
            {
                BeginDrag(selected, true, default);
                return;
            }

            if ((position - ToView(line.End)).Length <= HandleRadius + 3)
            {
                BeginDrag(selected, false, default);
                return;
            }
        }

        // Затем — угол выбранного слоя: потяните, чтобы изменить его границы
        if (Scene.SelectedLayer is { } selectedLayer &&
            (position - ToView(selectedLayer.Config.X + selectedLayer.Config.Width, selectedLayer.Config.Y + selectedLayer.Config.Height)).Length <= HandleRadius + 3)
        {
            BeginLayerDrag(selectedLayer, true, default);
            return;
        }

        var hit = HitTest(position);
        var world = ToWorld(position);
        Scene.SelectedItem = hit;

        if (hit != null)
        {
            BeginDrag(hit, null, new Vector(hit.Config.X - world.X, hit.Config.Y - world.Y));
        }
        else
        {
            // Мимо элементов — выбираем верхний слой под мышью и двигаем его
            var layer = Scene.Layers.LastOrDefault(l => l.Config.Contains(new ScenePoint(world.X, world.Y)));
            Scene.SelectedLayer = layer;

            if (layer != null)
                BeginLayerDrag(layer, false, new Vector(layer.Config.X - world.X, layer.Config.Y - world.Y));
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        if (!_isDragging || (_dragItem == null && _dragLayer == null) || Scene == null)
        {
            Cursor = HitTest(position) != null ? Cursors.Hand : Cursors.Arrow;
            return;
        }

        var world = ToWorld(position);
        var snap = !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt);

        if (_dragLayer != null)
        {
            if (_isResizingLayer)
                Scene.ResizeLayer(_dragLayer, Snap(world.X, snap), Snap(world.Y, snap));
            else
                Scene.MoveLayer(_dragLayer, Snap(world.X + _dragOffset.X, snap), Snap(world.Y + _dragOffset.Y, snap));

            return;
        }

        if (_dragItem == null)
            return;

        if (_dragFirstEnd is { } isFirstEnd)
        {
            Scene.DragStripEnd(_dragItem, isFirstEnd, Snap(world.X, snap), Snap(world.Y, snap));
            return;
        }

        Scene.MoveItem(_dragItem, Snap(world.X + _dragOffset.X, snap), Snap(world.Y + _dragOffset.Y, snap));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        EndDrag();
        Scene?.CommitEdit();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_isDragging)
            Scene?.CommitEdit();

        EndDrag();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Колесо поворачивает выбранный элемент: шагом 15°, с Shift — 5°
        if (Scene?.SelectedItem is not { IsMonitor: false } item)
            return;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 15;
        Scene.RotateItem(item, e.Delta > 0 ? -step : step);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Scene?.SelectedLayer is { } layer)
        {
            Scene.RemoveLayerCommand.Execute(layer);
            e.Handled = true;
            return;
        }

        if (Scene?.SelectedItem is not { } item)
            return;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };

        if (e.Key == Key.Delete)
        {
            Scene.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (dx == 0 && dy == 0)
            return;

        Scene.MoveItem(item, item.Config.X + dx, item.Config.Y + dy);
        Scene.CommitEdit();
        e.Handled = true;
    }

    private void BeginDrag(SceneItemViewModel item, bool? firstEnd, Vector offset)
    {
        _dragItem = item;
        _dragFirstEnd = firstEnd;
        _dragOffset = offset;
        _isDragging = true;

        Cursor = firstEnd != null ? Cursors.Cross : Cursors.SizeAll;
        CaptureMouse();
    }

    private void BeginLayerDrag(SceneLayerViewModel layer, bool isResizing, Vector offset)
    {
        _dragLayer = layer;
        _isResizingLayer = isResizing;
        _dragOffset = offset;
        _isDragging = true;

        Cursor = isResizing ? Cursors.SizeNWSE : Cursors.SizeAll;
        CaptureMouse();
    }

    private void EndDrag()
    {
        _isDragging = false;
        _dragItem = null;
        _dragFirstEnd = null;
        _dragLayer = null;
        _isResizingLayer = false;

        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void DrawMonitor(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        var (topLeft, width, height) = shape.Screen!.Value;
        var screen = new Rect(ToView(topLeft), ToView(topLeft.X + width, topLeft.Y + height));

        // Обычная лента подсветки светится одним цветом вокруг всего экрана
        if (shape.DeviceId != null && shape.Leds.Count == 0)
        {
            var color = ToMedia(Scene!.SolidColor(shape));
            var glow = screen;
            glow.Inflate(4, 4);

            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)), 8), glow, 10, 10);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(color), 2), glow, 10, 10);
        }

        dc.DrawRoundedRectangle(ScreenFill, ScreenPen, screen, 6, 6);
        DrawText(dc, item.Title, new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2), 11, MutedText, screen.Width - 8);

        DrawLeds(dc, shape);
    }

    private void DrawStrip(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        var (start, end) = shape.Line!.Value;
        var from = ToView(start);
        var to = ToView(end);

        if (shape.Leds.Count == 0)
        {
            // Обычная лента — одна светящаяся линия
            var color = ToMedia(Scene!.SolidColor(shape));
            dc.DrawLine(RoundPen(Color.FromArgb(70, color.R, color.G, color.B), 12), from, to);
            dc.DrawLine(RoundPen(color, 5), from, to);
        }
        else
        {
            dc.DrawLine(RoundPen(Color.FromRgb(0x24, 0x27, 0x32), 8), from, to);
            DrawLeds(dc, shape);
        }

        // Начало ленты: маленькое кольцо у первого светодиода
        dc.DrawEllipse(null, new Pen(MutedText, 1.2), from, 5, 5);
        DrawLabel(dc, item, shape);
    }

    private void DrawPanel(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        var radius = shape.HexRadius * _scale;
        var isSolid = shape.Leds.Count == 0;
        Brush fill = HexFill;

        if (isSolid && shape.DeviceId != null)
        {
            var color = ToMedia(Scene!.SolidColor(shape));
            fill = new SolidColorBrush(Color.FromArgb(0xD0, color.R, color.G, color.B));
        }

        foreach (var hex in shape.Hexes)
            dc.DrawGeometry(fill, HexPen, HexGeometry(ToView(hex), radius * 0.95, shape.HexRotation));

        DrawLeds(dc, shape);
        DrawLabel(dc, item, shape);
    }

    private void DrawKeyboard(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        Brush body = HexFill;

        // Клавиатура не найдена — клавиш не знаем, корпус светится цветом устройства целиком
        if (shape.Keys.Count == 0 && shape.DeviceId != null)
        {
            var color = ToMedia(Scene!.SolidColor(shape));
            body = new SolidColorBrush(Color.FromArgb(0xD0, color.R, color.G, color.B));
        }

        dc.DrawGeometry(body, HexPen, PolygonGeometry(shape.Body!));

        foreach (var key in shape.Keys)
        {
            var color = Scene!.LedColor(shape, key.Led);
            var fill = color == new RgbColor(0, 0, 0) ? OffLed : new SolidColorBrush(ToMedia(color));

            dc.DrawGeometry(fill, null, PolygonGeometry(key.Corners));
        }

        DrawLabel(dc, item, shape);
    }

    private void DrawLeds(DrawingContext dc, SceneShape shape)
    {
        if (shape.Leds.Count == 0)
            return;

        var radius = Math.Clamp(_scale * 1.1, 1.6, 4);

        for (var i = 0; i < shape.Leds.Count; i++)
        {
            if (shape.Leds[i] is not { } led)
                continue;

            var center = ToView(led);
            var color = Scene!.LedColor(shape, i);

            if (color == new RgbColor(0, 0, 0))
            {
                dc.DrawEllipse(OffLed, null, center, radius, radius);
                continue;
            }

            var media = ToMedia(color);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, media.R, media.G, media.B)), null, center, radius * 2.2, radius * 2.2);
            dc.DrawEllipse(new SolidColorBrush(media), null, center, radius, radius);
        }
    }

    private void DrawLabel(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        var bottom = ToView(shape.Center.X, shape.Bounds.Max.Y);
        DrawText(dc, item.Title, new Point(bottom.X, bottom.Y + 12), 11, MutedText, 160);
    }

    private void DrawSelection(DrawingContext dc, SceneItemViewModel item, SceneShape shape)
    {
        var bounds = new Rect(ToView(shape.Bounds.Min), ToView(shape.Bounds.Max));
        bounds.Inflate(6, 6);
        dc.DrawRoundedRectangle(null, SelectionPen, bounds, 8, 8);

        if (shape.Line is not { } line)
            return;

        // Ручки на концах ленты: потяните, чтобы изменить длину и наклон
        foreach (var end in new[] { line.Start, line.End })
            dc.DrawEllipse(Brushes.White, HandlePen, ToView(end), HandleRadius, HandleRadius);

        DrawText(dc, "1", ToView(line.Start) + new Vector(0, -16), 11, Brushes.White, 40);
    }

    private void DrawLayer(DrawingContext dc, SceneLayerViewModel layer)
    {
        var bounds = LayerRect(layer);

        dc.DrawRoundedRectangle(LayerFill, LayerPen, bounds, 6, 6);
        DrawText(dc, layer.Title, new Point(bounds.X + bounds.Width / 2, bounds.Y + 10), 10, MutedText, bounds.Width - 8);
    }

    private void DrawLayerSelection(DrawingContext dc, SceneLayerViewModel layer)
    {
        var bounds = LayerRect(layer);

        // Ручка в правом нижнем углу: потяните, чтобы изменить границы слоя
        dc.DrawRoundedRectangle(null, SelectedLayerPen, bounds, 6, 6);
        dc.DrawEllipse(Brushes.White, HandlePen, bounds.BottomRight, HandleRadius, HandleRadius);
    }

    private Rect LayerRect(SceneLayerViewModel layer)
    {
        var config = layer.Config;
        return new Rect(ToView(config.X, config.Y), ToView(config.X + config.Width, config.Y + config.Height));
    }

    private SceneItemViewModel? HitTest(Point position)
    {
        if (Scene == null)
            return null;

        var world = ToWorld(position);
        var tolerance = 8 / _scale;

        // Сверху то, что нарисовано последним
        foreach (var item in Scene.Items.Reverse())
        {
            if (item.Shape is not { } shape)
                continue;

            if (shape.Screen is { } screen)
            {
                if (world.X >= screen.TopLeft.X - tolerance && world.X <= screen.TopLeft.X + screen.Width + tolerance &&
                    world.Y >= screen.TopLeft.Y - tolerance && world.Y <= screen.TopLeft.Y + screen.Height + tolerance)
                    return item;

                continue;
            }

            if (shape.Line is { } line)
            {
                if (DistanceToSegment(world, line.Start, line.End) <= tolerance)
                    return item;

                continue;
            }

            if (shape.Body is { } body)
            {
                if (IsInsidePolygon(world, body))
                    return item;

                continue;
            }

            if (shape.Hexes.Any(h => Math.Sqrt((h.X - world.X) * (h.X - world.X) + (h.Y - world.Y) * (h.Y - world.Y)) <= shape.HexRadius))
                return item;
        }

        return null;
    }

    private void UpdateTransform()
    {
        var width = Math.Max(1, ActualWidth - Padding * 2);
        var height = Math.Max(1, ActualHeight - Padding * 2);

        _scale = Math.Min(width / SceneViewModel.WorldWidth, height / SceneViewModel.WorldHeight);
        _origin = new Point(
            (ActualWidth - SceneViewModel.WorldWidth * _scale) / 2,
            (ActualHeight - SceneViewModel.WorldHeight * _scale) / 2);
    }

    private Point ToView(double x, double y)
    {
        return new Point(_origin.X + x * _scale, _origin.Y + y * _scale);
    }

    private Point ToView(ScenePoint point)
    {
        return ToView(point.X, point.Y);
    }

    private Point ToWorld(Point point)
    {
        return new Point((point.X - _origin.X) / _scale, (point.Y - _origin.Y) / _scale);
    }

    private static double Snap(double value, bool isEnabled)
    {
        return isEnabled ? Math.Round(value / SnapStep) * SnapStep : value;
    }

    private static double DistanceToSegment(Point point, ScenePoint start, ScenePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);

        var closestX = start.X + dx * t;
        var closestY = start.Y + dy * t;

        return Math.Sqrt((point.X - closestX) * (point.X - closestX) + (point.Y - closestY) * (point.Y - closestY));
    }

    /// <summary>
    /// Лежит ли точка внутри выпуклого многоугольника, углы которого перечислены по порядку обхода.
    /// </summary>
    private static bool IsInsidePolygon(Point point, IReadOnlyList<ScenePoint> corners)
    {
        var sign = 0;

        for (var i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);

            if (cross == 0)
                continue;

            var side = Math.Sign(cross);
            if (sign != 0 && side != sign)
                return false;

            sign = side;
        }

        return true;
    }

    private Geometry PolygonGeometry(IReadOnlyList<ScenePoint> corners)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(ToView(corners[0]), true, true);

            for (var i = 1; i < corners.Count; i++)
                context.LineTo(ToView(corners[i]), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry HexGeometry(Point center, double radius, double rotation)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            for (var i = 0; i < 6; i++)
            {
                // Вершины шестигранника «вершиной вверх», повёрнутые вместе с панелью
                var angle = (90 - 60 * i - rotation) * Math.PI / 180;
                var point = new Point(center.X + radius * Math.Cos(angle), center.Y - radius * Math.Sin(angle));

                if (i == 0)
                    context.BeginFigure(point, true, true);
                else
                    context.LineTo(point, true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Pen RoundPen(Color color, double thickness)
    {
        return new Pen(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    }

    private static Color ToMedia(RgbColor color)
    {
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private void DrawText(DrawingContext dc, string text, Point center, double size, Brush brush, double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };

        // При выравнивании по центру текст отсчитывается от середины заданной ширины
        dc.DrawText(formatted, new Point(center.X - formatted.MaxTextWidth / 2, center.Y - formatted.Height / 2));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

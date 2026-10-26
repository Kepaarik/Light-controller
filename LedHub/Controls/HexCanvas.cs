using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LedHub.Core;
using LedHub.Services;
using LedHub.ViewModels;

namespace LedHub.Controls;

/// <summary>
/// Редактор формы из шестигранников: в режиме формы клик добавляет или убирает шестигранник,
/// в режимах рисования левая кнопка красит светодиоды, правая берёт цвет пипеткой.
/// </summary>
public sealed class HexCanvas : FrameworkElement
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(HexLayoutConfig), typeof(HexCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(HexEditMode), typeof(HexCanvas),
        new FrameworkPropertyMetadata(HexEditMode.Shape, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushColorProperty = DependencyProperty.Register(
        nameof(BrushColor), typeof(RgbColor), typeof(HexCanvas),
        new FrameworkPropertyMetadata(RgbColor.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// Меняется, когда раскладку изменили снаружи, чтобы холст перерисовался.
    /// </summary>
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(HexCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Вызывается после каждой правки; параметр true — правка завершена и её стоит сохранить.
    /// </summary>
    public static readonly DependencyProperty ChangedCommandProperty = DependencyProperty.Register(
        nameof(ChangedCommand), typeof(ICommand), typeof(HexCanvas));

    private const double MaxHexRadius = 46;
    private const double LedRingRatio = 0.55;
    private const double LedRadiusRatio = 0.16;

    private static readonly double Sqrt3 = Math.Sqrt(3);
    private static readonly (int Q, int R)[] NeighborOffsets = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

    private static readonly Brush HexFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x24)));
    private static readonly Pen HexPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x4C))), 1.5));
    private static readonly Pen GhostPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4E, 0x5E))), 1.2) { DashStyle = DashStyles.Dash });
    private static readonly Brush GhostText = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x6F, 0x82)));
    private static readonly Brush NumberText = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush OffLed = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x38)));

    // Геометрия последней отрисовки: по ней мышь переводится в шестигранник
    private double _radius;
    private Point _origin;
    private bool _isPainting;

    public HexCanvas()
    {
        Cursor = Cursors.Hand;
    }

    public HexLayoutConfig? Layout
    {
        get => (HexLayoutConfig?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public HexEditMode Mode
    {
        get => (HexEditMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public RgbColor BrushColor
    {
        get => (RgbColor)GetValue(BrushColorProperty);
        set => SetValue(BrushColorProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public ICommand? ChangedCommand
    {
        get => (ICommand?)GetValue(ChangedCommandProperty);
        set => SetValue(ChangedCommandProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Прозрачный фон, чтобы клики по пустому месту тоже доходили до контрола
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (Layout == null)
            return;

        // Место под свободные ячейки учитывается всегда, чтобы форма не меняла масштаб при смене режима
        var ghosts = GhostPositions();
        UpdateGeometry(ghosts);

        if (Mode == HexEditMode.Shape)
        {
            foreach (var (q, r) in ghosts)
            {
                var center = CenterOf(q, r);
                dc.DrawGeometry(null, GhostPen, HexGeometry(center, _radius * 0.94));
                DrawText(dc, "+", center, _radius * 0.5, GhostText);
            }
        }

        for (var i = 0; i < Layout.Cells.Count; i++)
        {
            var cell = Layout.Cells[i];
            var center = CenterOf(cell.Q, cell.R);

            dc.DrawGeometry(HexFill, HexPen, HexGeometry(center, _radius * 0.94));

            for (var led = 0; led < HexCellConfig.LedCount; led++)
                DrawLed(dc, center + LedOffset(led), cell.Leds[led]);

            // В режиме формы показываем порядок шестигранников в цепочке
            if (Mode == HexEditMode.Shape)
                DrawText(dc, (i + 1).ToString(CultureInfo.InvariantCulture), center + new Vector(0, _radius * 0.8), _radius * 0.24, NumberText);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Layout == null)
            return;

        var position = e.GetPosition(this);

        if (Mode == HexEditMode.Shape)
        {
            ToggleHexAt(position);
            return;
        }

        _isPainting = true;
        CaptureMouse();
        PaintAt(position);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isPainting)
            PaintAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isPainting)
            return;

        _isPainting = false;
        ReleaseMouseCapture();
        ChangedCommand?.Execute(true);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _isPainting = false;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // Пипетка: правый клик по светодиоду делает его цвет текущим
        if (Mode == HexEditMode.Shape || FindLed(e.GetPosition(this)) is not { } hit)
            return;

        SetCurrentValue(BrushColorProperty, hit.Cell.Leds[hit.Led]);
        e.Handled = true;
    }

    private void ToggleHexAt(Point position)
    {
        var (q, r) = HexAt(position);
        var existing = FindCell(q, r);

        if (existing != null)
        {
            Layout!.Cells.Remove(existing);
        }
        else
        {
            // Добавлять можно только рядом с уже стоящими шестигранниками или первый — в центр
            var isFirst = Layout!.Cells.Count == 0 && (q, r) == (0, 0);
            if (!isFirst && !GhostPositions().Contains((q, r)))
                return;

            var cell = new HexCellConfig { Q = q, R = r };
            Array.Fill(cell.Leds, BrushColor);
            Layout.Cells.Add(cell);
        }

        InvalidateVisual();
        ChangedCommand?.Execute(true);
    }

    private void PaintAt(Point position)
    {
        if (FindLed(position) is not { } hit)
            return;

        var changed = false;

        if (Mode == HexEditMode.PaintHex)
        {
            for (var i = 0; i < HexCellConfig.LedCount; i++)
            {
                if (hit.Cell.Leds[i] == BrushColor)
                    continue;

                hit.Cell.Leds[i] = BrushColor;
                changed = true;
            }
        }
        else if (hit.Cell.Leds[hit.Led] != BrushColor)
        {
            hit.Cell.Leds[hit.Led] = BrushColor;
            changed = true;
        }

        if (!changed)
            return;

        InvalidateVisual();
        ChangedCommand?.Execute(false);
    }

    /// <summary>
    /// Шестигранник под точкой и ближайший к точке светодиод в нём.
    /// </summary>
    private (HexCellConfig Cell, int Led)? FindLed(Point position)
    {
        var (q, r) = HexAt(position);
        var cell = FindCell(q, r);
        if (cell == null)
            return null;

        var local = position - CenterOf(q, r);
        var nearest = Enumerable.Range(0, HexCellConfig.LedCount).MinBy(i => (local - LedOffset(i)).LengthSquared);

        return (cell, nearest);
    }

    private HexCellConfig? FindCell(int q, int r)
    {
        return Layout?.Cells.FirstOrDefault(c => c.Q == q && c.R == r);
    }

    /// <summary>
    /// Свободные места рядом с уже стоящими шестигранниками; для пустой формы — одно место в центре.
    /// </summary>
    private List<(int Q, int R)> GhostPositions()
    {
        var cells = Layout?.Cells ?? new List<HexCellConfig>();
        if (cells.Count == 0)
            return new List<(int Q, int R)> { (0, 0) };

        var occupied = cells.Select(c => (c.Q, c.R)).ToHashSet();

        return cells
            .SelectMany(c => NeighborOffsets.Select(o => (Q: c.Q + o.Q, R: c.R + o.R)))
            .Where(p => !occupied.Contains(p))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Подбирает размер шестигранника и смещение так, чтобы вся форма поместилась и стояла по центру.
    /// </summary>
    private void UpdateGeometry(List<(int Q, int R)> ghosts)
    {
        var positions = Layout!.Cells.Select(c => (c.Q, c.R)).Concat(ghosts).ToList();
        if (positions.Count == 0)
            positions.Add((0, 0));

        // Координаты центров для шестигранника единичного радиуса (вершиной вверх)
        var xs = positions.Select(p => Sqrt3 * (p.Item1 + p.Item2 / 2.0)).ToList();
        var ys = positions.Select(p => 1.5 * p.Item2).ToList();

        var width = xs.Max() - xs.Min() + Sqrt3;
        var height = ys.Max() - ys.Min() + 2;
        const double padding = 16;

        _radius = Math.Min(MaxHexRadius, Math.Min((ActualWidth - padding) / width, (ActualHeight - padding) / height));
        _radius = Math.Max(_radius, 8);

        var centerX = (xs.Max() + xs.Min()) / 2;
        var centerY = (ys.Max() + ys.Min()) / 2;
        _origin = new Point(ActualWidth / 2 - centerX * _radius, ActualHeight / 2 - centerY * _radius);
    }

    private Point CenterOf(int q, int r)
    {
        return new Point(_origin.X + _radius * Sqrt3 * (q + r / 2.0), _origin.Y + _radius * 1.5 * r);
    }

    /// <summary>
    /// Переводит точку экрана в осевые координаты шестигранника с округлением в кубических координатах.
    /// </summary>
    private (int Q, int R) HexAt(Point position)
    {
        var x = (position.X - _origin.X) / _radius;
        var y = (position.Y - _origin.Y) / _radius;

        var q = Sqrt3 / 3 * x - y / 3;
        var r = 2.0 / 3 * y;
        var s = -q - r;

        var rq = Math.Round(q);
        var rr = Math.Round(r);
        var rs = Math.Round(s);

        var dq = Math.Abs(rq - q);
        var dr = Math.Abs(rr - r);
        var ds = Math.Abs(rs - s);

        if (dq > dr && dq > ds)
            rq = -rr - rs;
        else if (dr > ds)
            rr = -rq - rs;

        return ((int)rq, (int)rr);
    }

    /// <summary>
    /// Смещение светодиода от центра: 0 — центр, 1–6 — по часовой стрелке, начиная с верхнего.
    /// </summary>
    private Vector LedOffset(int index)
    {
        if (index == 0)
            return new Vector(0, 0);

        var angle = (90 - 60 * (index - 1)) * Math.PI / 180;

        return new Vector(Math.Cos(angle), -Math.Sin(angle)) * _radius * LedRingRatio;
    }

    private void DrawLed(DrawingContext dc, Point center, RgbColor color)
    {
        var radius = _radius * LedRadiusRatio;

        if (color == new RgbColor(0, 0, 0))
        {
            dc.DrawEllipse(OffLed, null, center, radius, radius);
            return;
        }

        var media = Color.FromRgb(color.R, color.G, color.B);

        // Мягкое свечение вокруг светодиода, чтобы рисунок выглядел как на стене
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, media.R, media.G, media.B)), null, center, radius * 1.9, radius * 1.9);
        dc.DrawEllipse(new SolidColorBrush(media), null, center, radius, radius);
    }

    private void DrawText(DrawingContext dc, string text, Point center, double size, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text, Segoe UI"),
            Math.Max(size, 8),
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private static Geometry HexGeometry(Point center, double radius)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            for (var i = 0; i < 6; i++)
            {
                // Вершины шестигранника «вершиной вверх»: 90°, 30°, −30°…
                var angle = (90 - 60 * i) * Math.PI / 180;
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

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

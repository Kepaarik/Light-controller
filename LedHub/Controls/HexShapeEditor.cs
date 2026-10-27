using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LedHub.ViewModels;

namespace LedHub.Controls;

/// <summary>
/// Сетка для сборки формы панели на сцене: щелчок по пустому месту добавляет шестигранник, по занятому — убирает.
/// </summary>
public sealed class HexShapeEditor : FrameworkElement
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(SceneItemViewModel), typeof(HexShapeEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemChanged));

    // Сетка всегда немного шире формы, чтобы было куда добавлять
    private const int MinGridRadius = 3;

    private static readonly double Sqrt3 = Math.Sqrt(3);

    private static readonly Brush CellFill = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x7C, 0xFF)));
    private static readonly Pen CellPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xB4, 0xAA, 0xFF))), 1.2));
    private static readonly Pen EmptyPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen AddHoverPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x7C, 0x6C, 0xFF))), 2));
    private static readonly Pen RemoveHoverPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x7A))), 2));

    private Point _origin;
    private double _radius;
    private (int Q, int R)? _hover;

    public HexShapeEditor()
    {
        Cursor = Cursors.Hand;
    }

    public SceneItemViewModel? Item
    {
        get => (SceneItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (Item is not { } item)
            return;

        var cells = item.ShapeCells.ToHashSet();
        var gridRadius = GridRadius(cells);

        UpdateGeometry(gridRadius);

        foreach (var (q, r) in GridCells(gridRadius))
        {
            var filled = cells.Contains((q, r));
            dc.DrawGeometry(filled ? CellFill : null, filled ? CellPen : EmptyPen, HexGeometry(CenterOf(q, r), _radius * 0.9));
        }

        if (_hover is { } hover && Distance(hover) <= gridRadius)
        {
            var pen = cells.Contains(hover) ? RemoveHoverPen : AddHoverPen;
            dc.DrawGeometry(null, pen, HexGeometry(CenterOf(hover.Q, hover.R), _radius * 0.9));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Item is not { } item || _radius <= 0)
            return;

        var cell = HexAt(e.GetPosition(this));
        if (Distance(cell) > GridRadius(item.ShapeCells.ToHashSet()))
            return;

        item.ToggleCell(cell.Q, cell.R);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_radius <= 0)
            return;

        var cell = HexAt(e.GetPosition(this));
        if (_hover == cell)
            return;

        _hover = cell;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (HexShapeEditor)d;

        if (e.OldValue is SceneItemViewModel oldItem)
            oldItem.PropertyChanged -= editor.OnItemPropertyChanged;

        if (e.NewValue is SceneItemViewModel newItem)
            newItem.PropertyChanged += editor.OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private static int GridRadius(HashSet<(int Q, int R)> cells)
    {
        return Math.Max(MinGridRadius, cells.Max(Distance) + 1);
    }

    private static int Distance((int Q, int R) cell)
    {
        return (Math.Abs(cell.Q) + Math.Abs(cell.R) + Math.Abs(cell.Q + cell.R)) / 2;
    }

    private static IEnumerable<(int Q, int R)> GridCells(int radius)
    {
        for (var q = -radius; q <= radius; q++)
        {
            for (var r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
                yield return (q, r);
        }
    }

    /// <summary>
    /// Подбирает размер шестигранника так, чтобы вся сетка поместилась в элемент, и ставит её по центру.
    /// </summary>
    private void UpdateGeometry(int gridRadius)
    {
        // Ширина сетки — (2·радиус + 1)·√3 радиусов шестигранника, высота — (3·радиус + 2)
        var byWidth = ActualWidth / ((2 * gridRadius + 1) * Sqrt3);
        var byHeight = ActualHeight / (3 * gridRadius + 2);

        _radius = Math.Max(0, Math.Min(byWidth, byHeight));
        _origin = new Point(ActualWidth / 2, ActualHeight / 2);
    }

    private Point CenterOf(int q, int r)
    {
        return new Point(_origin.X + _radius * Sqrt3 * (q + r / 2.0), _origin.Y + _radius * 1.5 * r);
    }

    /// <summary>
    /// Переводит точку элемента в осевые координаты шестигранника с округлением в кубических координатах.
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

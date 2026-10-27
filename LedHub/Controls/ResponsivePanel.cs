using System.Windows;
using System.Windows.Controls;

namespace LedHub.Controls;

/// <summary>
/// Раскладывает элементы в ряд, когда хватает ширины, и столбиком — на узком окне.
/// В ряду элементы с Fill="True" делят оставшееся место, остальные берут сколько им нужно.
/// </summary>
public sealed class ResponsivePanel : Panel
{
    public static readonly DependencyProperty BreakWidthProperty = DependencyProperty.Register(
        nameof(BreakWidth), typeof(double), typeof(ResponsivePanel),
        new FrameworkPropertyMetadata(720.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ResponsivePanel),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill", typeof(bool), typeof(ResponsivePanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>
    /// Ширина, начиная с которой элементы встают в ряд.
    /// </summary>
    public double BreakWidth
    {
        get => (double)GetValue(BreakWidthProperty);
        set => SetValue(BreakWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static bool GetFill(DependencyObject element) => (bool)element.GetValue(FillProperty);

    public static void SetFill(DependencyObject element, bool value) => element.SetValue(FillProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = VisibleChildren();
        if (children.Count == 0)
            return new Size();

        if (!IsWide(availableSize.Width))
        {
            double height = 0, width = 0;

            foreach (var child in children)
            {
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                height += child.DesiredSize.Height;
                width = Math.Max(width, child.DesiredSize.Width);
            }

            return new Size(width, height + Spacing * (children.Count - 1));
        }

        var widths = RowWidths(children, availableSize.Width, measure: true);
        var rowHeight = children.Max(c => c.DesiredSize.Height);

        return new Size(widths.Sum() + Spacing * (children.Count - 1), rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren();
        if (children.Count == 0)
            return finalSize;

        if (!IsWide(finalSize.Width))
        {
            double y = 0;

            foreach (var child in children)
            {
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height + Spacing;
            }

            return finalSize;
        }

        var widths = RowWidths(children, finalSize.Width, measure: false);
        double x = 0;

        for (var i = 0; i < children.Count; i++)
        {
            children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i] + Spacing;
        }

        return finalSize;
    }

    private bool IsWide(double width)
    {
        return double.IsInfinity(width) || width >= BreakWidth;
    }

    /// <summary>
    /// Ширина каждого элемента в ряду: обычные — по желанию, растягиваемые делят остаток поровну.
    /// </summary>
    private double[] RowWidths(List<UIElement> children, double available, bool measure)
    {
        var widths = new double[children.Count];
        var fillCount = children.Count(GetFill);
        var used = Spacing * (children.Count - 1);

        for (var i = 0; i < children.Count; i++)
        {
            if (GetFill(children[i]))
                continue;

            if (measure)
                children[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            widths[i] = children[i].DesiredSize.Width;
            used += widths[i];
        }

        var share = fillCount == 0 || double.IsInfinity(available) ? 0 : Math.Max(0, (available - used) / fillCount);

        for (var i = 0; i < children.Count; i++)
        {
            if (!GetFill(children[i]))
                continue;

            if (measure)
                children[i].Measure(new Size(double.IsInfinity(available) ? double.PositiveInfinity : share, double.PositiveInfinity));

            widths[i] = double.IsInfinity(available) ? children[i].DesiredSize.Width : share;
        }

        return widths;
    }

    private List<UIElement> VisibleChildren()
    {
        return InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
    }
}

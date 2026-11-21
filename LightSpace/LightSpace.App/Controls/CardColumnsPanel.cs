using System.Windows;
using System.Windows.Controls;

namespace LightSpace.App.Controls;

/// <summary>
/// Карточки колонками: колонок столько, сколько их влезает по ширине, если колонка не уже MinColumnWidth. Колонки
/// одной ширины и вместе занимают всю ширину. Карточка с номером i стоит в колонке i по модулю числа колонок, поэтому
/// карточки не перескакивают из колонки в колонку, когда одна из них меняет высоту.
/// </summary>
public sealed class CardColumnsPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(CardColumnsPanel),
        new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardColumnsPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Самая узкая колонка: уже этого колонки не становятся — их становится меньше.
    /// </summary>
    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>
    /// Промежуток между колонками и между карточками в колонке.
    /// </summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnsFor(availableSize.Width);
        var width = ColumnWidth(availableSize.Width, columns);
        var heights = new double[columns];
        var counts = new int[columns];
        var index = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width, double.PositiveInfinity));

            if (child.Visibility == Visibility.Collapsed)
                continue;

            var column = index % columns;

            heights[column] += (counts[column] > 0 ? Gap : 0) + child.DesiredSize.Height;
            counts[column]++;
            index++;
        }

        var desiredWidth = double.IsInfinity(availableSize.Width) ? width * columns + Gap * (columns - 1) : availableSize.Width;

        return new Size(desiredWidth, heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var width = ColumnWidth(finalSize.Width, columns);
        var tops = new double[columns];
        var counts = new int[columns];
        var index = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var column = index % columns;

            if (counts[column] > 0)
                tops[column] += Gap;

            child.Arrange(new Rect(column * (width + Gap), tops[column], width, child.DesiredSize.Height));

            tops[column] += child.DesiredSize.Height;
            counts[column]++;
            index++;
        }

        return finalSize;
    }

    private int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || width <= 0)
            return 1;

        return Math.Max(1, (int)((width + Gap) / (MinColumnWidth + Gap)));
    }

    private double ColumnWidth(double width, int columns)
    {
        if (double.IsInfinity(width))
            return MinColumnWidth;

        return Math.Max(0, (width - Gap * (columns - 1)) / columns);
    }
}

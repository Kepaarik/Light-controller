using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightSpace.App.Controls;

namespace LightSpace.Tests;

public sealed class CardColumnsPanelTests
{
    [Fact]
    public void ColumnCountFollowsWidth()
    {
        UiThread.Invoke(() =>
        {
            var panel = PanelWithCards(100, 50, 80, 30, 60);

            // 400 px при колонке не уже 120 и промежутке 16: влезают три колонки по 122,67 px
            Layout(panel, 400);

            Assert.Equal(new[] { 0.0, 138.67, 277.33, 0.0, 138.67 }, Lefts(panel));
            Assert.Equal(new[] { 0.0, 0.0, 0.0, 116.0, 66.0 }, Tops(panel));
            Assert.Equal(146.0, panel.DesiredSize.Height);

            // Уже — две колонки, карточка i стоит в колонке i по модулю двух
            Layout(panel, 300);

            Assert.Equal(new[] { 0.0, 158.0, 0.0, 158.0, 0.0 }, Lefts(panel));
            Assert.Equal(new[] { 0.0, 0.0, 116.0, 66.0, 212.0 }, Tops(panel));

            // Совсем узко — одна колонка на всю ширину
            Layout(panel, 100);

            Assert.All(Lefts(panel), left => Assert.Equal(0.0, left));
            Assert.Equal(100.0, panel.Children[0].RenderSize.Width);
        });
    }

    [Fact]
    public void CollapsedCardTakesNoPlace()
    {
        UiThread.Invoke(() =>
        {
            var panel = PanelWithCards(100, 50, 80);

            panel.Children[0].Visibility = Visibility.Collapsed;
            Layout(panel, 300);

            Assert.Equal(new[] { 0.0, 158.0 }, Lefts(panel).Skip(1));
            Assert.Equal(new[] { 0.0, 0.0 }, Tops(panel).Skip(1));
        });
    }

    private static CardColumnsPanel PanelWithCards(params double[] heights)
    {
        var panel = new CardColumnsPanel { MinColumnWidth = 120, Gap = 16 };

        foreach (var height in heights)
            panel.Children.Add(new Border { Height = height });

        return panel;
    }

    private static void Layout(CardColumnsPanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }

    private static double[] Lefts(Panel panel)
    {
        return panel.Children.Cast<UIElement>().Select(c => Math.Round(VisualTreeHelper.GetOffset(c).X, 2)).ToArray();
    }

    private static double[] Tops(Panel panel)
    {
        return panel.Children.Cast<UIElement>().Select(c => Math.Round(VisualTreeHelper.GetOffset(c).Y, 2)).ToArray();
    }
}

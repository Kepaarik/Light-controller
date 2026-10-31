using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LightSpace.Core;

namespace LightSpace.App.Controls;

/// <summary>
/// Выбор цвета в одну строку: образец и код цвета, по клику — карточка с кругом цветов.
/// Подходит, когда цветов на экране несколько и полный круг для каждого не помещается.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(RgbColor), typeof(ColorPicker),
        new FrameworkPropertyMetadata(RgbColor.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public ColorPicker()
    {
        InitializeComponent();

        // Круг в карточке и сам выбор цвета — одно значение
        Wheel.SetBinding(ColorWheel.SelectedColorProperty, new Binding(nameof(SelectedColor)) { Source = this, Mode = BindingMode.TwoWay });
        UpdateSample();
    }

    public RgbColor SelectedColor
    {
        get => (RgbColor)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ColorPicker)d).UpdateSample();
    }

    private void UpdateSample()
    {
        var color = SelectedColor;
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();

        Swatch.Background = brush;
        HexText.Text = color.ToHex();
    }
}

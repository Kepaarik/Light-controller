using System.Windows;
using System.Windows.Controls;
using LightSpace.Devices;

namespace LightSpace.App.Controls;

/// <summary>
/// Раскладка клавиатуры: клавиши с подсветкой на своих местах и в своих размерах, с надписями.
/// </summary>
public sealed class KeyboardLayout : Canvas
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(IReadOnlyList<KeyboardKey>), typeof(KeyboardLayout), new PropertyMetadata(null, OnKeysChanged));

    /// <summary>
    /// Клавиши в размерах стандартной клавиши; null или пусто — раскладки нет.
    /// </summary>
    public IReadOnlyList<KeyboardKey>? Keys
    {
        get => (IReadOnlyList<KeyboardKey>?)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    private static void OnKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((KeyboardLayout)d).Rebuild();
    }

    /// <summary>
    /// Пересобирает клавиши; холст получает размер всей клавиатуры, потому что сам Canvas по детям не меряется.
    /// </summary>
    private void Rebuild()
    {
        Children.Clear();

        var unit = (double)FindResource("KeyUnitSize");
        var gap = (double)FindResource("KeyGap");
        var width = 0.0;
        var height = 0.0;

        foreach (var key in Keys ?? Array.Empty<KeyboardKey>())
        {
            var label = new TextBlock
            {
                Text = key.Label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeCaption");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var cap = new Border
            {
                Width = key.Width * unit - gap,
                Height = key.Height * unit - gap,
                Child = label
            };

            cap.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
            cap.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
            cap.SetResourceReference(Border.BorderThicknessProperty, "StrokeThin");
            cap.SetResourceReference(Border.CornerRadiusProperty, "RadiusSmall");

            SetLeft(cap, key.X * unit);
            SetTop(cap, key.Y * unit);
            Children.Add(cap);

            width = Math.Max(width, (key.X + key.Width) * unit);
            height = Math.Max(height, (key.Y + key.Height) * unit);
        }

        Width = width;
        Height = height;
    }
}

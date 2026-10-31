using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightSpace.Core;
using LightSpace.Devices;

namespace LightSpace.App.Controls;

/// <summary>
/// Раскладка клавиатуры: клавиши с подсветкой на своих местах и в своих размерах, с надписями.
/// Если заданы цвета светодиодов, клавиши закрашены своими живыми цветами.
/// </summary>
public sealed class KeyboardLayout : Canvas
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(IReadOnlyList<KeyboardKey>), typeof(KeyboardLayout), new PropertyMetadata(null, OnKeysChanged));

    public static readonly DependencyProperty ColorsProperty = DependencyProperty.Register(
        nameof(Colors), typeof(IReadOnlyList<RgbColor>), typeof(KeyboardLayout), new PropertyMetadata(null, OnColorsChanged));

    // Клавиши и их колпачки в том порядке, в каком они на холсте
    private readonly List<(KeyboardKey Key, Border Cap)> _caps = new();

    /// <summary>
    /// Клавиши в размерах стандартной клавиши; null или пусто — раскладки нет.
    /// </summary>
    public IReadOnlyList<KeyboardKey>? Keys
    {
        get => (IReadOnlyList<KeyboardKey>?)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    /// <summary>
    /// Цвета светодиодов по их номерам; null — клавиши обычного цвета.
    /// </summary>
    public IReadOnlyList<RgbColor>? Colors
    {
        get => (IReadOnlyList<RgbColor>?)GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    private static void OnKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((KeyboardLayout)d).Rebuild();
    }

    private static void OnColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((KeyboardLayout)d).Paint();
    }

    /// <summary>
    /// Закрашивает клавиши цветами их светодиодов; без цветов — возвращает обычный цвет клавиш.
    /// </summary>
    private void Paint()
    {
        var colors = Colors;

        foreach (var (key, cap) in _caps)
        {
            if (colors == null || key.Led < 0 || key.Led >= colors.Count)
            {
                cap.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
                continue;
            }

            var color = colors[key.Led];
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();

            cap.Background = brush;
        }
    }

    /// <summary>
    /// Пересобирает клавиши; холст получает размер всей клавиатуры, потому что сам Canvas по детям не меряется.
    /// </summary>
    private void Rebuild()
    {
        Children.Clear();
        _caps.Clear();

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
            _caps.Add((key, cap));

            width = Math.Max(width, (key.X + key.Width) * unit);
            height = Math.Max(height, (key.Y + key.Height) * unit);
        }

        Width = width;
        Height = height;

        // Новые клавиши получают цвета, заданные ещё до них
        Paint();
    }
}

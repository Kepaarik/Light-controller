using System.Windows;
using System.Windows.Media;

namespace LightSpace.App.Controls;

/// <summary>
/// Дополнительные свойства оформления для шаблонов дизайн-системы: кисти состояний, скругление, значок
/// и принудительные состояния наведения и нажатия для витрины компонентов.
/// </summary>
public static class Appearance
{
    /// <summary>
    /// Фон элемента под курсором.
    /// </summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(Brush), typeof(Appearance), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Фон нажатого элемента.
    /// </summary>
    public static readonly DependencyProperty PressedBrushProperty = DependencyProperty.RegisterAttached(
        "PressedBrush", typeof(Brush), typeof(Appearance), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Скругление углов элемента.
    /// </summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Appearance), new FrameworkPropertyMetadata(default(CornerRadius)));

    /// <summary>
    /// Символ значка из шрифта Segoe Fluent Icons, который элемент показывает рядом с подписью.
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Appearance), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Элемент выглядит так, будто курсор над ним.
    /// </summary>
    public static readonly DependencyProperty ForceHoverProperty = DependencyProperty.RegisterAttached(
        "ForceHover", typeof(bool), typeof(Appearance), new FrameworkPropertyMetadata(false));

    /// <summary>
    /// Элемент выглядит нажатым.
    /// </summary>
    public static readonly DependencyProperty ForcePressedProperty = DependencyProperty.RegisterAttached(
        "ForcePressed", typeof(bool), typeof(Appearance), new FrameworkPropertyMetadata(false));

    public static Brush? GetHoverBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBrushProperty);

    public static void SetHoverBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBrushProperty, value);

    public static Brush? GetPressedBrush(DependencyObject element) => (Brush?)element.GetValue(PressedBrushProperty);

    public static void SetPressedBrush(DependencyObject element, Brush? value) => element.SetValue(PressedBrushProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static bool GetForceHover(DependencyObject element) => (bool)element.GetValue(ForceHoverProperty);

    public static void SetForceHover(DependencyObject element, bool value) => element.SetValue(ForceHoverProperty, value);

    public static bool GetForcePressed(DependencyObject element) => (bool)element.GetValue(ForcePressedProperty);

    public static void SetForcePressed(DependencyObject element, bool value) => element.SetValue(ForcePressedProperty, value);
}

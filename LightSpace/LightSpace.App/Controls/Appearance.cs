using System.Windows;
using System.Windows.Media;

namespace LightSpace.App.Controls;

/// <summary>
/// Дополнительные свойства оформления для шаблонов дизайн-системы: фон под курсором, скругление, значок,
/// выбранное и перетаскиваемое состояние.
/// </summary>
public static class Appearance
{
    /// <summary>
    /// Фон элемента под курсором.
    /// </summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(Brush), typeof(Appearance), new FrameworkPropertyMetadata(null));

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
    /// Элемент выбран, например устройство или группа на сцене.
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(Appearance), new FrameworkPropertyMetadata(false));

    /// <summary>
    /// Элемент можно перетащить мышью, например устройство на сцене в режиме редактирования расстановки.
    /// </summary>
    public static readonly DependencyProperty IsMovableProperty = DependencyProperty.RegisterAttached(
        "IsMovable", typeof(bool), typeof(Appearance), new FrameworkPropertyMetadata(false));

    public static Brush? GetHoverBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBrushProperty);

    public static void SetHoverBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBrushProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);

    public static bool GetIsMovable(DependencyObject element) => (bool)element.GetValue(IsMovableProperty);

    public static void SetIsMovable(DependencyObject element, bool value) => element.SetValue(IsMovableProperty, value);
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LightSpace.App.Controls;

/// <summary>
/// Плавная смена страниц: страница, которая становится видимой, проявляется и чуть поднимается на своё место.
/// </summary>
public static class PageTransition
{
    // На сколько пикселей ниже своего места страница начинает появляться
    private const double Rise = 8;

    /// <summary>
    /// Страница плавно появляется каждый раз, когда становится видимой.
    /// </summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PageTransition), new FrameworkPropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.IsVisibleChanged -= OnIsVisibleChanged;

        if ((bool)e.NewValue)
            element.IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || !(bool)e.NewValue)
            return;

        var duration = (Duration)element.FindResource("MotionNormal");
        var easing = (IEasingFunction)element.FindResource("EaseOut");

        // Сдвиг заводится один раз: исходный RenderTransform у страницы — общий замороженный, его не анимировать
        if (element.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            element.RenderTransform = shift;
        }

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Rise, 0, duration) { EasingFunction = easing });
    }
}

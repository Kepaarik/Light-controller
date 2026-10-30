using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LightSpace.App.Controls;

/// <summary>
/// Обратное логическое значение: true ↔ false.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }
}

/// <summary>
/// Показывает элемент, только когда значение равно параметру: страница видна, когда выбрана её вкладка.
/// Параметр — имя значения перечисления.
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public static readonly EqualsToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var matches = value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Показывает элемент, когда значение true; иначе убирает его из раскладки.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public static readonly BooleanToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

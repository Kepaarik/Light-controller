using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LedHub.Controls;

/// <summary>
/// true — скрыть, false — показать: для элементов, которые видны, пока условие не выполнено.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

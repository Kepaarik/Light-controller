using System.Windows.Controls;
using System.Windows.Input;
using LightSpace.App.ViewModels;

namespace LightSpace.App.Views;

/// <summary>
/// Страница «Настройки»: подсветка, частота кадров, переходы, автозапуск и трей.
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnTitleClicked(object sender, MouseButtonEventArgs e)
    {
        (DataContext as SettingsViewModel)?.Log.OnTitleClicked();
    }

    /// <summary>
    /// Журнал прокручивается к последней строке.
    /// </summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        ((TextBox)sender).ScrollToEnd();
    }
}

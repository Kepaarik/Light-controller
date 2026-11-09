using System.Windows;
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
    /// Сохраняет то, что сейчас играет, профилем с введённым названием.
    /// </summary>
    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main)
            return;

        main.SaveProfile(ProfileName.Text);
        ProfileName.Text = "";
    }

    private void OnProfileNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSaveProfile(sender, e);
    }

    /// <summary>
    /// Журнал прокручивается к последней строке.
    /// </summary>
    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        ((TextBox)sender).ScrollToEnd();
    }
}

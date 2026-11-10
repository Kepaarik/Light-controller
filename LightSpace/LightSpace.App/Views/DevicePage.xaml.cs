using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightSpace.App.ViewModels;

namespace LightSpace.App.Views;

/// <summary>
/// Страница устройства из левого меню: его название и значок, яркость, монитор ленты и эффект.
/// </summary>
public partial class DevicePage : UserControl
{
    public DevicePage()
    {
        InitializeComponent();
    }

    // Устройство, чья страница открыта
    private SceneDeviceViewModel? OpenedDevice => (DataContext as SceneViewModel)?.SelectedGroup?.Device;

    private MainViewModel? Main => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void OnIconSelected(object sender, SelectionChangedEventArgs e)
    {
        // Выбор приходит и от привязки, когда страница показывает значок устройства, — его не сохраняем
        if (IconList.SelectedItem is not string icon || OpenedDevice is not { } device || icon == device.Icon)
            return;

        IconOpener.IsChecked = false;
        Main?.SetDeviceIcon(device.Id, icon);
    }

    private void OnPowerClicked(object sender, RoutedEventArgs e)
    {
        if (OpenedDevice is { } device)
            Main?.SetDeviceOn(device.Id, PowerSwitch.IsChecked == true);
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (OpenedDevice is not { } device)
            return;

        NameBox.Text = device.Name;
        ShowNameBox(true);

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ShowNameBox(false);
            e.Handled = true;
        }
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitName();
    }

    /// <summary>
    /// Сохраняет название из поля, если поле ещё открыто.
    /// </summary>
    private void CommitName()
    {
        if (NameBox.Visibility != Visibility.Visible)
            return;

        ShowNameBox(false);

        if (OpenedDevice is { } device)
            Main?.RenameDevice(device.Id, NameBox.Text);
    }

    private void ShowNameBox(bool isShown)
    {
        NameBox.Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = isShown ? Visibility.Hidden : Visibility.Visible;
        RenameButton.Visibility = isShown ? Visibility.Collapsed : Visibility.Visible;
    }
}

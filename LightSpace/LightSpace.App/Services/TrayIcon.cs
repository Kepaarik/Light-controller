using System.ComponentModel;
using System.Windows;
using LightSpace.App.ViewModels;
using Forms = System.Windows.Forms;

namespace LightSpace.App.Services;

/// <summary>
/// Значок Light Space в области уведомлений: левый клик открывает окно, правый клик — своё меню.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string IconUri = "pack://application:,,,/Assets/LightSpace.ico";

    private readonly Forms.NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Action _open;
    private readonly Action _exit;

    private TrayMenuWindow? _menu;

    public TrayIcon(MainViewModel viewModel, Action open, Action exit)
    {
        _viewModel = viewModel;
        _open = open;
        _exit = exit;

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true
        };

        _icon.MouseUp += OnMouseUp;
        _viewModel.Settings.PropertyChanged += OnSettingsChanged;
        _viewModel.Devices.PropertyChanged += OnDevicesChanged;

        UpdateText();
    }

    public void Dispose()
    {
        _viewModel.Settings.PropertyChanged -= OnSettingsChanged;
        _viewModel.Devices.PropertyChanged -= OnDevicesChanged;
        _menu?.Close();

        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri(IconUri));

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream);
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
            ShowMenu();
        else if (e.Button == Forms.MouseButtons.Left)
            _open();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            UpdateText();
    }

    private void OnDevicesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicesViewModel.ProblemCount))
            UpdateText();
    }

    /// <summary>
    /// Подсказка значка: название, выключена ли подсветка и сколько устройств не на связи.
    /// </summary>
    private void UpdateText()
    {
        var text = _viewModel.Settings.LightingEnabled ? "Light Space" : "Light Space — подсветка выключена";

        if (_viewModel.Devices.ProblemCount is var problems and > 0)
            text += $"\nНет связи с устройствами: {problems}";

        // Счётчик пересчитывается дважды в секунду — значок обновляется, только когда текст правда изменился
        if (_icon.Text != text)
            _icon.Text = text;
    }

    private void ShowMenu()
    {
        _menu?.Close();

        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;

        _menu = new TrayMenuWindow(_viewModel, _open, _exit);
        _menu.Closed += (sender, _) =>
        {
            if (ReferenceEquals(sender, _menu))
                _menu = null;
        };

        _menu.ShowAt(new Point(cursor.X, cursor.Y), new Rect(screen.Left, screen.Top, screen.Width, screen.Height));
    }
}

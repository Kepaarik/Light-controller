using System.ComponentModel;
using System.Windows;
using LightSpace.App.ViewModels;
using Forms = System.Windows.Forms;

namespace LightSpace.App.Services;

/// <summary>
/// Значок Light Space в области уведомлений: двойной клик открывает окно, правый клик — своё меню.
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
        _icon.MouseDoubleClick += OnMouseDoubleClick;
        _viewModel.Settings.PropertyChanged += OnSettingsChanged;

        UpdateText();
    }

    public void Dispose()
    {
        _viewModel.Settings.PropertyChanged -= OnSettingsChanged;
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
    }

    private void OnMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            _open();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            UpdateText();
    }

    /// <summary>
    /// Подсказка значка: название и выключена ли подсветка.
    /// </summary>
    private void UpdateText()
    {
        _icon.Text = _viewModel.Settings.LightingEnabled ? "Light Space" : "Light Space — подсветка выключена";
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

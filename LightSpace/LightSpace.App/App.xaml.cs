using System.ComponentModel;
using System.Windows;
using LightSpace.App.Services;
using LightSpace.App.ViewModels;
using LightSpace.Core.Config;

namespace LightSpace.App;

/// <summary>
/// Приложение Light Space: живёт в трее, окно открывается по требованию.
/// </summary>
public partial class App : Application
{
    private AppConfig _config = new();
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;

    // Выход из приложения, а не просто закрытие окна в трей
    private bool _isExiting;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _config = ConfigStore.Load();
        ThemeService.Initialize();

        // Путь к exe мог поменяться с прошлого запуска — запись автозапуска обновляем
        if (_config.StartWithWindows)
            StartupRegistration.Apply(true);

        _viewModel = new MainViewModel(_config);
        _window = new MainWindow(_viewModel);
        _window.Closing += OnWindowClosing;

        _tray = new TrayIcon(_viewModel, ShowWindow, ExitApplication);

        // Устройства ищем сразу при запуске: найденные получают свои сохранённые цвета, даже если окно не открывали
        _viewModel.Devices.FindCommand.Execute(null);

        // При автозапуске окно не показываем: приложение сразу уходит в трей
        if (!e.Args.Contains(StartupRegistration.TrayArgument))
            ShowWindow();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Shutdown();
        _viewModel?.Settings.SaveNow();
        ThemeService.Shutdown();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
            return;

        if (_config.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            _window?.Hide();
            return;
        }

        // Окно уже закрывается само; повторный Close изнутри Closing недопустим, поэтому выходим следом
        _isExiting = true;
        Dispatcher.InvokeAsync(() => Shutdown());
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    private void ExitApplication()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        _window?.Close();
        Shutdown();
    }
}

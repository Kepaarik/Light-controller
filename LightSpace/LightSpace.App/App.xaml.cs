using System.ComponentModel;
using System.Windows;
using LightSpace.App.Services;
using LightSpace.App.ViewModels;
using LightSpace.Core.Config;
using Microsoft.Win32;

namespace LightSpace.App;

/// <summary>
/// Приложение Light Space: живёт в трее, окно открывается по требованию.
/// </summary>
public partial class App : Application
{
    // Сколько ждать перед сном, чтобы устройства успели получить команду выключения
    private static readonly TimeSpan SleepDelay = TimeSpan.FromSeconds(1);

    private AppConfig _config = new();
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;

    // Выход из приложения уже начался
    private bool _isExiting;

    // В каком виде показать окно из трея: обычным или развёрнутым, как было до сворачивания
    private WindowState _restoreState = WindowState.Normal;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Журнал — первым: предупреждения при загрузке настроек и поиске устройств тоже попадут в файл
        FileLog.Start();

        _config = ConfigStore.Load();

        // Путь к exe мог поменяться с прошлого запуска — запись автозапуска обновляем
        if (_config.StartWithWindows)
            StartupRegistration.Apply(true);

        _viewModel = new MainViewModel(_config);
        _window = new MainWindow(_viewModel);
        _window.Closing += OnWindowClosing;
        _window.StateChanged += OnWindowStateChanged;

        _tray = new TrayIcon(_viewModel, ShowWindow, ExitApplication);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Устройства ищем сразу при запуске: найденные сразу показывают текущий эффект, даже если окно не открывали
        _viewModel.Devices.FindCommand.Execute(null);

        // При автозапуске окно не показываем: приложение сразу уходит в трей
        if (!e.Args.Contains(StartupRegistration.TrayArgument))
            ShowWindow();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        // Событие статическое: без отписки оно держало бы приложение в памяти
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _tray?.Dispose();
        _viewModel?.Shutdown();
        _viewModel?.Settings.SaveNow();
    }

    /// <summary>
    /// Перед сном гасит свет на всех устройствах, после пробуждения возвращает его.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.Suspend or PowerModes.Resume))
            return;

        var isAsleep = e.Mode == PowerModes.Suspend;

        Dispatcher.Invoke(() => _viewModel?.SetAsleep(isAsleep));

        // Windows не усыпляет компьютер, пока обработчик не вернулся, — даём выводам отправить команду выключения
        if (isAsleep)
            Thread.Sleep(SleepDelay);
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
            return;

        // Окно уже закрывается само; повторный Close изнутри Closing недопустим, поэтому выходим следом
        _isExiting = true;
        Dispatcher.InvokeAsync(() => Shutdown());
    }

    /// <summary>
    /// Свёрнутое окно уходит в трей; развёрнутое или обычное запоминается, чтобы из трея вернуться в нём же.
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window is null)
            return;

        if (_window.WindowState != WindowState.Minimized)
        {
            _restoreState = _window.WindowState;
            return;
        }

        _window.Hide();
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = _restoreState;

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

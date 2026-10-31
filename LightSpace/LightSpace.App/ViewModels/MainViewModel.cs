using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Frames;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Главное окно и меню трея: вкладки, настройки, общие команды.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly DeviceManager _deviceManager;
    private readonly FrameLoop _frameLoop;

    // Окно открыто и не свёрнуто
    private bool _isWindowShown;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Scene;

    public MainViewModel(AppConfig config)
    {
        _config = config;
        Settings = new SettingsViewModel(config);

        _deviceManager = new DeviceManager(config);
        _frameLoop = new FrameLoop(FrameSnapshot.Build(config, _deviceManager.Devices));
        Devices = new DevicesViewModel(_deviceManager, _frameLoop, Settings.ScheduleSave);
        Scene = new SceneViewModel(config, _deviceManager, _frameLoop, OnContentChanged);

        Settings.PropertyChanged += OnSettingsChanged;
        _deviceManager.DevicesChanged += OnDevicesChanged;
    }

    /// <summary>
    /// Вкладки в заголовке окна.
    /// </summary>
    public IReadOnlyList<PageOption> Pages { get; } = new PageOption[]
    {
        new(AppPage.Scene, "Сцена"),
        new(AppPage.Scenarios, "Сценарии"),
        new(AppPage.Devices, "Устройства"),
        new(AppPage.Settings, "Настройки")
    };

    public SettingsViewModel Settings { get; }

    public DevicesViewModel Devices { get; }

    public SceneViewModel Scene { get; }

    /// <summary>
    /// Сообщает, видно ли окно: живые цвета и превью считаются, только пока на них можно посмотреть.
    /// </summary>
    public void SetWindowShown(bool isShown)
    {
        _isWindowShown = isShown;
        UpdateLive();
    }

    /// <summary>
    /// Останавливает поток кадров, вывод на устройства и закрывает соединения с ними.
    /// </summary>
    public void Shutdown()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _deviceManager.DevicesChanged -= OnDevicesChanged;
        Scene.SetLive(false);

        // Сначала поток кадров: иначе он клал бы кадры в уже закрытые выводы устройств
        _frameLoop.Dispose();
        _deviceManager.Dispose();
    }

    /// <summary>
    /// Выключает всю подсветку или включает её обратно.
    /// </summary>
    [RelayCommand]
    private void ToggleLighting()
    {
        Settings.LightingEnabled = !Settings.LightingEnabled;
    }

    /// <summary>
    /// Поиск устройств: открывает вкладку «Устройства» и запускает поиск.
    /// </summary>
    [RelayCommand]
    private async Task FindDevicesAsync()
    {
        SelectedPage = AppPage.Devices;

        await Devices.FindCommand.ExecuteAsync(null);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Подсветку выключают из меню трея или из настроек — выводы устройств гасят свет и перестают отправлять кадры
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            _deviceManager.SetLightingEnabled(Settings.LightingEnabled);

        // Выключенная подсветка и частота кадров — тоже часть снимка для потока кадров
        if (e.PropertyName is nameof(SettingsViewModel.LightingEnabled) or nameof(SettingsViewModel.FrameRate))
            UpdateFrames();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        UpdateFrames();
    }

    /// <summary>
    /// Эффект, его настройки или яркость изменились: движку — новый снимок, в файл — с задержкой.
    /// </summary>
    private void OnContentChanged()
    {
        UpdateFrames();
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Отдаёт потоку кадров новый снимок текущих настроек и подключённых устройств.
    /// </summary>
    private void UpdateFrames()
    {
        _frameLoop.Update(FrameSnapshot.Build(_config, _deviceManager.Devices));
    }

    partial void OnSelectedPageChanged(AppPage value)
    {
        UpdateLive();
    }

    /// <summary>
    /// Живая сцена работает, только пока окно видно и открыта вкладка «Сцена».
    /// </summary>
    private void UpdateLive()
    {
        Scene.SetLive(_isWindowShown && SelectedPage == AppPage.Scene);
    }
}

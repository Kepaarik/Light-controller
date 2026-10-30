using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Главное окно и меню трея: вкладки, настройки, общие команды.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DeviceManager _deviceManager;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Scene;

    public MainViewModel(AppConfig config)
    {
        Settings = new SettingsViewModel(config);

        _deviceManager = new DeviceManager(config);
        Devices = new DevicesViewModel(_deviceManager, Settings.ScheduleSave);

        Settings.PropertyChanged += OnSettingsChanged;
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

    /// <summary>
    /// Останавливает вывод на устройства и закрывает соединения с ними.
    /// </summary>
    public void Shutdown()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
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

    [RelayCommand]
    private void OpenDevices()
    {
        SelectedPage = AppPage.Devices;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Подсветку выключают из меню трея или из настроек — выводы устройств гасят свет и перестают отправлять кадры
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            _deviceManager.SetLightingEnabled(Settings.LightingEnabled);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Главное окно и меню трея: вкладки, настройки, общие команды.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Scene;

    public MainViewModel(AppConfig config)
    {
        Settings = new SettingsViewModel(config);
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

    /// <summary>
    /// Выключает всю подсветку или включает её обратно.
    /// </summary>
    [RelayCommand]
    private void ToggleLighting()
    {
        Settings.LightingEnabled = !Settings.LightingEnabled;
    }

    /// <summary>
    /// Поиск устройств; сами драйверы появятся вместе с переносом устройств.
    /// </summary>
    [RelayCommand]
    private void FindDevices()
    {
    }
}

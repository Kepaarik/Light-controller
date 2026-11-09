namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница главного окна.
/// </summary>
public enum AppPage
{
    Scene,
    Devices,
    Monitors,
    Settings,

    /// <summary>
    /// Эффект одного устройства, открытого в боковом меню.
    /// </summary>
    Device
}

/// <summary>
/// Раздел в левом меню окна: страница, подпись и значок из Segoe Fluent Icons.
/// </summary>
public sealed record PageOption(AppPage Page, string Title, string Icon)
{
    public override string ToString() => Title;
}

/// <summary>
/// Найденное устройство в левом меню окна: открывает выбор его эффекта. Рядом со значком — живой цвет устройства.
/// </summary>
public sealed record DeviceNavOption(SceneDeviceViewModel Device)
{
    public string Id => Device.Id;

    public string Title => Device.Name;

    public string Icon => Device.Icon;

    public override string ToString() => Title;
}

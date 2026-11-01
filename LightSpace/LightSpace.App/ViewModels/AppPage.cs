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
/// Найденное устройство в левом меню окна: открывает выбор его эффекта.
/// </summary>
public sealed record DeviceNavOption(string Id, string Title, string Icon)
{
    public override string ToString() => Title;
}

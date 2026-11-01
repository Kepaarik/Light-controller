namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница главного окна.
/// </summary>
public enum AppPage
{
    Scene,
    Devices,
    Monitors,
    Settings
}

/// <summary>
/// Раздел в левом меню окна: страница, подпись и значок из Segoe Fluent Icons.
/// </summary>
public sealed record PageOption(AppPage Page, string Title, string Icon)
{
    public override string ToString() => Title;
}

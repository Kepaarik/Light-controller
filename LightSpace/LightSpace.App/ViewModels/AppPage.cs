namespace LightSpace.App.ViewModels;

/// <summary>
/// Страница главного окна.
/// </summary>
public enum AppPage
{
    Scene,
    Scenarios,
    Devices,
    Settings,

    /// <summary>
    /// Витрина компонентов, есть только в отладочной сборке.
    /// </summary>
    Showcase
}

/// <summary>
/// Пункт левого меню разделов: страница, подпись и значок из Segoe Fluent Icons.
/// </summary>
public sealed record PageOption(AppPage Page, string Title, string Icon)
{
    public override string ToString() => Title;
}

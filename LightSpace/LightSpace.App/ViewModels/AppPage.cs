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
/// Вкладка в заголовке окна.
/// </summary>
public sealed record PageOption(AppPage Page, string Title)
{
    public override string ToString() => Title;
}

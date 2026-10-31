using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вариант темы для переключателя в настройках и на витрине.
/// </summary>
public sealed record ThemeOption(ThemePreference Preference, string Title)
{
    public override string ToString() => Title;
}

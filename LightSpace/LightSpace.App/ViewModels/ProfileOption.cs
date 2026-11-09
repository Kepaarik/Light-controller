using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Сохранённый профиль подсветки в меню трея и в настройках.
/// </summary>
public sealed class ProfileOption
{
    public ProfileOption(ProfileConfig settings)
    {
        Settings = settings;
    }

    public ProfileConfig Settings { get; }

    public string Name => Settings.Name;

    /// <summary>
    /// Сколько устройств помнит профиль.
    /// </summary>
    public string DevicesText => $"Устройств: {Settings.DeviceLighting.Count}";
}

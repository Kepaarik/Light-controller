using System.Windows;
using LightSpace.Core.Config;
using Microsoft.Win32;

namespace LightSpace.App.Services;

/// <summary>
/// Тема оформления приложения: по умолчанию тёмная, по выбору — светлая или как в Windows; акцентный цвет — свой, фиолетовый.
/// При смене темы в Windows приложение перекрашивается на лету.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightUri = new("/LightSpace;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("/LightSpace;component/Themes/Dark.xaml", UriKind.Relative);

    /// <summary>
    /// Сейчас показана тёмная тема.
    /// </summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// Выбранная тема: системная или принудительная.
    /// </summary>
    public static ThemePreference Preference { get; private set; } = ThemePreference.Dark;

    /// <summary>
    /// Тема или акцентный цвет сменились.
    /// </summary>
    public static event EventHandler? ThemeChanged;

    public static void Initialize(ThemePreference preference)
    {
        Preference = preference;
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    public static void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        Apply();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Смена темы приходит как General, смена акцента — как General или Color; событие может прийти не из потока UI
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.InvokeAsync(Apply);
    }

    private static void Apply()
    {
        var dark = Preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemDark()
        };

        var resources = Application.Current.Resources;

        // Первый словарь в App.xaml — цвета темы; подменяем его целиком, всё на DynamicResource перекрашивается
        resources.MergedDictionaries[0] = new ResourceDictionary { Source = dark ? DarkUri : LightUri };

        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

        // Нет значения — в Windows светлая тема по умолчанию
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}

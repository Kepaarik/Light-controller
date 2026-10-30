using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace LightSpace.App.Services;

/// <summary>
/// Какую тему показывать: как в Windows или принудительно светлую либо тёмную.
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Тема оформления приложения: светлая или тёмная по системной настройке Windows, акцентный цвет — системный.
/// При смене темы или акцента в Windows приложение перекрашивается на лету.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightUri = new("/LightSpace;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("/LightSpace;component/Themes/Dark.xaml", UriKind.Relative);

    // Акцентный цвет на случай, если Windows его не отдала
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static UISettings? _uiSettings;

    /// <summary>
    /// Сейчас показана тёмная тема.
    /// </summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// Выбранная тема: системная или принудительная.
    /// </summary>
    public static ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>
    /// Тема или акцентный цвет сменились.
    /// </summary>
    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
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
        ApplyAccent(resources, dark);

        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

        // Нет значения — в Windows светлая тема по умолчанию
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    /// <summary>
    /// Кладёт акцентные кисти прямо в ресурсы приложения: они перекрывают запасные из словаря темы.
    /// Как в Windows 11: в тёмной теме акцент светлее, в светлой — темнее, чтобы хватало контраста.
    /// </summary>
    private static void ApplyAccent(ResourceDictionary resources, bool dark)
    {
        var accent = ReadAccent(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
        var accentText = ReadAccent(dark ? UIColorType.AccentLight3 : UIColorType.AccentDark2);

        resources["AccentBrush"] = Freeze(new SolidColorBrush(accent));
        resources["AccentHoverBrush"] = Freeze(new SolidColorBrush(WithAlpha(accent, 0xE6)));
        resources["AccentPressedBrush"] = Freeze(new SolidColorBrush(WithAlpha(accent, 0xCC)));
        resources["AccentSoftBrush"] = Freeze(new SolidColorBrush(WithAlpha(accent, 0x33)));
        resources["AccentTextBrush"] = Freeze(new SolidColorBrush(accentText));
        resources["OnAccentBrush"] = Freeze(new SolidColorBrush(dark ? Colors.Black : Colors.White));
    }

    private static Color ReadAccent(UIColorType type)
    {
        try
        {
            _uiSettings ??= new UISettings();
            var color = _uiSettings.GetColorValue(type);

            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException or TypeLoadException)
        {
            return FallbackAccent;
        }
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

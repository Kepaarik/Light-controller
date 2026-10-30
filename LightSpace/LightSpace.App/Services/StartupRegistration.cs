using Microsoft.Win32;

namespace LightSpace.App.Services;

/// <summary>
/// Автозапуск Light Space вместе с Windows: запись в HKCU\...\Run, приложение стартует сразу в трей.
/// </summary>
public static class StartupRegistration
{
    /// <summary>
    /// Ключ командной строки: запуститься без окна, только значком в трее.
    /// </summary>
    public const string TrayArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LightSpace";

    /// <summary>
    /// Включает или выключает автозапуск; при включении обновляет путь к exe, если приложение переехало.
    /// </summary>
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var path = Environment.ProcessPath;
        if (path is null)
            return;

        key.SetValue(ValueName, $"\"{path}\" {TrayArgument}");
    }
}

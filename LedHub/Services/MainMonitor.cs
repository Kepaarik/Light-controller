namespace LedHub.Services;

/// <summary>
/// Главный монитор, который выбрал пользователь: с него берут картинку режимы «по экрану» и игровые режимы.
/// </summary>
public static class MainMonitor
{
    /// <summary>
    /// Системное имя выбранного монитора; null — основной монитор Windows.
    /// </summary>
    public static string? Key { get; set; }

    /// <summary>
    /// Выбранный монитор, а если его сейчас нет — основной монитор Windows.
    /// </summary>
    public static MonitorInfo? Resolve()
    {
        var monitors = MonitorList.GetAll();

        return monitors.FirstOrDefault(m => m.Key == Key)
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors.FirstOrDefault();
    }
}

namespace LedHub.ViewModels;

/// <summary>
/// Вариант главного монитора в списке; Key == null — основной монитор Windows.
/// </summary>
public sealed record MainMonitorOption(string? Key, string Title);

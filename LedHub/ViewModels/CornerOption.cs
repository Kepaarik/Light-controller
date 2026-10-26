using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Пункт выбора угла монитора, с которого начинается лента.
/// </summary>
public sealed record CornerOption(ScreenCorner Corner, string Title);

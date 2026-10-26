using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Начало ленты: угол монитора и направление, в котором лента идёт от него (вид спереди).
/// </summary>
public sealed record StartOption(ScreenCorner Corner, bool IsClockwise, string Title)
{
    /// <summary>
    /// Все варианты. По часовой стрелке углы идут так: сверху слева → сверху справа → снизу справа → снизу слева.
    /// </summary>
    public static IReadOnlyList<StartOption> All { get; } = new StartOption[]
    {
        new(ScreenCorner.BottomLeft, false, "Снизу слева → вправо"),
        new(ScreenCorner.BottomLeft, true, "Снизу слева ↑ вверх"),
        new(ScreenCorner.TopLeft, true, "Сверху слева → вправо"),
        new(ScreenCorner.TopLeft, false, "Сверху слева ↓ вниз"),
        new(ScreenCorner.TopRight, false, "Сверху справа ← влево"),
        new(ScreenCorner.TopRight, true, "Сверху справа ↓ вниз"),
        new(ScreenCorner.BottomRight, true, "Снизу справа ← влево"),
        new(ScreenCorner.BottomRight, false, "Снизу справа ↑ вверх")
    };
}

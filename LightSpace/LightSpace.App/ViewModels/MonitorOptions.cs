using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вариант ориентации монитора.
/// </summary>
public sealed record RotationOption(MonitorRotation Rotation, string Title)
{
    public static IReadOnlyList<RotationOption> All { get; } = new RotationOption[]
    {
        new(MonitorRotation.None, "Как в Windows"),
        new(MonitorRotation.Clockwise90, "Повёрнут по часовой (90°)"),
        new(MonitorRotation.Upside180, "Вверх ногами (180°)"),
        new(MonitorRotation.CounterClockwise270, "Повёрнут против часовой (270°)")
    };
}

/// <summary>
/// Начало ленты: угол монитора и направление, в котором лента идёт от него.
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

/// <summary>
/// Устройство, которое можно повесить вокруг монитора; Id null — «Без ленты».
/// </summary>
/// <param name="LedCount">Светодиодов у адресной ленты; 0 — устройство одного цвета или сейчас не подключено.</param>
public sealed record MonitorDeviceOption(string? Id, string Name, int LedCount);

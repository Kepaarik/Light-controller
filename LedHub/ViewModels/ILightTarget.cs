using LedHub.Core;

namespace LedHub.ViewModels;

/// <summary>
/// То, чем можно управлять из панели управления: одна лента или группа лент.
/// </summary>
public interface ILightTarget
{
    string Title { get; }

    string Subtitle { get; }

    /// <summary>
    /// Последний выставленный цвет; сами контроллеры своё состояние не сообщают.
    /// </summary>
    RgbColor Color { get; set; }

    int Brightness { get; set; }

    IReadOnlyList<LedDevice> GetDevices();
}

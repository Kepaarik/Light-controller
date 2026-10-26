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

    /// <summary>
    /// Включена ли лента по последней команде из приложения; null — ещё не включали и не выключали.
    /// </summary>
    bool? IsOn { get; set; }

    IReadOnlyList<LedDevice> GetDevices();
}

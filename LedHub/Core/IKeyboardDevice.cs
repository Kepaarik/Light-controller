namespace LedHub.Core;

/// <summary>
/// Клавиша с подсветкой: номер её светодиода, надпись и место на клавиатуре в размерах стандартной клавиши.
/// X и Y — левый верхний угол клавиши.
/// </summary>
public sealed record KeyboardKey(int Led, string Label, double X, double Y, double Width = 1, double Height = 1);

/// <summary>
/// Клавиатура, у которой каждая клавиша подсвечивается своим цветом.
/// </summary>
public interface IKeyboardDevice : IAddressableLedDevice
{
    /// <summary>
    /// Клавиши с подсветкой так, как они расположены на клавиатуре.
    /// </summary>
    IReadOnlyList<KeyboardKey> Keys { get; }
}

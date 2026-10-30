namespace LedHub.Core;

/// <summary>
/// Клавиша с подсветкой: номер её светодиода, надпись и место на клавиатуре в размерах стандартной клавиши.
/// X и Y — левый верхний угол клавиши. Code — код клавиши в Windows, по нему нажатие находит клавишу
/// (0 — клавиша, которую Windows не видит, например Fn); у Enter цифрового блока — <see cref="NumpadEnter"/>.
/// </summary>
public sealed record KeyboardKey(int Led, string Label, double X, double Y, double Width = 1, double Height = 1, int Code = 0)
{
    /// <summary>
    /// Код Enter цифрового блока: у Windows он тот же, что у основного Enter, поэтому ему дан свой.
    /// </summary>
    public const int NumpadEnter = 0x10D;
}

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

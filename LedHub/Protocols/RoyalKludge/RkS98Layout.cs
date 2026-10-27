using LedHub.Core;

namespace LedHub.Protocols.RoyalKludge;

/// <summary>
/// Раскладка клавиш RK S98: где стоит каждая клавиша и какой у неё светодиод.
/// Светодиод клавиши — её место в матрице 21×6: ряд + 6 × столбец.
/// </summary>
public static class RkS98Layout
{
    private const int Rows = 6;

    public static IReadOnlyList<KeyboardKey> Keys { get; } = Build();

    private static List<KeyboardKey> Build()
    {
        var keys = new List<KeyboardKey>();

        // Esc и F1–F12 группами по четыре, Delete справа; дальше экран и колёсико без подсветки
        keys.Add(Key(0, 0, "Esc", 0));

        for (var i = 0; i < 12; i++)
            keys.Add(Key(0, 2 + i, $"F{i + 1}", 1.25 + i + i / 4 * 0.25));

        keys.Add(Key(0, 14, "Del", 15));

        // Ряд цифр
        for (var column = 0; column <= 12; column++)
            keys.Add(Key(1, column, "`1234567890-="[column].ToString(), column));

        keys.Add(Key(1, 13, "Backspace", 13, 2));
        keys.Add(Key(1, 14, "Home", 15));
        keys.AddRange(Numpad(1, "Num", "/", "*"));
        keys.Add(Key(1, 18, "-", 19));

        // Tab, Q…], обратная косая черта
        keys.Add(Key(2, 0, "Tab", 0, 1.5));

        for (var column = 1; column <= 12; column++)
            keys.Add(Key(2, column, "QWERTYUIOP[]"[column - 1].ToString(), column + 0.5));

        keys.Add(Key(2, 13, "\\", 13.5, 1.5));
        keys.Add(Key(2, 14, "PgUp", 15));
        keys.AddRange(Numpad(2, "7", "8", "9"));
        keys.Add(Key(2, 18, "+", 19, 1, 2));

        // Caps Lock, A…', Enter
        keys.Add(Key(3, 0, "Caps", 0, 1.75));

        for (var column = 1; column <= 11; column++)
            keys.Add(Key(3, column, "ASDFGHJKL;'"[column - 1].ToString(), column + 0.75));

        keys.Add(Key(3, 13, "Enter", 12.75, 2.25));
        keys.Add(Key(3, 14, "PgDn", 15));
        keys.AddRange(Numpad(3, "4", "5", "6"));

        // Shift, Z…/, правый Shift и стрелка вверх
        keys.Add(Key(4, 0, "Shift", 0, 2.25));

        for (var column = 1; column <= 10; column++)
            keys.Add(Key(4, column, "ZXCVBNM,./"[column - 1].ToString(), column + 1.25));

        keys.Add(Key(4, 13, "Shift", 12.25, 1.75));
        keys.Add(Key(4, 14, "↑", 14));
        keys.AddRange(Numpad(4, "1", "2", "3"));
        keys.Add(Key(4, 18, "Enter", 19, 1, 2));

        // Нижний ряд: Ctrl, Win, Alt, пробел, Alt, Fn, Ctrl, стрелки, 0 и точка цифрового блока
        keys.Add(Key(5, 0, "Ctrl", 0, 1.25));
        keys.Add(Key(5, 1, "Win", 1.25, 1.25));
        keys.Add(Key(5, 2, "Alt", 2.5, 1.25));
        keys.Add(Key(5, 5, "", 3.75, 6.25));
        keys.Add(Key(5, 8, "Alt", 10));
        keys.Add(Key(5, 9, "Fn", 11));
        keys.Add(Key(5, 10, "Ctrl", 12));
        keys.Add(Key(5, 13, "←", 13));
        keys.Add(Key(5, 14, "↓", 14));
        keys.Add(Key(5, 15, "→", 15));
        keys.Add(Key(5, 16, "0", 16, 2));
        keys.Add(Key(5, 17, ".", 18));

        return keys;
    }

    /// <summary>
    /// Три клавиши цифрового блока в ряду: столбцы матрицы 15–17.
    /// </summary>
    private static IEnumerable<KeyboardKey> Numpad(int row, params string[] labels)
    {
        for (var column = 15; column <= 17; column++)
            yield return Key(row, column, labels[column - 15], column + 1);
    }

    /// <summary>
    /// Клавиша по месту в матрице; ряд функциональных клавиш отделён от остальных небольшим зазором.
    /// </summary>
    private static KeyboardKey Key(int row, int column, string label, double x, double width = 1, double height = 1)
    {
        return new KeyboardKey(row + Rows * column, label, x, row == 0 ? 0 : row + 0.25, width, height);
    }
}

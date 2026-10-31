using LightSpace.Core;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Неизменяемая уменьшенная картинка монитора: клетки по строкам сверху вниз, слева направо.
/// </summary>
public sealed class ScreenPicture
{
    /// <summary>
    /// Картинки нет: монитор не снимается или снять его не удалось. Везде чёрный цвет.
    /// </summary>
    public static readonly ScreenPicture Empty = new(1, 1, new[] { RgbColor.Black });

    /// <summary>
    /// cells — клетки по строкам; массив после этого никто не меняет.
    /// </summary>
    public ScreenPicture(int columns, int rows, RgbColor[] cells)
    {
        Columns = columns;
        Rows = rows;
        Cells = cells;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>
    /// Клетки картинки по строкам сверху вниз.
    /// </summary>
    public IReadOnlyList<RgbColor> Cells { get; }

    /// <summary>
    /// Картинка по кадру в формате BGRA, строки сверху вниз.
    /// </summary>
    public static ScreenPicture FromBgra(byte[] pixels, int columns, int rows)
    {
        var cells = new RgbColor[columns * rows];

        for (var i = 0; i < cells.Length; i++)
            cells[i] = new RgbColor(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);

        return new ScreenPicture(columns, rows, cells);
    }

    public RgbColor At(int column, int row)
    {
        return Cells[Math.Clamp(row, 0, Rows - 1) * Columns + Math.Clamp(column, 0, Columns - 1)];
    }

    /// <summary>
    /// Цвет картинки в точке: u и v от 0 до 1 слева направо и сверху вниз, между клетками — плавно.
    /// </summary>
    public RgbColor Sample(double u, double v)
    {
        var x = Math.Clamp(u, 0, 1) * Columns - 0.5;
        var y = Math.Clamp(v, 0, 1) * Rows - 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);

        var top = EffectMath.Mix(At(x0, y0), At(x0 + 1, y0), x - x0);
        var bottom = EffectMath.Mix(At(x0, y0 + 1), At(x0 + 1, y0 + 1), x - x0);

        return EffectMath.Mix(top, bottom, y - y0);
    }
}

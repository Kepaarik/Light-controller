namespace LightSpace.Core.Config;

/// <summary>
/// Вид стекла окна; все силы — от 0 до 100.
/// </summary>
public sealed class GlassConfig
{
    /// <summary>
    /// Цвет тонировки в формате #RRGGBB.
    /// </summary>
    public string Tint { get; set; } = "#7C6CFF";

    /// <summary>
    /// Насколько сквозь окно видно то, что под ним.
    /// </summary>
    public double Transparency { get; set; } = 55;

    /// <summary>
    /// Насколько сильно стекло окрашено в цвет тонировки.
    /// </summary>
    public double TintStrength { get; set; } = 30;

    /// <summary>
    /// Плотность карточек: чем больше, тем они матовее и заметнее на стекле.
    /// </summary>
    public double Frost { get; set; } = 30;

    /// <summary>
    /// Яркость блика по краю карточек.
    /// </summary>
    public double Shine { get; set; } = 50;

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (!RgbColor.TryParse(Tint, out _))
            Tint = "#7C6CFF";

        Transparency = Math.Clamp(Transparency, 0, 100);
        TintStrength = Math.Clamp(TintStrength, 0, 100);
        Frost = Math.Clamp(Frost, 0, 100);
        Shine = Math.Clamp(Shine, 0, 100);
    }
}

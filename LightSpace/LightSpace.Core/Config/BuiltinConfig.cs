namespace LightSpace.Core.Config;

/// <summary>
/// Настройки содержимого — встроенного эффекта устройств: какой эффект играет каждое устройство, с какой скоростью
/// и каким цветом. Эффекты играют сами устройства, у каждого устройства свой набор эффектов.
/// </summary>
public sealed class BuiltinConfig
{
    /// <summary>
    /// Скорость по умолчанию, в процентах.
    /// </summary>
    public const int DefaultSpeed = 50;

    /// <summary>
    /// Цвет по умолчанию для эффектов одним цветом.
    /// </summary>
    public const string DefaultColor = "#7C6CFF";

    /// <summary>
    /// Номер встроенного эффекта по идентификатору устройства. Устройство, которого здесь нет или у которого нет такого
    /// эффекта, играет свой первый эффект.
    /// </summary>
    public Dictionary<string, int> Effects { get; set; } = new();

    /// <summary>
    /// Скорость эффектов, 0–100 %.
    /// </summary>
    public int Speed { get; set; } = DefaultSpeed;

    /// <summary>
    /// Эффекты играют одним цветом <see cref="Color"/> там, где устройство это умеет; иначе — своими цветами.
    /// </summary>
    public bool UseColor { get; set; }

    /// <summary>
    /// Цвет эффектов в формате #RRGGBB, когда они играют одним цветом.
    /// </summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>
    /// Независимая копия настроек: правки копии не меняют оригинал.
    /// </summary>
    public BuiltinConfig Clone()
    {
        return new BuiltinConfig
        {
            Effects = new Dictionary<string, int>(Effects),
            Speed = Speed,
            UseColor = UseColor,
            Color = Color
        };
    }

    /// <summary>
    /// Приводит настройки к допустимым: файл могли поправить руками или записать в другой версии.
    /// </summary>
    public void Normalize()
    {
        Effects ??= new Dictionary<string, int>();
        Speed = Math.Clamp(Speed, 0, 100);

        // Испорченный цвет не прочитается — вместо него цвет по умолчанию
        if (!RgbColor.TryParse(Color, out _))
            Color = DefaultColor;
    }
}

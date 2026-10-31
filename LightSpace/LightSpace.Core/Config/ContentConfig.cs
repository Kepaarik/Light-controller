namespace LightSpace.Core.Config;

/// <summary>
/// Содержимое, которое играет на устройствах: эффект из галереи и сохранённые настройки эффектов.
/// </summary>
public sealed class ContentConfig
{
    /// <summary>
    /// Идентификатор эффекта, который сейчас играет.
    /// </summary>
    public string EffectId { get; set; } = "solid";

    /// <summary>
    /// Настройки эффектов по их идентификаторам; эффект, которого здесь нет, играет с настройками по умолчанию.
    /// Настройки остаются, когда выбирают другой эффект, и возвращаются вместе с ним.
    /// </summary>
    public Dictionary<string, EffectValuesConfig> Effects { get; set; } = new();

    /// <summary>
    /// Независимая копия содержимого: правки копии не меняют оригинал.
    /// </summary>
    public ContentConfig Clone()
    {
        return new ContentConfig
        {
            EffectId = EffectId,
            Effects = Effects.Where(e => e.Value != null).ToDictionary(e => e.Key, e => new EffectValuesConfig
            {
                Numbers = new Dictionary<string, double>(e.Value.Numbers),
                Colors = new Dictionary<string, string>(e.Value.Colors)
            })
        };
    }

    /// <summary>
    /// Приводит содержимое к допустимому: пустые разделы в файле дали бы null вместо эффекта и его настроек.
    /// </summary>
    public void Normalize()
    {
        EffectId ??= "solid";
        Effects ??= new Dictionary<string, EffectValuesConfig>();
    }
}

/// <summary>
/// Значения настроек одного эффекта, которые выставил пользователь; чего здесь нет — берётся по умолчанию.
/// </summary>
public sealed class EffectValuesConfig
{
    /// <summary>
    /// Числа, переключатели (0 или 1) и выбор из вариантов по ключам настроек.
    /// </summary>
    public Dictionary<string, double> Numbers { get; set; } = new();

    /// <summary>
    /// Цвета в формате #RRGGBB по ключам настроек.
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();
}

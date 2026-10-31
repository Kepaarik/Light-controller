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

using LightSpace.Core;
using LightSpace.Core.Config;

namespace LightSpace.Engine.Effects;

/// <summary>
/// Вид настройки эффекта.
/// </summary>
public enum EffectParameterKind
{
    Number,
    Color,
    Choice,
    Toggle
}

/// <summary>
/// Вариант для настройки с выбором из списка.
/// </summary>
public sealed record EffectChoice(double Value, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// Настройка эффекта: число со своим диапазоном, цвет, выбор из вариантов или переключатель.
/// </summary>
public sealed record EffectParameter(string Key, string Title, EffectParameterKind Kind)
{
    public double Minimum { get; init; }

    public double Maximum { get; init; } = 1;

    public double Default { get; init; }

    public RgbColor DefaultColor { get; init; } = RgbColor.White;

    /// <summary>
    /// Как показывать значение числа рядом с ползунком, например «{0:0.0}×».
    /// </summary>
    public string Format { get; init; } = "{0:0.0}";

    public IReadOnlyList<EffectChoice> Choices { get; init; } = Array.Empty<EffectChoice>();

    public static EffectParameter Number(string key, string title, double minimum, double maximum, double value, string format = "{0:0.0}")
    {
        return new EffectParameter(key, title, EffectParameterKind.Number)
        {
            Minimum = minimum,
            Maximum = maximum,
            Default = value,
            Format = format
        };
    }

    public static EffectParameter Color(string key, string title, string hex)
    {
        return new EffectParameter(key, title, EffectParameterKind.Color) { DefaultColor = RgbColor.Parse(hex) };
    }

    public static EffectParameter Choice(string key, string title, double value, params EffectChoice[] choices)
    {
        return new EffectParameter(key, title, EffectParameterKind.Choice) { Default = value, Choices = choices };
    }

    public static EffectParameter Toggle(string key, string title, bool value)
    {
        return new EffectParameter(key, title, EffectParameterKind.Toggle) { Default = value ? 1 : 0 };
    }
}

/// <summary>
/// Неизменяемые значения настроек одного эффекта, которые выставил пользователь; чего здесь нет — берётся по умолчанию.
/// </summary>
public sealed class EffectValues
{
    /// <summary>
    /// Ни одной своей настройки: эффект играет как задумано по умолчанию.
    /// </summary>
    public static readonly EffectValues Empty = new(new Dictionary<string, double>(), new Dictionary<string, RgbColor>());

    public EffectValues(IReadOnlyDictionary<string, double> numbers, IReadOnlyDictionary<string, RgbColor> colors)
    {
        Numbers = numbers;
        Colors = colors;
    }

    public IReadOnlyDictionary<string, double> Numbers { get; }

    public IReadOnlyDictionary<string, RgbColor> Colors { get; }

    /// <summary>
    /// Копия сохранённых настроек эффекта: дальнейшие правки настроек в окне на неё не влияют.
    /// </summary>
    public static EffectValues From(EffectValuesConfig? config)
    {
        if (config == null)
            return Empty;

        var colors = new Dictionary<string, RgbColor>();

        foreach (var (key, hex) in config.Colors)
        {
            if (RgbColor.TryParse(hex, out var color))
                colors[key] = color;
        }

        return new EffectValues(new Dictionary<string, double>(config.Numbers), colors);
    }
}

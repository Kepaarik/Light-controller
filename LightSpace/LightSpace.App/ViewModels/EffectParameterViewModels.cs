using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Одна настройка выбранного эффекта; правка сразу попадает в сохранённые настройки эффекта, движок получает их новым снимком.
/// </summary>
public abstract class EffectParameterViewModel : ObservableObject
{
    private readonly Action _changed;

    public EffectParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed)
    {
        Parameter = parameter;
        Values = values;
        _changed = changed;
    }

    public EffectParameter Parameter { get; }

    /// <summary>
    /// Сохранённые настройки эффекта, в которые пишет эта настройка.
    /// </summary>
    public EffectValuesConfig Values { get; }

    public string Title => Parameter.Title;

    /// <summary>
    /// Возвращает значение по умолчанию.
    /// </summary>
    public abstract void Reset();

    public void NotifyChanged()
    {
        _changed();
    }
}

/// <summary>
/// Число со своим диапазоном: ползунок с подписью значения.
/// </summary>
public sealed class NumberParameterViewModel : EffectParameterViewModel
{
    public NumberParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed)
        : base(parameter, values, changed)
    {
    }

    public double Value
    {
        get => Values.Numbers.TryGetValue(Parameter.Key, out var value) ? value : Parameter.Default;
        set
        {
            if (Math.Abs(value - Value) < 1e-9)
                return;

            Values.Numbers[Parameter.Key] = value;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    public double Minimum => Parameter.Minimum;

    public double Maximum => Parameter.Maximum;

    /// <summary>
    /// Целые значения (количество, проценты) ползунок выбирает с шагом 1, остальные — плавно.
    /// </summary>
    public bool IsWhole => Decimals == 0;

    public double Step => IsWhole ? 1 : (Maximum - Minimum) / 100;

    /// <summary>
    /// Сколько знаков после запятой у значения: «{0:0.00}» — два.
    /// </summary>
    public int Decimals
    {
        get
        {
            var format = Parameter.Format;
            var start = format.IndexOf(':') + 1;
            var end = format.IndexOf('}');

            if (start <= 0 || end < start)
                return 1;

            var pattern = format[start..end];
            var dot = pattern.IndexOf('.');

            return dot < 0 ? 0 : pattern.Length - dot - 1;
        }
    }

    /// <summary>
    /// Единица после значения: «{0:0} %» — «%», «{0:0.0}×» — «×».
    /// </summary>
    public string Unit => Parameter.Format[(Parameter.Format.IndexOf('}') + 1)..].Trim();

    public override void Reset()
    {
        Values.Numbers.Remove(Parameter.Key);
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>
/// Цвет: образец, по клику — круг цветов.
/// </summary>
public sealed class ColorParameterViewModel : EffectParameterViewModel
{
    public ColorParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed)
        : base(parameter, values, changed)
    {
    }

    public RgbColor Color
    {
        get => Values.Colors.TryGetValue(Parameter.Key, out var hex) && RgbColor.TryParse(hex, out var color) ? color : Parameter.DefaultColor;
        set
        {
            if (value == Color)
                return;

            Values.Colors[Parameter.Key] = value.ToHex();
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    public override void Reset()
    {
        Values.Colors.Remove(Parameter.Key);
        OnPropertyChanged(nameof(Color));
    }
}

/// <summary>
/// Выбор из нескольких вариантов: сегменты.
/// </summary>
public sealed class ChoiceParameterViewModel : EffectParameterViewModel
{
    public ChoiceParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed)
        : base(parameter, values, changed)
    {
    }

    public IReadOnlyList<EffectChoice> Choices => Parameter.Choices;

    public EffectChoice? SelectedChoice
    {
        get
        {
            var value = Values.Numbers.TryGetValue(Parameter.Key, out var stored) ? stored : Parameter.Default;
            return Choices.FirstOrDefault(c => Math.Abs(c.Value - value) < 1e-9);
        }
        set
        {
            if (value == null || value == SelectedChoice)
                return;

            Values.Numbers[Parameter.Key] = value.Value;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    public override void Reset()
    {
        Values.Numbers.Remove(Parameter.Key);
        OnPropertyChanged(nameof(SelectedChoice));
    }
}

/// <summary>
/// Переключатель вкл/выкл.
/// </summary>
public sealed class ToggleParameterViewModel : EffectParameterViewModel
{
    public ToggleParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed)
        : base(parameter, values, changed)
    {
    }

    public bool IsOn
    {
        get => (Values.Numbers.TryGetValue(Parameter.Key, out var value) ? value : Parameter.Default) >= 0.5;
        set
        {
            if (value == IsOn)
                return;

            Values.Numbers[Parameter.Key] = value ? 1 : 0;
            OnPropertyChanged();
            NotifyChanged();
        }
    }

    public override void Reset()
    {
        Values.Numbers.Remove(Parameter.Key);
        OnPropertyChanged(nameof(IsOn));
    }
}

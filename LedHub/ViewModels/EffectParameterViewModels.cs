using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Core;
using LedHub.Services.Effects;

namespace LedHub.ViewModels;

/// <summary>
/// Одна настройка выбранного эффекта; правка сразу попадает в значения, которые запущенный эффект читает на каждом кадре.
/// </summary>
public abstract class EffectParameterViewModel : ObservableObject
{
    private readonly Action _changed;

    public EffectParameterViewModel(EffectParameter parameter, EffectValues values, Action changed)
    {
        Parameter = parameter;
        Values = values;
        _changed = changed;
    }

    public EffectParameter Parameter { get; }

    public EffectValues Values { get; }

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

public sealed class NumberParameterViewModel : EffectParameterViewModel
{
    public NumberParameterViewModel(EffectParameter parameter, EffectValues values, Action changed)
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
            OnPropertyChanged(nameof(ValueText));
            NotifyChanged();
        }
    }

    public double Minimum => Parameter.Minimum;

    public double Maximum => Parameter.Maximum;

    /// <summary>
    /// Целые значения (количество, проценты) ползунок выбирает с шагом 1, остальные — плавно.
    /// </summary>
    public bool IsWhole => Parameter.Format.StartsWith("{0:0}", StringComparison.Ordinal);

    public double Step => IsWhole ? 1 : (Maximum - Minimum) / 100;

    public string ValueText => string.Format(CultureInfo.CurrentCulture, Parameter.Format, Value);

    public override void Reset()
    {
        Values.Numbers.Remove(Parameter.Key);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(ValueText));
    }
}

public sealed partial class ColorParameterViewModel : EffectParameterViewModel
{
    private static readonly string[] PresetColors =
    {
        "#FFFFFF", "#FFB46B", "#FF2A2A", "#FF7F00", "#FFE03B",
        "#2BFF6B", "#2BFFE0", "#2B6BFF", "#8B3BFF", "#FF3BC4"
    };

    [ObservableProperty]
    private bool _isPickerOpen;

    public ColorParameterViewModel(EffectParameter parameter, EffectValues values, Action changed)
        : base(parameter, values, changed)
    {
    }

    public RgbColor Color
    {
        get => Values.Colors.TryGetValue(Parameter.Key, out var color) ? color : Parameter.DefaultColor;
        set
        {
            if (value == Color)
                return;

            Values.Colors[Parameter.Key] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Hex));
            NotifyChanged();
        }
    }

    public string Hex => Color.ToHex();

    public IReadOnlyList<string> Presets => PresetColors;

    public override void Reset()
    {
        Values.Colors.Remove(Parameter.Key);
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Hex));
    }

    [RelayCommand]
    private void ApplyPreset(string hex)
    {
        Color = RgbColor.Parse(hex);
    }
}

public sealed class ChoiceParameterViewModel : EffectParameterViewModel
{
    public ChoiceParameterViewModel(EffectParameter parameter, EffectValues values, Action changed)
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

public sealed class ToggleParameterViewModel : EffectParameterViewModel
{
    public ToggleParameterViewModel(EffectParameter parameter, EffectValues values, Action changed)
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

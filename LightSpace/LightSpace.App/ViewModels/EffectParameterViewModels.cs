using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Готовый цвет для быстрого выбора.
/// </summary>
public sealed record ColorPreset(string Title, RgbColor Color)
{
    /// <summary>
    /// Образец цвета для кнопки пресета.
    /// </summary>
    public Brush Swatch { get; } = CreateSwatch(Color);

    private static Brush CreateSwatch(RgbColor color)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));

        brush.Freeze();
        return brush;
    }
}

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
    private static readonly IReadOnlyList<ColorPreset> AllPresets = new ColorPreset[]
    {
        new("Белый", new RgbColor(255, 255, 255)),
        new("Тёплый белый", new RgbColor(255, 180, 100)),
        new("Красный", new RgbColor(255, 0, 0)),
        new("Оранжевый", new RgbColor(255, 90, 0)),
        new("Жёлтый", new RgbColor(255, 200, 0)),
        new("Лаймовый", new RgbColor(140, 255, 0)),
        new("Зелёный", new RgbColor(0, 255, 0)),
        new("Бирюзовый", new RgbColor(0, 255, 160)),
        new("Голубой", new RgbColor(0, 200, 255)),
        new("Синий", new RgbColor(0, 0, 255)),
        new("Фиолетовый", new RgbColor(130, 0, 255)),
        new("Пурпурный", new RgbColor(255, 0, 255)),
        new("Розовый", new RgbColor(255, 40, 120))
    };

    /// <summary>
    /// showsPresets — под выбором цвета показать готовые цвета, например у сплошного цвета.
    /// </summary>
    public ColorParameterViewModel(EffectParameter parameter, EffectValuesConfig values, Action changed, bool showsPresets = false)
        : base(parameter, values, changed)
    {
        Presets = showsPresets ? AllPresets : Array.Empty<ColorPreset>();
        ApplyPresetCommand = new RelayCommand<ColorPreset>(preset =>
        {
            if (preset != null)
                Color = preset.Color;
        });
    }

    /// <summary>
    /// Готовые цвета: щелчок сразу ставит цвет.
    /// </summary>
    public IReadOnlyList<ColorPreset> Presets { get; }

    public bool HasPresets => Presets.Count > 0;

    public IRelayCommand<ColorPreset> ApplyPresetCommand { get; }

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

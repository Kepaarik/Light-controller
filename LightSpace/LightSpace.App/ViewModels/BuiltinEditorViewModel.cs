using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Встроенный эффект со всеми его вариантами: в списке он один, вариант выбирается ползунком.
/// </summary>
/// <param name="Variants">Варианты эффекта по порядку; у эффекта без вариантов — он сам.</param>
public sealed record BuiltinEffectGroup(string Title, IReadOnlyList<BuiltinEffect> Variants)
{
    public override string ToString() => Title;
}

/// <summary>
/// Устройство со встроенными эффектами в редакторе: его эффекты и тот, который оно играет.
/// </summary>
public sealed class BuiltinDeviceViewModel : ObservableObject
{
    private readonly BuiltinConfig _builtin;
    private readonly string _deviceId;
    private readonly Action _changed;

    /// <summary>
    /// effects — встроенные эффекты устройства, хотя бы один; changed вызывается, когда устройству выбирают эффект.
    /// </summary>
    public BuiltinDeviceViewModel(BuiltinConfig builtin, ManagedDevice device, IBuiltinEffectsDevice effects, Action changed)
    {
        _builtin = builtin;
        _deviceId = device.Id;
        _changed = changed;

        Effects = effects.BuiltinEffects;
        HasColor = effects.HasBuiltinColor;

        // Варианты одного эффекта идут под общим названием — в списке они одной строкой
        EffectGroups = Effects
            .GroupBy(e => e.Title)
            .Select(g => new BuiltinEffectGroup(g.Key, g.ToList()))
            .ToList();
    }

    /// <summary>
    /// Встроенные эффекты устройства.
    /// </summary>
    public IReadOnlyList<BuiltinEffect> Effects { get; }

    /// <summary>
    /// Эффекты для списка: варианты одного эффекта собраны в одну строку.
    /// </summary>
    public IReadOnlyList<BuiltinEffectGroup> EffectGroups { get; }

    /// <summary>
    /// Устройство играет встроенные эффекты и одним выбранным цветом.
    /// </summary>
    public bool HasColor { get; }

    /// <summary>
    /// Эффект, который играет устройство; пока его не выбирали — первый эффект устройства.
    /// </summary>
    public BuiltinEffect? SelectedEffect
    {
        get
        {
            var hasChoice = _builtin.Effects.TryGetValue(_deviceId, out var id);

            return hasChoice ? Effects.FirstOrDefault(e => e.Id == id) ?? Effects[0] : Effects[0];
        }
        set
        {
            // Выпадающая панель сбрасывает выбор в null, когда пересобирает список, — эффект от этого не меняется
            if (value == null || value == SelectedEffect)
                return;

            _builtin.Effects[_deviceId] = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGroup));
            OnPropertyChanged(nameof(HasVariants));
            OnPropertyChanged(nameof(VariantCount));
            OnPropertyChanged(nameof(Variant));
            OnPropertyChanged(nameof(Presets));
            OnPropertyChanged(nameof(SelectedPreset));
            _changed();
        }
    }

    /// <summary>
    /// Эффект в списке, который играет устройство; другой эффект начинается со своего первого варианта.
    /// </summary>
    public BuiltinEffectGroup? SelectedGroup
    {
        get => SelectedEffect is { } effect ? EffectGroups.FirstOrDefault(g => g.Variants.Contains(effect)) : null;
        set
        {
            if (value == null || value == SelectedGroup)
                return;

            SelectedEffect = value.Variants[0];
        }
    }

    /// <summary>
    /// У выбранного эффекта несколько вариантов — виден ползунок варианта.
    /// </summary>
    public bool HasVariants => VariantCount > 1;

    public int VariantCount => SelectedGroup?.Variants.Count ?? 0;

    /// <summary>
    /// Номер варианта выбранного эффекта с 1; ползунок отдаёт дробное число.
    /// </summary>
    public double Variant
    {
        get => SelectedGroup is { } group && SelectedEffect is { } effect ? IndexOf(group, effect) + 1 : 1;
        set
        {
            if (SelectedGroup is not { } group)
                return;

            var index = Math.Clamp((int)Math.Round(value) - 1, 0, group.Variants.Count - 1);

            SelectedEffect = group.Variants[index];
        }
    }

    /// <summary>
    /// Пресеты выбранного эффекта — его варианты по номерам с 1; пусто, если вариантов нет.
    /// </summary>
    public IReadOnlyList<int> Presets => HasVariants ? Enumerable.Range(1, VariantCount).ToList() : Array.Empty<int>();

    /// <summary>
    /// Выбранный пресет — номер варианта с 1; null сегменты присылают при пересборке.
    /// </summary>
    public int? SelectedPreset
    {
        get => HasVariants ? (int)Variant : null;
        set
        {
            if (value is { } number)
                Variant = number;
        }
    }

    private static int IndexOf(BuiltinEffectGroup group, BuiltinEffect effect)
    {
        for (var i = 0; i < group.Variants.Count; i++)
        {
            if (group.Variants[i] == effect)
                return i;
        }

        return 0;
    }
}

/// <summary>
/// Редактор содержимого — встроенного эффекта одного устройства: какой эффект оно играет, скорость и цвет. Показать
/// сам эффект в окне нельзя — его играет устройство. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class BuiltinEditorViewModel : ObservableObject
{
    private readonly Func<string, ManagedDevice?> _findDevice;
    private readonly Action _changed;

    private BuiltinConfig? _builtin;
    private BuiltinDeviceViewModel? _device;
    private IReadOnlyList<BuiltinTileViewModel> _tiles = Array.Empty<BuiltinTileViewModel>();
    private bool _isPlaying;

    /// <summary>
    /// findDevice находит известное устройство по идентификатору: эффекты есть только у подключённого; changed
    /// вызывается после каждой правки.
    /// </summary>
    public BuiltinEditorViewModel(Func<string, ManagedDevice?> findDevice, Action changed)
    {
        _findDevice = findDevice;
        _changed = changed;
    }

    /// <summary>
    /// Плитки встроенных эффектов открытого устройства для галереи; варианты одного эффекта — одна плитка.
    /// Пусто, если у устройства встроенных эффектов нет или оно не подключено.
    /// </summary>
    public IReadOnlyList<BuiltinTileViewModel> Tiles
    {
        get => _tiles;
        private set
        {
            if (SetProperty(ref _tiles, value))
                OnPropertyChanged(nameof(HasTiles));
        }
    }

    public bool HasTiles => _tiles.Count > 0;

    /// <summary>
    /// Открытое устройство, которому выбирают эффект плитками; null — встроенных эффектов у него нет или оно не подключено.
    /// </summary>
    public BuiltinDeviceViewModel? Device => _device;

    /// <summary>
    /// Встроенный эффект сейчас играет — тогда в галерее выделена его плитка.
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
                OnPropertyChanged(nameof(SelectedTile));
        }
    }

    /// <summary>
    /// Плитка эффекта, который играет устройство; null — встроенный эффект не играет. Выбор плитки включает её эффект,
    /// даже если это тот же эффект, что был выбран раньше.
    /// </summary>
    public BuiltinTileViewModel? SelectedTile
    {
        get => _isPlaying && Device?.SelectedGroup is { } group ? _tiles.FirstOrDefault(t => t.Group == group) : null;
        set
        {
            // Галерея сбрасывает выбор в null, когда пересобирает плитки
            if (value == null || Device is not { } device)
                return;

            if (device.SelectedGroup == value.Group)
                _changed();
            else
                device.SelectedGroup = value.Group;

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Выбор цвета виден, только если устройство умеет играть встроенный эффект одним цветом.
    /// </summary>
    public bool IsColorShown => _device?.HasColor == true;

    /// <summary>
    /// Эффекты играют одним своим цветом — тогда виден выбор цвета; false — цветами самого эффекта.
    /// </summary>
    public bool UseColor
    {
        get => _builtin?.UseColor == true;
        set
        {
            if (_builtin == null || value == _builtin.UseColor)
                return;

            _builtin.UseColor = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public RgbColor Color
    {
        get => _builtin != null && RgbColor.TryParse(_builtin.Color, out var color) ? color : RgbColor.Parse(BuiltinConfig.DefaultColor);
        set
        {
            if (_builtin == null || value == Color)
                return;

            _builtin.Color = value.ToHex();
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Готовые цвета: щелчок сразу ставит цвет эффекта.
    /// </summary>
    public IReadOnlyList<ColorPreset> ColorPresets => ColorParameterViewModel.AllPresets;

    /// <summary>
    /// Ставит готовый цвет.
    /// </summary>
    [RelayCommand]
    private void ApplyColorPreset(ColorPreset? preset)
    {
        if (preset != null)
            Color = preset.Color;
    }

    public double Speed
    {
        get => _builtin?.Speed ?? BuiltinConfig.DefaultSpeed;
        set => SetNumber(value, b => b.Speed, (b, v) => b.Speed = v);
    }

    /// <summary>
    /// Открывает для правки содержимое устройства deviceId; null — ничего не открыто. Повторный вызов перечитывает
    /// эффекты устройства: после поиска оно могло подключиться или пропасть.
    /// </summary>
    public void Edit(ContentConfig? content, string? deviceId)
    {
        _builtin = content?.Builtin;

        var device = deviceId != null ? _findDevice(deviceId) : null;

        _device = _builtin != null && device?.Device is IBuiltinEffectsDevice { BuiltinEffects.Count: > 0 } effects
            ? new BuiltinDeviceViewModel(_builtin, device, effects, _changed)
            : null;

        Tiles = _device != null
            ? _device.EffectGroups.Select(g => new BuiltinTileViewModel(g)).ToList()
            : Array.Empty<BuiltinTileViewModel>();

        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(SelectedTile));
        OnPropertyChanged(nameof(IsColorShown));
        OnPropertyChanged(nameof(UseColor));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Speed));
    }

    private void SetNumber(double value, Func<BuiltinConfig, int> get, Action<BuiltinConfig, int> set, [CallerMemberName] string? name = null)
    {
        var number = (int)Math.Round(value);

        if (_builtin == null || number == get(_builtin))
            return;

        set(_builtin, number);
        OnPropertyChanged(name);
        _changed();
    }
}

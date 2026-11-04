using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Цвета встроенного эффекта в сегментах: свои цвета эффекта или один выбранный цвет.
/// </summary>
public sealed record BuiltinColorsOption(bool UseColor, string Title)
{
    public static readonly IReadOnlyList<BuiltinColorsOption> All = new BuiltinColorsOption[]
    {
        new(false, "Цвета эффекта"),
        new(true, "Свой цвет")
    };

    public override string ToString() => Title;
}

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

        Name = device.Name;
        Protocol = device.Protocol;
        Effects = effects.BuiltinEffects;
        HasColor = effects.HasBuiltinColor;

        // Варианты одного эффекта идут под общим названием — в списке они одной строкой
        EffectGroups = Effects
            .GroupBy(e => e.Title)
            .Select(g => new BuiltinEffectGroup(g.Key, g.ToList()))
            .ToList();
    }

    public string Name { get; }

    /// <summary>
    /// Способ подключения и тип контроллера.
    /// </summary>
    public string Protocol { get; }

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
/// Редактор содержимого — встроенного эффекта: эффект каждого подключённого устройства, которое их умеет, скорость
/// и цвет. Показать сам эффект в окне нельзя — его играет устройство. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class BuiltinEditorViewModel : ObservableObject
{
    private readonly DeviceManager _manager;
    private readonly Action _changed;

    private BuiltinConfig? _builtin;
    private IReadOnlyList<BuiltinDeviceViewModel> _devices = Array.Empty<BuiltinDeviceViewModel>();
    private IReadOnlyList<BuiltinTileViewModel> _tiles = Array.Empty<BuiltinTileViewModel>();
    private bool _isPlaying;

    // Открытое устройство: редактор показывает только его; null — все устройства со встроенными эффектами
    private string? _deviceId;

    /// <summary>
    /// manager — откуда берутся подключённые устройства и их эффекты; changed вызывается после каждой правки.
    /// </summary>
    public BuiltinEditorViewModel(DeviceManager manager, Action changed)
    {
        _manager = manager;
        _changed = changed;

        // После поиска могли найтись или пропасть устройства со встроенными эффектами
        _manager.DevicesChanged += (_, _) => RefreshDevices();
    }

    /// <summary>
    /// Подключённые устройства со встроенными эффектами.
    /// </summary>
    public IReadOnlyList<BuiltinDeviceViewModel> Devices
    {
        get => _devices;
        private set
        {
            if (SetProperty(ref _devices, value))
            {
                OnPropertyChanged(nameof(HasDevices));
                OnPropertyChanged(nameof(IsColorShown));
            }
        }
    }

    public bool HasDevices => _devices.Count > 0;

    /// <summary>
    /// Плитки встроенных эффектов открытого устройства для галереи; варианты одного эффекта — одна плитка.
    /// Пусто, если открыто не одно устройство.
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
    /// Открытое устройство, которому выбирают эффект плитками; null — открыто не одно устройство.
    /// </summary>
    public BuiltinDeviceViewModel? Device => IsOneDevice && _devices.Count == 1 ? _devices[0] : null;

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
    /// Редактор открыт для одного устройства: его название и подсказки про другие устройства не нужны.
    /// </summary>
    public bool IsOneDevice => _deviceId != null;

    /// <summary>
    /// Выбор цвета виден, только если его понимает хоть одно показанное устройство.
    /// </summary>
    public bool IsColorShown => _devices.Any(d => d.HasColor);

    public IReadOnlyList<BuiltinColorsOption> ColorOptions => BuiltinColorsOption.All;

    public BuiltinColorsOption? SelectedColors
    {
        get => _builtin == null ? null : ColorOptions.First(o => o.UseColor == _builtin.UseColor);
        set
        {
            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (value == null || _builtin == null || value.UseColor == _builtin.UseColor)
                return;

            _builtin.UseColor = value.UseColor;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseColor));
            _changed();
        }
    }

    /// <summary>
    /// Эффекты играют одним цветом — тогда виден выбор цвета.
    /// </summary>
    public bool UseColor => _builtin?.UseColor == true;

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
    /// Открывает содержимое для правки; null — ничего не открыто. deviceId — показывать только это устройство;
    /// null — все устройства со встроенными эффектами.
    /// </summary>
    public void Edit(ContentConfig? content, string? deviceId = null)
    {
        _builtin = content?.Builtin;
        _deviceId = deviceId;

        RefreshDevices();
        OnPropertyChanged(nameof(IsOneDevice));
        OnPropertyChanged(nameof(SelectedColors));
        OnPropertyChanged(nameof(UseColor));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Speed));
    }

    /// <summary>
    /// Собирает строки подключённых устройств со встроенными эффектами для открытого содержимого.
    /// </summary>
    private void RefreshDevices()
    {
        if (_builtin == null)
        {
            Devices = Array.Empty<BuiltinDeviceViewModel>();
            RefreshTiles();
            return;
        }

        var builtin = _builtin;

        Devices = _manager.Devices
            .Where(d => d.Device is IBuiltinEffectsDevice { BuiltinEffects.Count: > 0 } && (_deviceId == null || d.Id == _deviceId))
            .Select(d => new BuiltinDeviceViewModel(builtin, d, (IBuiltinEffectsDevice)d.Device!, _changed))
            .ToList();

        RefreshTiles();
    }

    /// <summary>
    /// Собирает плитки эффектов открытого устройства.
    /// </summary>
    private void RefreshTiles()
    {
        Tiles = Device is { } device
            ? device.EffectGroups.Select(g => new BuiltinTileViewModel(g)).ToList()
            : Array.Empty<BuiltinTileViewModel>();

        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(SelectedTile));
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

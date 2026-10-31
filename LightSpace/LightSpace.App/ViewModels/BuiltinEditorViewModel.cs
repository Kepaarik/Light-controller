using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
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
/// Устройство со встроенными эффектами в редакторе: его эффекты и тот, который оно играет в сценарии.
/// </summary>
public sealed class BuiltinDeviceViewModel : ObservableObject
{
    private readonly BuiltinConfig _builtin;
    private readonly string _deviceId;
    private readonly Action _changed;

    /// <summary>
    /// effects — встроенные эффекты устройства, хотя бы один; changed вызывается, когда устройству выбирают эффект.
    /// </summary>
    public BuiltinDeviceViewModel(BuiltinConfig builtin, ManagedDevice device, IReadOnlyList<BuiltinEffect> effects, Action changed)
    {
        _builtin = builtin;
        _deviceId = device.Id;
        _changed = changed;

        Name = device.Name;
        Protocol = device.Protocol;
        Effects = effects;
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
    /// Эффект, который устройство играет в сценарии; пока его не выбирали — первый эффект устройства.
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
            _changed();
        }
    }
}

/// <summary>
/// Редактор содержимого — встроенного эффекта: эффект каждого подключённого устройства, которое их умеет, скорость
/// и цвет. Показать сам эффект в окне нельзя — его играет устройство. Все правки сразу попадают в содержимое.
/// </summary>
public sealed class BuiltinEditorViewModel : ObservableObject
{
    private readonly DeviceManager _manager;
    private readonly Action _changed;

    private BuiltinConfig? _builtin;
    private IReadOnlyList<BuiltinDeviceViewModel> _devices = Array.Empty<BuiltinDeviceViewModel>();

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
                OnPropertyChanged(nameof(HasDevices));
        }
    }

    public bool HasDevices => _devices.Count > 0;

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

    public double Speed
    {
        get => _builtin?.Speed ?? BuiltinConfig.DefaultSpeed;
        set => SetNumber(value, b => b.Speed, (b, v) => b.Speed = v);
    }

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _builtin = content?.Builtin;

        RefreshDevices();
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
            return;
        }

        var builtin = _builtin;

        Devices = _manager.Devices
            .Where(d => d.Device is IBuiltinEffectsDevice { BuiltinEffects.Count: > 0 })
            .Select(d => new BuiltinDeviceViewModel(builtin, d, ((IBuiltinEffectsDevice)d.Device!).BuiltinEffects, _changed))
            .ToList();
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

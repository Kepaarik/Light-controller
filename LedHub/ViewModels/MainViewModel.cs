using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Core;
using LedHub.Protocols.Tuya;
using LedHub.Services;

namespace LedHub.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly string[] PresetColors =
    {
        "#FFFFFF", "#FFB46B", "#FF0000", "#FF7F00", "#FFFF00",
        "#00FF00", "#00FFFF", "#0000FF", "#8B00FF", "#FF00FF"
    };

    private const int ColorTab = 0;
    private const int LedsTab = 2;

    private static readonly int[] BrightnessPresetValues = { 10, 25, 50, 75, 100 };

    private readonly EffectOption[] _effectOptions =
    {
        new(EffectKind.Rainbow, "Радуга", "#FF3B5C", "#3B7BFF"),
        new(EffectKind.ColorCycle, "Смена цветов", "#FFD23B", "#B23BFF"),
        new(EffectKind.Breathing, "Дыхание", "#5B7BFF", "#101634"),
        new(EffectKind.Wave, "Волна", "#3BE8FF", "#0F2F4F"),
        new(EffectKind.Comet, "Комета", "#FFF3B0", "#2A1E66"),
        new(EffectKind.Twinkle, "Звёзды", "#FFFFFF", "#1B1D3A"),
        new(EffectKind.Fire, "Огонь", "#FFC03B", "#E02400"),
        new(EffectKind.Police, "Полиция", "#FF2A2A", "#2A5BFF"),
        new(EffectKind.Strobe, "Стробоскоп", "#FFFFFF", "#3A3A44"),
        new(EffectKind.Candle, "Свеча", "#FFC46B", "#7A3A00")
    };

    private readonly DiscoveryService _discovery = new();
    private readonly EffectRunner _effects = new();
    private readonly AppConfig _config = ConfigStore.Load();

    // Пока панель загружает значения выбранной цели, изменения слайдеров не отправляются на ленты
    private bool _isLoadingTarget;
    private Func<Task>? _pendingSend;
    private bool _isSending;

    [ObservableProperty]
    private string _statusText = "Нажмите «Найти устройства», чтобы начать поиск";

    [ObservableProperty]
    private string _newGroupName = "";

    [ObservableProperty]
    private DeviceItemViewModel? _selectedDevice;

    [ObservableProperty]
    private GroupItemViewModel? _selectedGroup;

    [ObservableProperty]
    private ILightTarget? _target;

    [ObservableProperty]
    private int _red = 255;

    [ObservableProperty]
    private int _green = 255;

    [ObservableProperty]
    private int _blue = 255;

    [ObservableProperty]
    private int _brightness = 100;

    [ObservableProperty]
    private string _tuyaLocalKey = "";

    [ObservableProperty]
    private TuyaDpScheme _tuyaDpScheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEffectRunning))]
    [NotifyPropertyChangedFor(nameof(EffectStatus))]
    private string? _activeEffect;

    [ObservableProperty]
    private double _effectSpeed = 1;

    [ObservableProperty]
    private HardwareEffect? _selectedHardwareEffect;

    [ObservableProperty]
    private int _hardwareEffectSpeed = 50;

    /// <summary>
    /// Вкладка панели управления: 0 — цвет, 1 — эффекты, 2 — светодиоды, 3 — настройки.
    /// </summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _renameText = "";

    public MainViewModel()
    {
        foreach (var group in _config.Groups)
            Groups.Add(new GroupItemViewModel(group, FindDevice));
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();

    public ObservableCollection<GroupItemViewModel> Groups { get; } = new();

    public IReadOnlyList<string> Presets => PresetColors;

    public IReadOnlyList<TuyaDpScheme> TuyaDpSchemes { get; } = Enum.GetValues<TuyaDpScheme>();

    public bool HasTarget => Target != null;

    public string PreviewHex => CurrentColor.ToHex();

    /// <summary>
    /// Цвет, выбранный на цветовом круге; хранится в тех же каналах, что и RGB-слайдеры.
    /// </summary>
    public RgbColor SelectedColor
    {
        get => CurrentColor;
        set
        {
            if (value == CurrentColor)
                return;

            _isLoadingTarget = true;
            Red = value.R;
            Green = value.G;
            Blue = value.B;
            _isLoadingTarget = false;

            OnColorEdited();
        }
    }

    public bool IsGroupSelected => SelectedGroup != null;

    public TuyaDevice? SelectedTuya => SelectedDevice?.Device as TuyaDevice;

    public bool IsTuyaSelected => SelectedTuya != null;

    public IReadOnlyList<EffectOption> Effects => _effectOptions;

    public IReadOnlyList<int> BrightnessPresets => BrightnessPresetValues;

    /// <summary>
    /// Встроенные эффекты контроллера выбранной ленты; пусто для групп и лент без таких эффектов.
    /// </summary>
    public IReadOnlyList<HardwareEffect> HardwareEffects =>
        (Target as DeviceItemViewModel)?.HardwareEffects?.HardwareEffects ?? Array.Empty<HardwareEffect>();

    public bool HasHardwareEffects => HardwareEffects.Count > 0;

    private bool IsLedsTabSelected => SelectedTabIndex == LedsTab;

    public bool IsEffectRunning => ActiveEffect != null;

    public string EffectStatus => ActiveEffect != null ? $"Сейчас: {ActiveEffect}" : "Эффект не запущен";

    /// <summary>
    /// Светодиоды выбранной адресной ленты; пусто, если выбрана группа или обычная лента.
    /// </summary>
    public ObservableCollection<LedCellViewModel> LedCells { get; } = new();

    public bool IsAddressableSelected => LedCells.Count > 0;

    private RgbColor CurrentColor => new((byte)Red, (byte)Green, (byte)Blue);

    /// <summary>
    /// Останавливает эффекты и закрывает соединения со всеми лентами.
    /// </summary>
    public void Shutdown()
    {
        _effects.Stop();

        foreach (var item in Devices)
            item.Device.Dispose();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        // Старые соединения закрываем до поиска: BLE-ленты не видны, пока к ним подключены, а COM-порт занят
        Target = null;
        SelectedDevice = null;
        SelectedGroup = null;
        Shutdown();
        Devices.Clear();

        StatusText = "Идёт поиск: Bluetooth, Wi-Fi (Magic Home, Govee, Tuya), USB…";

        var result = await _discovery.DiscoverAllAsync(CancellationToken.None);

        foreach (var device in result.Devices.OrderBy(d => d.Protocol).ThenBy(d => d.Name))
        {
            if (device is TuyaDevice tuya)
                ApplyTuyaSettings(tuya);

            var item = new DeviceItemViewModel(device);
            if (_config.DeviceNames.TryGetValue(device.Id, out var customName))
                item.CustomName = customName;

            Devices.Add(item);
        }

        foreach (var group in Groups)
            group.Refresh();

        StatusText = result.Errors.Count == 0
            ? $"Найдено устройств: {Devices.Count}"
            : $"Найдено устройств: {Devices.Count}. Проблемы: {string.Join("; ", result.Errors)}";
    }

    [RelayCommand]
    private async Task TurnOnAsync()
    {
        StopEffect();
        await SendToTargetAsync(d => d.SetPowerAsync(true));

        // Включение восстанавливает сплошной цвет — возвращаем ленте её раскраску по светодиодам
        if (Target is DeviceItemViewModel { IsPixelMode: true } item)
            await SendPixelsAsync(item);
    }

    [RelayCommand]
    private async Task TurnOffAsync()
    {
        StopEffect();
        await SendToTargetAsync(d => d.SetPowerAsync(false));
    }

    [RelayCommand]
    private void StartEffect(EffectOption effect)
    {
        if (Target == null)
            return;

        var devices = Target.GetDevices().Where(d => d.IsReady).ToList();
        if (devices.Count == 0)
        {
            StatusText = $"{Target.Title}: нет доступных устройств";
            return;
        }

        _effects.Start(effect.Kind, devices, () => new EffectSettings(CurrentColor, Brightness, EffectSpeed));

        foreach (var option in _effectOptions)
            option.IsActive = option == effect;

        ActiveEffect = effect.Title;
        StatusText = $"{Target.Title}: эффект «{effect.Title}»";
    }

    [RelayCommand]
    private void StopEffect()
    {
        _effects.Stop();

        foreach (var option in _effectOptions)
            option.IsActive = false;

        ActiveEffect = null;
    }

    [RelayCommand]
    private async Task StartHardwareEffectAsync()
    {
        if (Target is not DeviceItemViewModel { HardwareEffects: { } device } item || SelectedHardwareEffect == null)
            return;

        // Программный эффект перебивал бы встроенный своими кадрами
        StopEffect();

        try
        {
            await device.StartHardwareEffectAsync(SelectedHardwareEffect.Id, HardwareEffectSpeed);
            StatusText = $"{item.Title}: встроенный эффект «{SelectedHardwareEffect.Title}»";
        }
        catch (Exception e)
        {
            StatusText = $"{item.Title}: {e.Message}";
        }
    }

    [RelayCommand]
    private void SetBrightness(int value)
    {
        Brightness = value;
    }

    [RelayCommand]
    private void Rename()
    {
        var name = RenameText.Trim();

        switch (Target)
        {
            case DeviceItemViewModel device:
                // Пустое имя возвращает название, которое сообщает само устройство
                device.CustomName = name.Length > 0 ? name : null;

                if (device.CustomName == null)
                    _config.DeviceNames.Remove(device.Id);
                else
                    _config.DeviceNames[device.Id] = name;

                // Группы хранят имя участника, чтобы показывать его, даже когда устройство не найдено
                foreach (var member in _config.Groups.SelectMany(g => g.Members).Where(m => m.Id == device.Id))
                    member.Name = device.Name;

                foreach (var group in Groups)
                    group.Refresh();

                RenameText = device.Name;
                break;

            case GroupItemViewModel group when name.Length > 0:
                group.Rename(name);
                break;

            default:
                return;
        }

        ConfigStore.Save(_config);
        StatusText = "Название сохранено";
    }

    [RelayCommand]
    private void PaintLed(LedCellViewModel cell)
    {
        if (Target is not DeviceItemViewModel { LedColors: { } colors } item)
            return;

        StopEffect();

        cell.Color = CurrentColor;
        colors[cell.Index] = CurrentColor;
        item.IsPixelMode = true;

        _ = QueueSendAsync(() => SendPixelsAsync(item));
    }

    [RelayCommand]
    private void FillLeds() => SetAllLeds(CurrentColor, isPixelMode: false);

    [RelayCommand]
    private void ClearLeds() => SetAllLeds(new RgbColor(0, 0, 0), isPixelMode: true);

    [RelayCommand]
    private void ApplyPreset(string hex)
    {
        var color = RgbColor.Parse(hex);

        _isLoadingTarget = true;
        Red = color.R;
        Green = color.G;
        Blue = color.B;
        _isLoadingTarget = false;

        OnColorEdited();
    }

    [RelayCommand]
    private void CreateGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0)
        {
            StatusText = "Введите название группы";
            return;
        }

        var config = new GroupConfig { Name = name };
        AddCheckedDevices(config);
        _config.Groups.Add(config);

        var group = new GroupItemViewModel(config, FindDevice);
        Groups.Add(group);
        SelectedGroup = group;

        NewGroupName = "";
        ConfigStore.Save(_config);
        StatusText = $"Группа «{name}» создана";
    }

    [RelayCommand]
    private void AddCheckedToGroup()
    {
        if (SelectedGroup == null)
        {
            StatusText = "Сначала выберите группу";
            return;
        }

        AddCheckedDevices(SelectedGroup.Config);
        SelectedGroup.Refresh();
        ConfigStore.Save(_config);
    }

    [RelayCommand]
    private void RemoveMember(GroupMemberViewModel member)
    {
        if (SelectedGroup == null)
            return;

        SelectedGroup.Config.Members.RemoveAll(m => m.Id == member.Id);
        SelectedGroup.Refresh();
        ConfigStore.Save(_config);
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup == null)
            return;

        var group = SelectedGroup;
        if (Target == group)
            Target = null;

        _config.Groups.Remove(group.Config);
        Groups.Remove(group);
        ConfigStore.Save(_config);
        StatusText = $"Группа «{group.Name}» удалена";
    }

    [RelayCommand]
    private void SaveTuyaSettings()
    {
        if (SelectedDevice == null || SelectedTuya == null)
            return;

        _config.Tuya[SelectedTuya.DeviceId] = new TuyaDeviceConfig { LocalKey = TuyaLocalKey.Trim(), DpScheme = TuyaDpScheme };
        ConfigStore.Save(_config);

        ApplyTuyaSettings(SelectedTuya);
        SelectedDevice.RefreshStatus();
        StatusText = SelectedTuya.StatusText ?? "Настройки Tuya сохранены";
    }

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        if (value != null)
        {
            SelectedGroup = null;
            Target = value;
        }

        if (value?.Device is TuyaDevice tuya)
        {
            TuyaLocalKey = tuya.LocalKey ?? "";
            TuyaDpScheme = tuya.DpScheme;
        }

        OnPropertyChanged(nameof(SelectedTuya));
        OnPropertyChanged(nameof(IsTuyaSelected));
    }

    partial void OnSelectedGroupChanged(GroupItemViewModel? value)
    {
        OnPropertyChanged(nameof(IsGroupSelected));

        if (value == null)
            return;

        SelectedDevice = null;
        Target = value;
    }

    partial void OnTargetChanged(ILightTarget? value)
    {
        // Эффект привязан к выбранной ленте или группе, поэтому смена выбора его останавливает
        StopEffect();
        LoadLedCells();

        // Вкладки светодиодов нет у обычных лент и групп
        if (SelectedTabIndex == LedsTab && !IsAddressableSelected)
            SelectedTabIndex = ColorTab;

        SelectedHardwareEffect = HardwareEffects.FirstOrDefault();
        RenameText = value switch
        {
            DeviceItemViewModel device => device.Name,
            GroupItemViewModel group => group.Name,
            _ => ""
        };

        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(HardwareEffects));
        OnPropertyChanged(nameof(HasHardwareEffects));

        if (value == null)
            return;

        _isLoadingTarget = true;
        Red = value.Color.R;
        Green = value.Color.G;
        Blue = value.Color.B;
        Brightness = value.Brightness;
        _isLoadingTarget = false;

        OnPropertyChanged(nameof(PreviewHex));
        OnPropertyChanged(nameof(SelectedColor));
    }

    partial void OnRedChanged(int value) => OnColorEdited();

    partial void OnGreenChanged(int value) => OnColorEdited();

    partial void OnBlueChanged(int value) => OnColorEdited();

    partial void OnBrightnessChanged(int value) => OnColorEdited(isBrightnessOnly: true);

    private void OnColorEdited(bool isBrightnessOnly = false)
    {
        OnPropertyChanged(nameof(PreviewHex));
        OnPropertyChanged(nameof(SelectedColor));

        if (_isLoadingTarget || Target == null)
            return;

        Target.Color = CurrentColor;
        Target.Brightness = Brightness;

        // Во время эффекта цвет и яркость читаются эффектом на каждом кадре
        if (IsEffectRunning)
            return;

        if (Target is DeviceItemViewModel { Strip: not null } stripItem)
        {
            // В редакторе светодиодов круг выбирает цвет кисти, а не заливает ленту
            if (IsLedsTabSelected && !isBrightnessOnly)
                return;

            // Яркость применяется ко всей раскраске, не стирая её
            if (stripItem.IsPixelMode && isBrightnessOnly)
            {
                _ = QueueSendAsync(() => SendPixelsAsync(stripItem));
                return;
            }
        }

        // Участники группы запоминают цвет группы, чтобы при выборе по отдельности панель показывала актуальное
        if (Target is GroupItemViewModel group)
        {
            foreach (var device in Devices.Where(d => group.Config.Members.Any(m => m.Id == d.Id)))
            {
                device.Color = CurrentColor;
                device.Brightness = Brightness;
                FillLedColors(device, CurrentColor);
            }
        }
        else if (Target is DeviceItemViewModel item)
        {
            FillLedColors(item, CurrentColor);

            foreach (var cell in LedCells)
                cell.Color = CurrentColor;
        }

        var color = CurrentColor;
        var brightness = Brightness;

        _ = QueueSendAsync(() => SendToTargetAsync(d => d.SetColorAsync(color, brightness)));
    }

    /// <summary>
    /// Отправляет на ленты без накопления очереди: пока идёт отправка, слайдер может сдвинуться
    /// много раз, но после неё уйдёт только последнее изменение.
    /// </summary>
    private async Task QueueSendAsync(Func<Task> send)
    {
        _pendingSend = send;
        if (_isSending)
            return;

        _isSending = true;
        try
        {
            while (_pendingSend != null)
            {
                var next = _pendingSend;
                _pendingSend = null;

                await next();
            }
        }
        finally
        {
            _isSending = false;
        }
    }

    private async Task SendPixelsAsync(DeviceItemViewModel item)
    {
        if (item.Strip == null || item.LedColors == null)
            return;

        var colors = item.LedColors.ToArray();

        try
        {
            await item.Strip.SetLedColorsAsync(colors, item.Brightness);
            StatusText = $"{item.Title}: готово";
        }
        catch (Exception e)
        {
            StatusText = $"{item.Title}: {e.Message}";
        }
    }

    private void SetAllLeds(RgbColor color, bool isPixelMode)
    {
        if (Target is not DeviceItemViewModel { LedColors: { } colors } item)
            return;

        StopEffect();

        Array.Fill(colors, color);
        foreach (var cell in LedCells)
            cell.Color = color;

        item.IsPixelMode = isPixelMode;

        _ = QueueSendAsync(() => SendPixelsAsync(item));
    }

    /// <summary>
    /// Запоминает, что лента залита одним цветом.
    /// </summary>
    private static void FillLedColors(DeviceItemViewModel item, RgbColor color)
    {
        if (item.LedColors != null)
            Array.Fill(item.LedColors, color);

        item.IsPixelMode = false;
    }

    private void LoadLedCells()
    {
        LedCells.Clear();

        if (Target is DeviceItemViewModel { Strip: { } strip } item)
        {
            item.LedColors ??= Enumerable.Repeat(item.Color, strip.LedCount).ToArray();

            for (var i = 0; i < item.LedColors.Length; i++)
                LedCells.Add(new LedCellViewModel(i, item.LedColors[i]));
        }

        OnPropertyChanged(nameof(IsAddressableSelected));
    }

    private async Task SendToTargetAsync(Func<LedDevice, Task> action)
    {
        if (Target == null)
            return;

        var title = Target.Title;
        var devices = Target.GetDevices();
        var failures = new List<string>();

        if (devices.Count == 0)
        {
            StatusText = $"{title}: нет доступных устройств";
            return;
        }

        // Все ленты цели получают команду одновременно
        await Task.WhenAll(devices.Select(async device =>
        {
            if (!device.IsReady)
            {
                lock (failures)
                    failures.Add($"{device.Name}: {device.StatusText}");

                return;
            }

            try
            {
                await action(device);
            }
            catch (Exception e)
            {
                lock (failures)
                    failures.Add($"{device.Name}: {e.Message}");
            }
        }));

        StatusText = failures.Count == 0
            ? $"{title}: готово"
            : $"{title}: {string.Join("; ", failures)}";
    }

    private void AddCheckedDevices(GroupConfig group)
    {
        foreach (var device in Devices.Where(d => d.IsChecked))
        {
            if (group.Members.All(m => m.Id != device.Id))
                group.Members.Add(new GroupMemberConfig { Id = device.Id, Name = device.Name });

            device.IsChecked = false;
        }
    }

    private void ApplyTuyaSettings(TuyaDevice device)
    {
        if (!_config.Tuya.TryGetValue(device.DeviceId, out var settings))
            return;

        device.LocalKey = settings.LocalKey;
        device.DpScheme = settings.DpScheme;
    }

    private DeviceItemViewModel? FindDevice(string id)
    {
        return Devices.FirstOrDefault(d => d.Id == id);
    }
}

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

    private readonly DiscoveryService _discovery = new();
    private readonly AppConfig _config = ConfigStore.Load();

    // Пока панель загружает значения выбранной цели, изменения слайдеров не отправляются на ленты
    private bool _isLoadingTarget;
    private bool _isColorPending;
    private bool _isSendingColor;

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

    private RgbColor CurrentColor => new((byte)Red, (byte)Green, (byte)Blue);

    /// <summary>
    /// Закрывает соединения со всеми лентами.
    /// </summary>
    public void Shutdown()
    {
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

            Devices.Add(new DeviceItemViewModel(device));
        }

        foreach (var group in Groups)
            group.Refresh();

        StatusText = result.Errors.Count == 0
            ? $"Найдено устройств: {Devices.Count}"
            : $"Найдено устройств: {Devices.Count}. Проблемы: {string.Join("; ", result.Errors)}";
    }

    [RelayCommand]
    private Task TurnOnAsync() => SendToTargetAsync(d => d.SetPowerAsync(true));

    [RelayCommand]
    private Task TurnOffAsync() => SendToTargetAsync(d => d.SetPowerAsync(false));

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
        OnPropertyChanged(nameof(HasTarget));

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

    partial void OnBrightnessChanged(int value) => OnColorEdited();

    private void OnColorEdited()
    {
        OnPropertyChanged(nameof(PreviewHex));
        OnPropertyChanged(nameof(SelectedColor));

        if (_isLoadingTarget || Target == null)
            return;

        Target.Color = CurrentColor;
        Target.Brightness = Brightness;

        // Участники группы запоминают цвет группы, чтобы при выборе по отдельности панель показывала актуальное
        if (Target is GroupItemViewModel group)
        {
            foreach (var device in Devices.Where(d => group.Config.Members.Any(m => m.Id == d.Id)))
            {
                device.Color = CurrentColor;
                device.Brightness = Brightness;
            }
        }

        _ = PushColorAsync();
    }

    /// <summary>
    /// Отправляет цвет на ленты, не накапливая очередь: пока идёт отправка, слайдер может сдвинуться
    /// много раз, но после неё уйдёт только последнее значение.
    /// </summary>
    private async Task PushColorAsync()
    {
        _isColorPending = true;
        if (_isSendingColor)
            return;

        _isSendingColor = true;
        try
        {
            while (_isColorPending)
            {
                _isColorPending = false;

                var color = CurrentColor;
                var brightness = Brightness;

                await SendToTargetAsync(d => d.SetColorAsync(color, brightness));
            }
        }
        finally
        {
            _isSendingColor = false;
        }
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

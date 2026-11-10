using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Главное окно и меню трея: разделы, настройки, общие команды.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly DeviceManager _deviceManager;
    private readonly FrameLoop _frameLoop;
    private readonly SourceHub _sources = new();

    // Окно открыто и не свёрнуто
    private bool _isWindowShown;

    // Компьютер спит: свет погашен, но общий выключатель подсветки в настройках не тронут
    private bool _isAsleep;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Devices;

    // Устройство, выбранное в левом меню; null — открыта не страница устройства
    [ObservableProperty]
    private string? _selectedDeviceId;

    // Устройство, чья страница открыта: список в меню пересобирается и на время сбрасывает выбор
    private string? _openedDeviceId;

    public MainViewModel(AppConfig config)
    {
        _config = config;
        Settings = new SettingsViewModel(config);

        // Страницы «Сцена» пока нет: у каждого устройства своё содержимое, оно выбирается на странице устройства
        config.Mode = SceneMode.Devices;

        DropUnavailableContent();

        _deviceManager = new DeviceManager(config);

        // Профиль показывает устройства по их названиям, поэтому читается после списка устройств
        foreach (var profile in config.Profiles)
            Profiles.Add(NewProfileOption(profile));

        _frameLoop = new FrameLoop(FrameSnapshot.Build(config, _deviceManager.Devices), _sources);
        Devices = new DevicesViewModel(_deviceManager, _frameLoop, Settings.ScheduleSave, OpenDevice);
        Scene = new SceneViewModel(config, _deviceManager, _frameLoop, _sources, OnLightingEdited);
        Monitors = new MonitorsViewModel(config, _deviceManager, OnContentChanged);

        Settings.PropertyChanged += OnSettingsChanged;
        _deviceManager.DevicesChanged += OnDevicesChanged;

        RefreshDeviceNav();
    }

    /// <summary>
    /// Найденные устройства в левом меню, под разделами.
    /// </summary>
    public ObservableCollection<DeviceNavOption> DeviceNav { get; } = new();

    /// <summary>
    /// Разделы в левом меню окна.
    /// </summary>
    public IReadOnlyList<PageOption> Pages { get; } = new PageOption[]
    {
        new(AppPage.Devices, "Устройства", ""),
        new(AppPage.Monitors, "Мониторы", ""),
        new(AppPage.Profiles, "Профили", "")
    };

    /// <summary>
    /// Разделы внизу левого меню.
    /// </summary>
    public IReadOnlyList<PageOption> BottomPages { get; } = new PageOption[]
    {
        new(AppPage.Settings, "Настройки", "")
    };

    /// <summary>
    /// Левое меню свёрнуто до значков.
    /// </summary>
    public bool IsMenuCollapsed
    {
        get => _config.IsMenuCollapsed;
        set
        {
            if (_config.IsMenuCollapsed == value)
                return;

            _config.IsMenuCollapsed = value;
            OnPropertyChanged();
            Settings.ScheduleSave();
        }
    }

    public SettingsViewModel Settings { get; }

    public DevicesViewModel Devices { get; }

    public SceneViewModel Scene { get; }

    public MonitorsViewModel Monitors { get; }

    /// <summary>
    /// Сообщает, видно ли окно: живые цвета и превью считаются, только пока на них можно посмотреть.
    /// </summary>
    public void SetWindowShown(bool isShown)
    {
        _isWindowShown = isShown;
        UpdateLive();
    }

    /// <summary>
    /// Компьютер уходит в сон или просыпается: на время сна свет на всех устройствах гаснет,
    /// после пробуждения возвращается, если подсветка включена в настройках.
    /// </summary>
    public void SetAsleep(bool isAsleep)
    {
        if (_isAsleep == isAsleep)
            return;

        _isAsleep = isAsleep;

        _deviceManager.SetLightingEnabled(IsLightOn);
        UpdateFrames();
    }

    /// <summary>
    /// Свет должен гореть: подсветка включена и компьютер не спит.
    /// </summary>
    private bool IsLightOn => Settings.LightingEnabled && !_isAsleep;

    /// <summary>
    /// Останавливает поток кадров, вывод на устройства и закрывает соединения с ними.
    /// </summary>
    public void Shutdown()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _deviceManager.DevicesChanged -= OnDevicesChanged;
        Scene.SetLive(false, false);

        // Сначала поток кадров: иначе он клал бы кадры в уже закрытые выводы устройств
        _frameLoop.Dispose();
        _sources.Dispose();
        _deviceManager.Dispose();
    }

    /// <summary>
    /// Выключает всю подсветку или включает её обратно.
    /// </summary>
    [RelayCommand]
    private void ToggleLighting()
    {
        Settings.LightingEnabled = !Settings.LightingEnabled;
    }

    /// <summary>
    /// Сохранённые профили подсветки.
    /// </summary>
    public ObservableCollection<ProfileOption> Profiles { get; } = new();

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Общая яркость всей подсветки, 0–100 %.
    /// </summary>
    public double MasterBrightness
    {
        get => _config.MasterBrightness;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == _config.MasterBrightness)
                return;

            _config.MasterBrightness = brightness;
            OnPropertyChanged();
            OnContentChanged();
        }
    }

    /// <summary>
    /// Профиль, открытый в разделе «Профили».
    /// </summary>
    [ObservableProperty]
    private ProfileOption? _selectedProfile;

    public bool HasSelectedProfile => SelectedProfile != null;

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        OnPropertyChanged(nameof(HasSelectedProfile));
    }

    /// <summary>
    /// Сохраняет то, что сейчас играет на устройствах, новым профилем и открывает его.
    /// </summary>
    [RelayCommand]
    private void NewProfile()
    {
        var profile = NewProfileOption(_config.SaveProfile($"Профиль {Profiles.Count + 1}"));

        Profiles.Add(profile);
        OnPropertyChanged(nameof(HasProfiles));
        SetActiveProfile(profile);
        SelectedProfile = profile;
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Записывает в профиль то, что сейчас играет на устройствах, вместо сохранённого раньше.
    /// </summary>
    [RelayCommand]
    private async Task UpdateProfileAsync(ProfileOption? profile)
    {
        if (profile == null)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Перезаписать профиль «{profile.Name}»?",
            "В профиль попадёт то, что сейчас играет на каждом устройстве, и его яркость. Сохранённое раньше пропадёт.",
            "Перезаписать");

        if (!confirmed)
            return;

        _config.UpdateProfile(profile.Settings);
        profile.Reload();
        SetActiveProfile(profile);
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Возвращает устройствам яркость и содержимое из профиля.
    /// </summary>
    [RelayCommand]
    private void ApplyProfile(ProfileOption? profile)
    {
        if (profile == null)
            return;

        _config.ApplyProfile(profile.Settings);
        DropUnavailableContent();

        // Открытая страница устройства показывает уже новое содержимое
        Scene.Reload();

        if (SelectedPage == AppPage.Device && _openedDeviceId != null)
            Scene.OpenDevice(_openedDeviceId);

        OnContentChanged();
        SetActiveProfile(profile);
    }

    /// <summary>
    /// Отмечает профиль, который сейчас играет; null — играет то, что выбрали вручную.
    /// </summary>
    private void SetActiveProfile(ProfileOption? active)
    {
        foreach (var profile in Profiles)
            profile.IsActive = profile == active;
    }

    private ProfileOption NewProfileOption(ProfileConfig profile)
    {
        return new ProfileOption(profile, id => _deviceManager.Devices.FirstOrDefault(d => d.Id == id), OnProfileEdited);
    }

    /// <summary>
    /// Профиль поправили в разделе «Профили»: то, что играет, он больше не повторяет, пока его не применят снова.
    /// </summary>
    private void OnProfileEdited()
    {
        SetActiveProfile(null);
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Эффект или яркость устройства поменяли вручную: играет уже не профиль.
    /// </summary>
    private void OnLightingEdited()
    {
        SetActiveProfile(null);
        OnContentChanged();
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(ProfileOption? profile)
    {
        if (profile == null)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Удалить профиль «{profile.Name}»?",
            "Подсветка, которая играет сейчас, не изменится.", "Удалить", isDanger: true);

        if (!confirmed)
            return;

        _config.Profiles.Remove(profile.Settings);
        Profiles.Remove(profile);

        if (SelectedProfile == profile)
            SelectedProfile = Profiles.FirstOrDefault();

        OnPropertyChanged(nameof(HasProfiles));
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Сцену и игру пока не выбрать, а экран — только у ленты за монитором: устройство, которое их играет,
    /// переходит на обычный эффект, и экран не снимается впустую.
    /// </summary>
    private void DropUnavailableContent()
    {
        foreach (var lighting in _config.DeviceLighting)
        {
            var isOnMonitor = _config.Monitors.Any(m => m.DeviceId != null && lighting.DeviceIds.Contains(m.DeviceId));

            if (lighting.DefaultContent.Kind is ContentKind.Scene or ContentKind.Game
                || lighting.DefaultContent.Kind == ContentKind.Screen && !isOnMonitor)
                lighting.DefaultContent.Kind = ContentKind.Effect;
        }
    }

    /// <summary>
    /// Сворачивает левое меню до значков или разворачивает обратно.
    /// </summary>
    [RelayCommand]
    private void ToggleMenu()
    {
        IsMenuCollapsed = !IsMenuCollapsed;
    }

    /// <summary>
    /// Поиск устройств: открывает вкладку «Устройства» и запускает поиск.
    /// </summary>
    [RelayCommand]
    private async Task FindDevicesAsync()
    {
        SelectedPage = AppPage.Devices;

        await Devices.FindCommand.ExecuteAsync(null);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Подсветку выключают из меню трея или из настроек — выводы устройств гасят свет и перестают отправлять кадры
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            _deviceManager.SetLightingEnabled(IsLightOn);

        // Выключенная подсветка — тоже часть снимка для потока кадров
        if (e.PropertyName == nameof(SettingsViewModel.LightingEnabled))
            UpdateFrames();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        UpdateFrames();
        RefreshDeviceNav();
    }

    /// <summary>
    /// Пересобирает устройства в меню; открытое устройство остаётся открытым, пропавшее — возвращает на «Устройства».
    /// </summary>
    private void RefreshDeviceNav()
    {
        var opened = _openedDeviceId;

        DeviceNav.Clear();

        foreach (var device in Scene.Devices)
            DeviceNav.Add(new DeviceNavOption(device));

        if (SelectedPage != AppPage.Device || opened == null)
            return;

        if (DeviceNav.Any(d => d.Id == opened))
        {
            // Меню при пересборке сбросило выбор — возвращаем его
            SelectedDeviceId = opened;
            OnPropertyChanged(nameof(SelectedDeviceId));
        }
        else
        {
            SelectedPage = AppPage.Devices;
        }
    }

    /// <summary>
    /// Даёт устройству своё название; пустое или такое же, как у драйвера, возвращает название от драйвера.
    /// </summary>
    public void RenameDevice(string deviceId, string name)
    {
        var device = _deviceManager.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device == null)
            return;

        var customName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        if (customName == (device.Device?.Name ?? device.Settings.Name))
            customName = null;

        if (customName == device.Settings.CustomName)
            return;

        device.Settings.CustomName = customName;
        OnDeviceLookChanged(deviceId);
    }

    /// <summary>
    /// Меняет значок устройства в левом меню.
    /// </summary>
    public void SetDeviceIcon(string deviceId, string icon)
    {
        var device = _deviceManager.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device == null || device.Settings.Icon == icon)
            return;

        device.Settings.Icon = icon;
        OnDeviceLookChanged(deviceId);
    }

    /// <summary>
    /// Выключает подсветку одного устройства или включает её обратно. Профиль это не меняет: что играет, остаётся прежним.
    /// </summary>
    public void SetDeviceOn(string deviceId, bool isOn)
    {
        var device = _deviceManager.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device == null || device.Settings.IsOn == isOn)
            return;

        // Меню, список устройств и открытая страница обновятся по смене состояния устройств
        _deviceManager.SetDeviceOn(device, isOn);
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Название или значок устройства сменились: меню и открытая страница показывают новые, настройки сохраняются.
    /// </summary>
    private void OnDeviceLookChanged(string deviceId)
    {
        Scene.Reload();

        if (SelectedPage == AppPage.Device && _openedDeviceId == deviceId)
            Scene.OpenDevice(deviceId);

        RefreshDeviceNav();
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Открывает страницу устройства, как пункт устройства в левом меню.
    /// </summary>
    private void OpenDevice(string deviceId)
    {
        // Тот же Id подряд не вызвал бы смену выбора — сначала сбрасываем; null страницу не трогает
        SelectedDeviceId = null;
        SelectedDeviceId = deviceId;
    }

    partial void OnSelectedDeviceIdChanged(string? value)
    {
        // null приходит и от пересборки меню — страница устройства при этом остаётся открытой
        if (value == null)
            return;

        _openedDeviceId = value;
        Scene.OpenDevice(value);
        SelectedPage = AppPage.Device;
    }

    /// <summary>
    /// Группы, режим сцены, эффект, настройки эффекта или яркость изменились: движку — новый снимок, в файл — с задержкой.
    /// </summary>
    private void OnContentChanged()
    {
        UpdateFrames();
        Settings.ScheduleSave();
    }

    /// <summary>
    /// Отдаёт потоку кадров новый снимок текущих настроек и подключённых устройств.
    /// </summary>
    private void UpdateFrames()
    {
        var snapshot = FrameSnapshot.Build(_config, _deviceManager.Devices);

        _frameLoop.Update(snapshot with { IsLightingEnabled = IsLightOn });
    }

    partial void OnSelectedPageChanged(AppPage value)
    {
        // Мониторы и устройства могли подключить или отключить, пока страница была закрыта
        if (value == AppPage.Monitors)
            Monitors.Refresh();

        // Ушли со страницы устройства — его пункт в меню больше не выделен
        if (value != AppPage.Device)
        {
            _openedDeviceId = null;
            SelectedDeviceId = null;
        }

        UpdateLive();
    }

    /// <summary>
    /// Живые цвета устройств идут, пока окно видно; превью редакторов — пока открыта страница устройства или сцены.
    /// </summary>
    private void UpdateLive()
    {
        Scene.SetLive(_isWindowShown, SelectedPage is AppPage.Scene or AppPage.Device);
    }
}

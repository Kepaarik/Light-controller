using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Import;
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
        Settings = new SettingsViewModel(config, ImportFromLedHubAsync);

        // Страницы «Сцена» пока нет: у каждого устройства своё содержимое, оно выбирается на странице устройства
        config.Mode = SceneMode.Devices;

        // Сцену и игру пока не выбрать, а экран — только у ленты за монитором: устройство, которое их играло,
        // переходит на обычный эффект, и экран не снимается впустую
        foreach (var lighting in config.DeviceLighting)
        {
            var isOnMonitor = config.Monitors.Any(m => m.DeviceId != null && lighting.DeviceIds.Contains(m.DeviceId));

            if (lighting.DefaultContent.Kind is ContentKind.Scene or ContentKind.Game
                || lighting.DefaultContent.Kind == ContentKind.Screen && !isOnMonitor)
                lighting.DefaultContent.Kind = ContentKind.Effect;
        }

        _deviceManager = new DeviceManager(config);
        _frameLoop = new FrameLoop(FrameSnapshot.Build(config, _deviceManager.Devices), _sources);
        Devices = new DevicesViewModel(_deviceManager, _frameLoop, Settings.ScheduleSave, OpenDevice);
        Scene = new SceneViewModel(config, _deviceManager, _frameLoop, _sources, OnContentChanged);
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
        new(AppPage.Monitors, "Мониторы", "")
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
        Scene.SetLive(false);

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

    /// <summary>
    /// «Импортировать из LedHub» в настройках: после подтверждения переносит настройки старого приложения, сразу
    /// сохраняет их и показывает, что перенесено и что пропущено.
    /// </summary>
    private async Task ImportFromLedHubAsync()
    {
        if (!LedHubImport.IsAvailable)
        {
            await Dialogs.ShowAsync("Настройки LedHub не найдены", $"Файла {LedHubImport.FilePath} нет.");
            return;
        }

        // Свою расстановку от расставленной автоматически Light Space не отличает, поэтому заменить её можно только явно
        var (confirmed, replacesPlacements) = await Dialogs.ConfirmWithOptionAsync("Импортировать настройки LedHub?",
            "Устройства, группы, расстановка и настройки эффектов добавятся к текущим. То, что уже настроено в Light Space, не изменится. " +
            "Расстановка из LedHub переносится, только пока устройства на сцене не расставлены, — или если включить замену ниже.",
            "Заменить расстановку устройств на сцене", "Импортировать");

        if (!confirmed)
            return;

        var result = LedHubImport.Run(_config, Scene.NextGroupName, Scene.NextGroupColor, replacesPlacements);

        _deviceManager.SyncWithSettings();
        Settings.SaveNow();

        var lines = new List<string>();

        if (result.Imported.Count > 0)
            lines.Add("Перенесено:\n" + string.Join("\n", result.Imported.Select(l => "• " + l)));

        if (result.Skipped.Count > 0)
            lines.Add("Пропущено:\n" + string.Join("\n", result.Skipped.Select(l => "• " + l)));

        await Dialogs.ShowAsync("Импорт из LedHub", lines.Count > 0 ? string.Join("\n\n", lines) : "В настройках LedHub нечего переносить.");
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
            DeviceNav.Add(new DeviceNavOption(device.Id, device.Name, device.IsKeyboard ? "" : ""));

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
    /// Живая сцена работает, только пока окно видно и открыта вкладка «Сцена».
    /// </summary>
    private void UpdateLive()
    {
        Scene.SetLive(_isWindowShown && SelectedPage is AppPage.Scene or AppPage.Device);
    }
}

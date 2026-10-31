using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Apps;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Import;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Главное окно и меню трея: вкладки, настройки, общие команды.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly DeviceManager _deviceManager;
    private readonly AppWatcher _apps;
    private readonly FrameLoop _frameLoop;
    private readonly SourceHub _sources = new();

    // Окно открыто и не свёрнуто
    private bool _isWindowShown;

    [ObservableProperty]
    private AppPage _selectedPage = AppPage.Scene;

    public MainViewModel(AppConfig config)
    {
        _config = config;
        Settings = new SettingsViewModel(config, ImportFromLedHubAsync);

        _deviceManager = new DeviceManager(config);
        _apps = new AppWatcher { FocusReleaseDelay = TimeSpan.FromSeconds(config.FocusReleaseDelaySeconds) };
        _frameLoop = new FrameLoop(FrameSnapshot.Build(config, _deviceManager.Devices, _apps), _sources);
        Devices = new DevicesViewModel(_deviceManager, _frameLoop, Settings.ScheduleSave);
        Scenarios = new ScenariosViewModel(config, _deviceManager, _apps, _sources, OnContentChanged);
        Scene = new SceneViewModel(config, _deviceManager, _frameLoop, _apps, OnContentChanged, AddScenario, OpenScenario);

        Settings.PropertyChanged += OnSettingsChanged;
        _deviceManager.DevicesChanged += OnDevicesChanged;
        _apps.Changed += OnAppsChanged;
    }

    /// <summary>
    /// Вкладки в заголовке окна.
    /// </summary>
    public IReadOnlyList<PageOption> Pages { get; } = new PageOption[]
    {
        new(AppPage.Scene, "Сцена"),
        new(AppPage.Scenarios, "Сценарии"),
        new(AppPage.Devices, "Устройства"),
        new(AppPage.Settings, "Настройки")
    };

    public SettingsViewModel Settings { get; }

    public DevicesViewModel Devices { get; }

    public SceneViewModel Scene { get; }

    public ScenariosViewModel Scenarios { get; }

    /// <summary>
    /// Сообщает, видно ли окно: живые цвета и превью считаются, только пока на них можно посмотреть.
    /// </summary>
    public void SetWindowShown(bool isShown)
    {
        _isWindowShown = isShown;
        UpdateLive();
    }

    /// <summary>
    /// Останавливает поток кадров, вывод на устройства и закрывает соединения с ними.
    /// </summary>
    public void Shutdown()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _deviceManager.DevicesChanged -= OnDevicesChanged;
        _apps.Changed -= OnAppsChanged;
        _apps.Dispose();
        Scene.SetLive(false);
        Scenarios.SetLive(false);

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

        var confirmed = await Dialogs.ConfirmAsync("Импортировать настройки LedHub?",
            "Устройства, группы, расстановка, настройки эффектов и игровые режимы добавятся к текущим. То, что уже настроено в Light Space, не изменится.",
            "Импортировать");

        if (!confirmed)
            return;

        var result = LedHubImport.Run(_config, Scene.NextGroupName, Scene.NextGroupColor);

        Scenarios.AddMissing();
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
            _deviceManager.SetLightingEnabled(Settings.LightingEnabled);

        // Задержку снятия фокусного сценария держит слежение за приложениями; снимок обновится по его событию
        if (e.PropertyName == nameof(SettingsViewModel.FocusReleaseDelaySeconds))
            _apps.FocusReleaseDelay = TimeSpan.FromSeconds(Settings.FocusReleaseDelaySeconds);

        // Выключенная подсветка, частота кадров и длительность перехода — тоже часть снимка для потока кадров
        if (e.PropertyName is nameof(SettingsViewModel.LightingEnabled) or nameof(SettingsViewModel.FrameRate)
            or nameof(SettingsViewModel.TransitionMilliseconds))
            UpdateFrames();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        UpdateFrames();
        Scenarios.Refresh();
    }

    /// <summary>
    /// Запустилось или закрылось приложение, сменилось окно в фокусе: в группах мог смениться активный сценарий.
    /// </summary>
    private void OnAppsChanged(object? sender, EventArgs e)
    {
        UpdateFrames();
        RefreshScenarios();
    }

    /// <summary>
    /// Группы, их эффект, настройки эффекта или яркость, сценарии или их порядок изменились: движку — новый снимок,
    /// в файл — с задержкой, спискам сценариев — новые названия и отметки «играет».
    /// </summary>
    private void OnContentChanged()
    {
        UpdateFrames();
        Settings.ScheduleSave();
        RefreshScenarios();
    }

    /// <summary>
    /// Обновляет, какой сценарий где играет, на сцене и на вкладке «Сценарии».
    /// </summary>
    private void RefreshScenarios()
    {
        Scene.RefreshScenarios();
        Scenarios.Refresh();
    }

    /// <summary>
    /// «+ сценарий» в панели группы: новый сценарий в этой группе открывается на вкладке «Сценарии».
    /// </summary>
    private void AddScenario(GroupConfig group)
    {
        SelectedPage = AppPage.Scenarios;
        Scenarios.Create(group);
    }

    /// <summary>
    /// Открывает сценарий на вкладке «Сценарии».
    /// </summary>
    private void OpenScenario(string scenarioId)
    {
        SelectedPage = AppPage.Scenarios;
        Scenarios.Open(scenarioId);
    }

    /// <summary>
    /// Отдаёт потоку кадров новый снимок текущих настроек и подключённых устройств.
    /// </summary>
    private void UpdateFrames()
    {
        _frameLoop.Update(FrameSnapshot.Build(_config, _deviceManager.Devices, _apps));
    }

    partial void OnSelectedPageChanged(AppPage value)
    {
        UpdateLive();
    }

    /// <summary>
    /// Живая сцена работает, только пока окно видно и открыта вкладка «Сцена»; живое превью сценария — пока открыта
    /// вкладка «Сценарии».
    /// </summary>
    private void UpdateLive()
    {
        Scene.SetLive(_isWindowShown && SelectedPage == AppPage.Scene);
        Scenarios.SetLive(_isWindowShown && SelectedPage == AppPage.Scenarios);
    }
}

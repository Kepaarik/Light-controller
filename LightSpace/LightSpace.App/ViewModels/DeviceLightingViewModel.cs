using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Подсветка устройств: живые цвета каждого устройства для левого меню и страница устройства, открытого в меню, —
/// его яркость, монитор ленты и что на нём играет: эффект, музыка или экран со своими настройками.
/// Все правки сразу попадают в настройки и уходят движку новым снимком.
/// </summary>
public sealed partial class DeviceLightingViewModel : ObservableObject
{
    // Живые цвета и превью обновляются не чаще 30 раз в секунду
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly FrameLoop _loop;
    private readonly Action _changed;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private TimeSpan _lastTick;

    // Превью редакторов видно: открыта страница устройства и окно не скрыто
    private bool _isPreviewShown;

    // Подсветка устройства, чья страница открыта; null — страница не открыта
    private DeviceLightingConfig? _lighting;

    // Открытая вкладка типа содержимого; null — та, что играет
    private ContentKindOption? _viewedKind;

    /// <summary>
    /// changed вызывается после каждого изменения яркости, монитора, содержимого и его настроек.
    /// sources — откуда превью экрана и музыки берут картинку и звук.
    /// </summary>
    public DeviceLightingViewModel(AppConfig config, DeviceManager manager, FrameLoop loop, SourceHub sources, Action changed)
    {
        _config = config;
        _manager = manager;
        _loop = loop;
        _changed = changed;

        // Выбор в редакторе вкладки включает её тип содержимого: само переключение вкладок ничего не меняет
        Editor = new EffectEditorViewModel(() => OnEditorChanged(ContentKindOption.Effect));
        ScreenEditor = new ScreenEditorViewModel(sources, () => OnEditorChanged(ContentKindOption.Screen));
        MusicEditor = new MusicEditorViewModel(sources, () => OnEditorChanged(ContentKindOption.Music));
        BuiltinEditor = new BuiltinEditorViewModel(manager, () => OnEditorChanged(ContentKindOption.Builtin));

        _manager.DevicesChanged += (_, _) => SyncDevices();
        SyncDevices();

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();
    }

    /// <summary>
    /// Галерея и настройки эффекта открытого устройства.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Цвета экрана, если открытое устройство — лента за монитором.
    /// </summary>
    public ScreenEditorViewModel ScreenEditor { get; }

    /// <summary>
    /// Источник звука, режим и цвета музыки.
    /// </summary>
    public MusicEditorViewModel MusicEditor { get; }

    /// <summary>
    /// Встроенные эффекты открытого устройства, их скорость и цвет.
    /// </summary>
    public BuiltinEditorViewModel BuiltinEditor { get; }

    /// <summary>
    /// Найденные устройства с живыми цветами, в порядке списка устройств.
    /// </summary>
    public ObservableCollection<LiveDeviceViewModel> Devices { get; } = new();

    /// <summary>
    /// Устройство, чья страница открыта; null — страница не открыта.
    /// </summary>
    public LiveDeviceViewModel? Device { get; private set; }

    /// <summary>
    /// Яркость подсветки открытого устройства, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => _lighting?.Brightness ?? 0;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (_lighting == null || brightness == _lighting.Brightness)
                return;

            _lighting.Brightness = brightness;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Типы содержимого для сегментов. Встроенные эффекты устройства — плитки во вкладке «Эффект», «Экран» — только
    /// у ленты за монитором.
    /// </summary>
    public IReadOnlyList<ContentKindOption> ContentKinds =>
        ContentKindOption.All
            .Where(k => k != ContentKindOption.Builtin)
            .Where(k => k != ContentKindOption.Screen || HasMonitor)
            .ToList();

    /// <summary>
    /// Тип содержимого, который сейчас играет на открытом устройстве; null — играет то, что здесь не выбрать.
    /// </summary>
    public ContentKindOption? PlayingContentKind => _lighting?.Content.Kind switch
    {
        ContentKind.Effect => ContentKindOption.Effect,
        ContentKind.Screen => ContentKindOption.Screen,
        ContentKind.Music => ContentKindOption.Music,
        ContentKind.Builtin => ContentKindOption.Builtin,
        _ => null
    };

    /// <summary>
    /// Открытая вкладка типа содержимого. Переключение вкладок только показывает их настройки: играет прежнее,
    /// пока во вкладке не выберут новый эффект или не нажмут «Включить».
    /// </summary>
    public ContentKindOption? SelectedContentKind
    {
        get => _lighting == null ? null : _viewedKind ?? TabOf(PlayingContentKind);
        set
        {
            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (_lighting == null || value == null || !value.IsAvailable || value == SelectedContentKind)
                return;

            _viewedKind = value;

            OnPropertyChanged();
            OnContentKindChanged();
        }
    }

    /// <summary>
    /// Открытая вкладка — не та, что играет: видна кнопка «Включить».
    /// </summary>
    public bool IsViewedKindIdle => SelectedContentKind != null && SelectedContentKind != TabOf(PlayingContentKind);

    /// <summary>
    /// Играет встроенный эффект устройства — в боковой панели его настройки, а не настройки эффекта из галереи.
    /// </summary>
    public bool IsBuiltinPlaying => PlayingContentKind == ContentKindOption.Builtin;

    /// <summary>
    /// Что сейчас играет, для подписи над вкладкой, которая не играет.
    /// </summary>
    public string PlayingText => PlayingContentKind is { } kind ? $"Сейчас играет: {kind.Title}" : "";

    public bool IsEffectContent => SelectedContentKind == ContentKindOption.Effect;

    public bool IsScreenContent => SelectedContentKind == ContentKindOption.Screen;

    public bool IsMusicContent => SelectedContentKind == ContentKindOption.Music;

    /// <summary>
    /// Открытое устройство играет сцену применённого профиля вместо своей подсветки.
    /// </summary>
    public bool IsInScene => Device is { } device && _config.Scene?.Devices.Any(d => d.DeviceId == device.Id) == true;

    /// <summary>
    /// Убирает открытое устройство из сцены, которая сейчас играет: оно снова играет свою подсветку, остальные
    /// устройства сцены играют её дальше. Сцену в профиле это не меняет.
    /// </summary>
    [RelayCommand]
    private void LeaveScene()
    {
        if (Device is not { } device || _config.Scene == null)
            return;

        _config.Scene.Devices.RemoveAll(d => d.DeviceId == device.Id);

        OnPropertyChanged(nameof(IsInScene));
        _changed();
    }

    /// <summary>
    /// Мониторы, за которыми может висеть лента: «Без монитора» и все подключённые мониторы.
    /// </summary>
    public IReadOnlyList<DeviceMonitorOption> MonitorOptions { get; private set; } = Array.Empty<DeviceMonitorOption>();

    /// <summary>
    /// Открытое устройство — адресная лента: её можно повесить за монитор.
    /// </summary>
    public bool IsStripSelected => Device is { IsStrip: true };

    /// <summary>
    /// Открытая лента висит за монитором: ей доступен «Экран» — цвета краёв этого монитора.
    /// </summary>
    public bool HasMonitor => IsStripSelected && SelectedMonitor?.MonitorKey != null;

    /// <summary>
    /// Монитор, за которым висит открытая лента. Монитор, который сейчас не подключён, показывается как «Без монитора»,
    /// но в настройках не стирается.
    /// </summary>
    public DeviceMonitorOption? SelectedMonitor
    {
        get
        {
            var deviceId = Device?.Id;
            var key = _config.Monitors.FirstOrDefault(m => m.DeviceId != null && m.DeviceId == deviceId)?.MonitorKey;

            return MonitorOptions.FirstOrDefault(o => o.MonitorKey == key) ?? MonitorOptions.FirstOrDefault();
        }
        set
        {
            if (value == null || !IsStripSelected || Device is not { } device)
                return;

            // Лента висит только за одним монитором, а за монитором — только одна лента
            foreach (var monitor in _config.Monitors.Where(m => m.DeviceId == device.Id))
                monitor.DeviceId = null;

            if (value.MonitorKey != null)
            {
                var monitor = _config.Monitors.FirstOrDefault(m => m.MonitorKey == value.MonitorKey);

                if (monitor == null)
                {
                    monitor = new MonitorConfig { MonitorKey = value.MonitorKey };
                    _config.Monitors.Add(monitor);
                }

                monitor.DeviceId = device.Id;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMonitor));

            // Без монитора «Экрана» нет: лента, которая его играла, переходит на обычный эффект
            if (!HasMonitor && _viewedKind == ContentKindOption.Screen)
                _viewedKind = null;

            OnPropertyChanged(nameof(ContentKinds));
            OnPropertyChanged(nameof(SelectedContentKind));

            if (!HasMonitor && PlayingContentKind == ContentKindOption.Screen)
                Play(ContentKindOption.Effect);
            else
                OnContentKindChanged();

            _changed();
        }
    }

    /// <summary>
    /// Открывает страницу устройства из левого меню.
    /// </summary>
    public void OpenDevice(string deviceId)
    {
        // Мониторы могли подключить или отключить, пока страница была закрыта
        MonitorOptions = new[] { new DeviceMonitorOption(null, "Без монитора") }
            .Concat(Monitors.List().Select(m => new DeviceMonitorOption(m.Key,
                m.IsPrimary ? $"Монитор {m.Number} · основной" : $"Монитор {m.Number}")))
            .ToList();

        OnPropertyChanged(nameof(MonitorOptions));
        Show(Devices.FirstOrDefault(d => d.Id == deviceId));
    }

    /// <summary>
    /// Перечитывает устройства, их яркость и содержимое из настроек, например после применения профиля.
    /// </summary>
    public void Reload()
    {
        SyncDevices();

        // Применённый профиль мог включить или выключить сцену
        OnPropertyChanged(nameof(IsInScene));
    }

    /// <summary>
    /// Включает живые цвета устройств, пока окно видно (они нужны и значкам в левом меню), и превью редакторов,
    /// пока открыта страница устройства; выключает, когда окно скрыто или открыта другая страница.
    /// </summary>
    public void SetLive(bool isLive, bool isPageShown)
    {
        var isPreviewShown = isLive && isPageShown;

        // Пока превью не видно, монитор и звук для него не снимаются
        if (isPreviewShown != _isPreviewShown)
        {
            _isPreviewShown = isPreviewShown;

            if (!isPreviewShown)
                StopPreviews();
        }

        if (isLive == _liveTimer.IsEnabled)
            return;

        _loop.IsLiveColorsWanted = isLive;

        if (!isLive)
        {
            _liveTimer.Stop();
            _clock.Stop();
            return;
        }

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// «Включить» на вкладке, которая не играет: на устройстве начинает играть её тип содержимого с её настройками.
    /// </summary>
    [RelayCommand]
    private void PlayViewedKind()
    {
        if (SelectedContentKind is { } kind)
            Play(kind);
    }

    /// <summary>
    /// В редакторе вкладки что-то выбрали или поменяли: на устройстве начинает играть тип этой вкладки.
    /// </summary>
    private void OnEditorChanged(ContentKindOption kind)
    {
        if (_lighting != null && PlayingContentKind != kind)
        {
            Play(kind);
            return;
        }

        _changed();
    }

    /// <summary>
    /// Включает на открытом устройстве тип содержимого kind.
    /// </summary>
    private void Play(ContentKindOption kind)
    {
        var content = _lighting?.Content;

        var contentKind = kind == ContentKindOption.Screen ? ContentKind.Screen
            : kind == ContentKindOption.Music ? ContentKind.Music
            : kind == ContentKindOption.Builtin ? ContentKind.Builtin
            : ContentKind.Effect;

        if (content == null || contentKind == content.Kind)
            return;

        content.Kind = contentKind;

        OnContentKindChanged();
        _changed();
    }

    /// <summary>
    /// Вкладка, в которой показан тип содержимого: встроенные эффекты устройства — во вкладке «Эффект».
    /// </summary>
    private static ContentKindOption? TabOf(ContentKindOption? kind)
    {
        return kind == ContentKindOption.Builtin ? ContentKindOption.Effect : kind;
    }

    /// <summary>
    /// Показывает страницу устройства: редакторы открывают его содержимое, открывается вкладка того, что играет.
    /// null — страница закрыта.
    /// </summary>
    private void Show(LiveDeviceViewModel? device)
    {
        Device = device;
        _lighting = device != null ? _config.EnsureDeviceLighting(device.Id) : null;
        _viewedKind = null;

        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(IsInScene));
        OnPropertyChanged(nameof(IsStripSelected));
        OnPropertyChanged(nameof(SelectedMonitor));
        OnPropertyChanged(nameof(HasMonitor));

        // Список типов меняется раньше выбора в нём, иначе пересборка сегментов сбросила бы выбор
        OnPropertyChanged(nameof(ContentKinds));
        OnPropertyChanged(nameof(SelectedContentKind));

        var content = _lighting?.Content;

        Editor.Edit(content);
        ScreenEditor.Edit(content);
        MusicEditor.Edit(content);
        BuiltinEditor.Edit(content, device?.Id);

        OnContentKindChanged();
    }

    /// <summary>
    /// Сменился тип содержимого или открытое устройство: показываются редактор и превью этого типа, превью экрана
    /// и музыки, которых больше не видно, отпускают свои источники.
    /// </summary>
    private void OnContentKindChanged()
    {
        OnPropertyChanged(nameof(IsEffectContent));
        OnPropertyChanged(nameof(IsScreenContent));
        OnPropertyChanged(nameof(IsMusicContent));
        OnPropertyChanged(nameof(PlayingContentKind));
        OnPropertyChanged(nameof(IsViewedKindIdle));
        OnPropertyChanged(nameof(PlayingText));
        OnPropertyChanged(nameof(IsBuiltinPlaying));

        // Выделена плитка только того, что играет
        Editor.IsPlaying = PlayingContentKind == ContentKindOption.Effect;
        BuiltinEditor.IsPlaying = IsBuiltinPlaying;

        if (!IsScreenContent)
            ScreenEditor.StopPreview();

        if (!IsMusicContent)
            MusicEditor.StopPreview();
    }

    private void StopPreviews()
    {
        ScreenEditor.StopPreview();
        MusicEditor.StopPreview();
    }

    /// <summary>
    /// Приводит устройства к списку менеджера: после поиска у них могли смениться возможности и состояние.
    /// Открытая страница остаётся открытой, если устройство ещё есть, и показывает его настройки заново.
    /// </summary>
    private void SyncDevices()
    {
        var openedId = Device?.Id;

        Devices.Clear();

        foreach (var device in _manager.Devices)
            Devices.Add(new LiveDeviceViewModel(device));

        Show(Devices.FirstOrDefault(d => d.Id == openedId));
    }

    /// <summary>
    /// Один кадр живых цветов: последние цвета устройств от движка и следующий кадр превью в редакторе.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Превью редакторов считаются, только пока их видно
        if (_isPreviewShown)
            AdvancePreview(seconds);

        var live = _loop.LiveColors;

        foreach (var device in Devices)
        {
            // Выключенному устройству кадры считаются, но до него не доходят — цветов у него нет
            var colors = device.IsOn && live is { IsLightingEnabled: true } && live.Devices.TryGetValue(device.Id, out var found) ? found : null;

            device.Show(colors?.Colors, colors?.Brightness ?? 100);
            device.IsBuiltinPlaying = colors?.IsBuiltin == true;
            device.RefreshStatus();
        }
    }

    /// <summary>
    /// Следующий кадр превью во вкладке, которая открыта.
    /// </summary>
    private void AdvancePreview(double seconds)
    {
        if (IsScreenContent)
            ScreenEditor.Advance(seconds);
        else if (IsMusicContent)
            MusicEditor.Advance(seconds);
        else if (IsEffectContent)
            Editor.Advance(seconds);
    }
}

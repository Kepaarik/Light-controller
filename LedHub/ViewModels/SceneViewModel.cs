using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Core;
using LedHub.Services;
using LedHub.Services.Effects;
using LedHub.Services.Scene;

namespace LedHub.ViewModels;

/// <summary>
/// То, что ещё можно поставить на сцену: монитор или устройство.
/// </summary>
public sealed record SceneCandidate(string Key, string Title, string Subtitle, string Icon, bool IsMonitor);

/// <summary>
/// Сцена: мониторы и устройства расставлены так, как они стоят на самом деле, и эффекты играют по всей расстановке сразу.
/// </summary>
public sealed partial class SceneViewModel : ObservableObject
{
    /// <summary>
    /// Размер сцены в её единицах (примерно сантиметрах).
    /// </summary>
    public const double WorldWidth = 400;

    public const double WorldHeight = 240;

    private const double DefaultMonitorWidth = 60;
    private const double DefaultStripLength = 50;
    private const double DefaultKeyboardWidth = 38;

    private readonly AppConfig _config;
    private readonly ObservableCollection<DeviceItemViewModel> _devices;
    private readonly Action _save;
    private readonly Action<IReadOnlyCollection<LedDevice>> _stopConflicts;
    private readonly EffectRunner _runner = new();
    private readonly ScenePointMap _map = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly Stopwatch _previewClock = new();

    // Эффект, который сейчас показывается на схеме: либо запущенный на лентах, либо только предпросмотр
    private SceneLayerSource? _liveSource;
    private bool _isLiveSourceOwned;
    private double _previewPhase;
    private double _previewLastTick;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SceneItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    private SceneLayerViewModel? _selectedLayer;

    /// <summary>
    /// Увеличивается при каждом изменении, которое нужно перерисовать: правке расстановки или новом кадре эффекта.
    /// </summary>
    [ObservableProperty]
    private int _version;

    [ObservableProperty]
    private int _brightness = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private bool _isPreviewing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Расставьте мониторы и ленты так, как они стоят у вас, и запустите эффект на всю сцену";

    public SceneViewModel(AppConfig config, ObservableCollection<DeviceItemViewModel> devices, Action save,
        Action<IReadOnlyCollection<LedDevice>> stopConflicts)
    {
        _config = config;
        _devices = devices;
        _save = save;
        _stopConflicts = stopConflicts;

        Effects = new EffectPickerViewModel(config, StartEffect, StopEffect, save);
        Effects.PropertyChanged += (_, e) =>
        {
            // Предпросмотр показывает тот эффект, который выбран в галерее
            if (e.PropertyName == nameof(EffectPickerViewModel.SelectedEffect) && IsPreviewing)
                StartPreview();
        };

        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _frameTimer.Tick += (_, _) => OnFrame();

        // В слой можно поставить любой эффект, кроме эффектов от нажатий: им нужна раскладка клавиатуры
        LayerOptions = Effects.Options.Where(o => !o.Definition.UsesKeys).ToList();

        foreach (var layer in _config.SceneLayers)
            Layers.Add(new SceneLayerViewModel(layer, LayerOptions, OnLayerEdited));
    }

    public EffectPickerViewModel Effects { get; }

    public ObservableCollection<SceneItemViewModel> Items { get; } = new();

    public ObservableCollection<SceneCandidate> Candidates { get; } = new();

    /// <summary>
    /// Слои эффектов поверх эффекта всей сцены, снизу вверх.
    /// </summary>
    public ObservableCollection<SceneLayerViewModel> Layers { get; } = new();

    public IReadOnlyList<EffectOption> LayerOptions { get; }

    public bool HasSelection => SelectedItem != null;

    public bool HasLayers => Layers.Count > 0;

    public bool HasSelectedLayer => SelectedLayer != null;

    public bool HasItems => Items.Count > 0;

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>
    /// На схеме сейчас играет эффект.
    /// </summary>
    public bool IsLive => IsRunning || IsPreviewing;

    /// <summary>
    /// Пересобирает сцену: заново находит мониторы и устройства и связывает их с сохранённой расстановкой.
    /// </summary>
    public void Refresh()
    {
        var selectedKey = SelectedItem?.Config.Key;
        var monitors = MonitorList.GetAll();

        Items.Clear();

        foreach (var config in _config.SceneItems)
        {
            if (config.Kind == SceneItemKind.Monitor)
            {
                var monitor = monitors.FirstOrDefault(m => SceneItemConfig.MonitorPrefix + m.Key == config.Key);

                // Отключённый монитор не показываем, но его место помним — вдруг подключат снова
                if (monitor != null)
                    Items.Add(new SceneItemViewModel(config, MonitorTitle(monitor), null, monitor, OnItemEdited));

                continue;
            }

            var device = FindDevice(config.Key);
            var title = device?.Name ?? _config.DeviceNames.GetValueOrDefault(config.Key) ?? "Устройство не найдено";
            Items.Add(new SceneItemViewModel(config, title, device, null, OnItemEdited));
        }

        Candidates.Clear();

        foreach (var monitor in monitors.Where(m => _config.SceneItems.All(i => i.Key != SceneItemConfig.MonitorPrefix + m.Key)))
            Candidates.Add(new SceneCandidate(SceneItemConfig.MonitorPrefix + monitor.Key, MonitorTitle(monitor),
                $"{monitor.Width}×{monitor.Height}", "", true));

        var backlights = BacklightIds(monitors);

        foreach (var device in _devices.Where(d => _config.SceneItems.All(i => i.Key != d.Id) && !backlights.Contains(d.Id)))
            Candidates.Add(new SceneCandidate(device.Id, device.Name, device.Protocol, device.Icon, false));

        SelectedItem = Items.FirstOrDefault(i => i.Config.Key == selectedKey);

        Rebuild();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasCandidates));
    }

    /// <summary>
    /// Цвет светодиода элемента на схеме: из эффекта, если он играет, иначе — последний цвет устройства.
    /// </summary>
    public RgbColor LedColor(SceneShape shape, int index)
    {
        if (shape.DeviceId == null)
            return EffectMath.Black;

        if (_liveSource != null)
            return _liveSource.RenderLed(shape.DeviceId, index);

        var device = FindDevice(shape.DeviceId);
        if (device == null || device.IsOn == false)
            return EffectMath.Black;

        return device.LedColors is { } colors && index < colors.Length ? colors[index] : device.Color;
    }

    /// <summary>
    /// Цвет обычной ленты на схеме — один на весь элемент.
    /// </summary>
    public RgbColor SolidColor(SceneShape shape)
    {
        if (shape.DeviceId == null)
            return EffectMath.Black;

        if (_liveSource != null)
            return _liveSource.RenderCenter(shape.DeviceId);

        var device = FindDevice(shape.DeviceId);
        return device == null || device.IsOn == false ? EffectMath.Black : device.Color;
    }

    /// <summary>
    /// Переносит элемент мышью; сохраняется по окончании перетаскивания.
    /// </summary>
    public void MoveItem(SceneItemViewModel item, double x, double y)
    {
        item.Config.X = Math.Clamp(Math.Round(x), 0, WorldWidth);
        item.Config.Y = Math.Clamp(Math.Round(y), 0, WorldHeight);

        Rebuild();
    }

    /// <summary>
    /// Тянет конец ленты: противоположный конец стоит на месте, меняются длина и поворот.
    /// </summary>
    public void DragStripEnd(SceneItemViewModel item, bool isFirstEnd, double x, double y)
    {
        if (item.Shape?.Line is not { } line)
            return;

        var fixedEnd = isFirstEnd ? line.End : line.Start;
        var (fromX, fromY, toX, toY) = isFirstEnd ^ item.Config.IsReversed
            ? (x, y, fixedEnd.X, fixedEnd.Y)
            : (fixedEnd.X, fixedEnd.Y, x, y);

        var length = Math.Sqrt((toX - fromX) * (toX - fromX) + (toY - fromY) * (toY - fromY));
        if (length < 5)
            return;

        item.Config.X = (fromX + toX) / 2;
        item.Config.Y = (fromY + toY) / 2;
        item.Config.Size = Math.Round(length);
        item.Config.Rotation = Math.Round(Math.Atan2(toY - fromY, toX - fromX) * 180 / Math.PI);

        Rebuild();
    }

    public void RotateItem(SceneItemViewModel item, double degrees)
    {
        if (item.IsMonitor)
            return;

        item.Config.Rotation = SceneItemViewModel.NormalizeAngle(item.Config.Rotation + degrees);

        Rebuild();
        CommitEdit();
    }

    /// <summary>
    /// Правка мышью закончена: сохраняем расстановку и обновляем поля настроек.
    /// </summary>
    public void CommitEdit()
    {
        SelectedItem?.RefreshFields();
        SelectedLayer?.RefreshFields();
        _save();
    }

    /// <summary>
    /// Переносит слой мышью за его левый верхний угол; сохраняется по окончании перетаскивания.
    /// </summary>
    public void MoveLayer(SceneLayerViewModel layer, double x, double y)
    {
        layer.Config.X = Math.Clamp(Math.Round(x), 0, WorldWidth - layer.Config.Width);
        layer.Config.Y = Math.Clamp(Math.Round(y), 0, WorldHeight - layer.Config.Height);

        Version++;
    }

    /// <summary>
    /// Тянет правый нижний угол слоя: левый верхний стоит на месте, меняются ширина и высота.
    /// </summary>
    public void ResizeLayer(SceneLayerViewModel layer, double right, double bottom)
    {
        layer.Config.Width = Math.Clamp(Math.Round(right - layer.Config.X), 10, WorldWidth - layer.Config.X);
        layer.Config.Height = Math.Clamp(Math.Round(bottom - layer.Config.Y), 10, WorldHeight - layer.Config.Y);

        Version++;
    }

    public void StopIfUsing(IReadOnlyCollection<LedDevice> devices)
    {
        if (IsRunning && _runner.Devices.Any(d => devices.Contains(d)))
            StopEffect();
    }

    public void Shutdown()
    {
        StopEffect();
        StopPreview();
    }

    [RelayCommand]
    private void Add(SceneCandidate candidate)
    {
        var config = new SceneItemConfig { Key = candidate.Key };

        if (candidate.IsMonitor)
        {
            // Мониторы встают в ряд по центру сцены, следующий — справа от предыдущего
            var monitors = Items.Where(i => i.IsMonitor && i.Shape?.Screen != null).Select(i => i.Shape!.Screen!.Value).ToList();

            config.Kind = SceneItemKind.Monitor;
            config.Size = DefaultMonitorWidth;
            config.X = monitors.Count == 0 ? WorldWidth / 2 : monitors.Max(s => s.TopLeft.X + s.Width) + 8 + DefaultMonitorWidth / 2;
            config.Y = WorldHeight / 2;
        }
        else
        {
            var offset = Items.Count(i => !i.IsMonitor) * 14;

            config.Kind = DeviceKind(candidate.Key);
            config.Size = config.Kind == SceneItemKind.Keyboard ? DefaultKeyboardWidth : DefaultStripLength;
            config.X = 70 + offset;
            config.Y = 60 + offset;
        }

        config.X = Math.Clamp(config.X, 0, WorldWidth);
        _config.SceneItems.Add(config);
        _save();

        Refresh();
        SelectedItem = Items.FirstOrDefault(i => i.Config == config);
    }

    /// <summary>
    /// Расставляет сцену сама: ставит на неё всё, чего там ещё нет, мониторы — в ряд по центру в том же порядке,
    /// что в Windows, клавиатуры — под основным монитором, ленты и панели — по бокам от мониторов.
    /// </summary>
    [RelayCommand]
    private void AutoLayout()
    {
        foreach (var candidate in Candidates.ToList())
        {
            var kind = candidate.IsMonitor ? SceneItemKind.Monitor : DeviceKind(candidate.Key);

            _config.SceneItems.Add(new SceneItemConfig
            {
                Key = candidate.Key,
                Kind = kind,
                Size = kind switch
                {
                    SceneItemKind.Monitor => DefaultMonitorWidth,
                    SceneItemKind.Keyboard => DefaultKeyboardWidth,
                    _ => DefaultStripLength
                }
            });
        }

        Refresh();

        if (Items.Count == 0)
        {
            StatusText = "Ставить на сцену нечего: мониторы и ленты не найдены";
            return;
        }

        const double gap = 8;
        const double sideGap = 16;
        var rowY = WorldHeight * 0.4;

        // Мониторы — в ряд по центру, слева направо так же, как они стоят в Windows
        var monitors = Items.Where(i => i.Monitor != null).OrderBy(i => i.Monitor!.Left).ToList();
        var x = WorldWidth / 2 - (monitors.Sum(m => m.Config.Size) + gap * Math.Max(0, monitors.Count - 1)) / 2;

        foreach (var monitor in monitors)
        {
            monitor.Config.X = x + monitor.Config.Size / 2;
            monitor.Config.Y = rowY;
            x += monitor.Config.Size + gap;
        }

        Rebuild();

        var left = monitors.Count > 0 ? monitors.Min(m => m.Shape!.Bounds.Min.X) : WorldWidth / 2;
        var right = monitors.Count > 0 ? monitors.Max(m => m.Shape!.Bounds.Max.X) : WorldWidth / 2;
        var bottom = monitors.Count > 0 ? monitors.Max(m => m.Shape!.Bounds.Max.Y) : rowY;
        var main = monitors.FirstOrDefault(m => m.Monitor!.IsPrimary) ?? monitors.FirstOrDefault();

        // Клавиатуры — под основным монитором, одна под другой
        var y = bottom + 20;

        foreach (var keyboard in Items.Where(i => i.IsKeyboard))
        {
            keyboard.Config.Rotation = 0;
            var (_, height) = SizeOf(keyboard);

            keyboard.Config.X = main?.Config.X ?? WorldWidth / 2;
            keyboard.Config.Y = y + height / 2;
            y += height + gap;
        }

        // Ленты и панели — по очереди слева и справа от мониторов; ленты стоят вертикально
        var isLeft = true;
        var leftEdge = left - sideGap;
        var rightEdge = right + sideGap;

        foreach (var item in Items.Where(i => i.IsDevice && !i.IsKeyboard))
        {
            item.Config.Rotation = item.Config.Kind == SceneItemKind.Strip ? 90 : 0;
            var (width, _) = SizeOf(item);

            item.Config.X = isLeft ? leftEdge - width / 2 : rightEdge + width / 2;
            item.Config.Y = rowY;

            if (isLeft)
                leftEdge -= width + sideGap;
            else
                rightEdge += width + sideGap;

            isLeft = !isLeft;
        }

        foreach (var item in Items)
        {
            item.Config.X = Math.Clamp(Math.Round(item.Config.X), 0, WorldWidth);
            item.Config.Y = Math.Clamp(Math.Round(item.Config.Y), 0, WorldHeight);
        }

        _save();
        Rebuild();
        SelectedItem?.RefreshFields();

        StatusText = $"Сцена расставлена автоматически: элементов — {Items.Count}. Поправьте мышью то, что стоит не так";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedItem == null)
            return;

        _config.SceneItems.Remove(SelectedItem.Config);
        _save();

        SelectedItem = null;
        Refresh();
    }

    /// <summary>
    /// Добавляет слой поверх остальных с эффектом, выбранным в галерее; новый слой встаёт чуть ниже и правее прежнего.
    /// </summary>
    [RelayCommand]
    private void AddLayer()
    {
        var effect = Effects.SelectedEffect is { Definition.UsesKeys: false } selected ? selected : LayerOptions[0];
        var offset = Layers.Count % 8 * 12;

        var config = new SceneLayerConfig
        {
            EffectId = effect.Definition.Id,
            X = Math.Min(WorldWidth / 2 - 60 + offset, WorldWidth - 120),
            Y = Math.Min(WorldHeight / 2 - 40 + offset, WorldHeight - 80)
        };

        _config.SceneLayers.Add(config);
        _save();

        var layer = new SceneLayerViewModel(config, LayerOptions, OnLayerEdited);
        Layers.Add(layer);
        SelectedLayer = layer;

        OnPropertyChanged(nameof(HasLayers));
        RestartLive();
    }

    [RelayCommand]
    private void RemoveLayer(SceneLayerViewModel layer)
    {
        _config.SceneLayers.Remove(layer.Config);
        _save();

        Layers.Remove(layer);

        if (SelectedLayer == layer)
            SelectedLayer = null;

        OnPropertyChanged(nameof(HasLayers));
        RestartLive();
    }

    partial void OnSelectedItemChanged(SceneItemViewModel? value)
    {
        // Справа показываются настройки либо элемента, либо слоя
        if (value != null)
            SelectedLayer = null;
    }

    partial void OnSelectedLayerChanged(SceneLayerViewModel? value)
    {
        if (value != null)
            SelectedItem = null;

        Version++;
    }

    [RelayCommand]
    private void TogglePreview()
    {
        if (IsPreviewing)
            StopPreview();
        else
            StartPreview();
    }

    /// <summary>
    /// Запускает эффект на всех доступных устройствах сцены.
    /// </summary>
    private bool StartEffect(EffectOption option)
    {
        Rebuild();

        var devices = EffectDevices();

        if (devices.Count == 0)
        {
            StatusText = "На сцене нет доступных лент для эффекта. Добавьте их, верните им режим «В связке» или «Отдельно» "
                         + "или включите предпросмотр, чтобы посмотреть эффект на схеме";
            return false;
        }

        StopPreview();
        StopEffect();
        _stopConflicts(devices);

        SceneLayerSource source;

        try
        {
            source = CreateSource(option);
        }
        catch (Exception e)
        {
            // Например, в системе нет устройства вывода звука для музыкального эффекта
            StatusText = $"Не удалось запустить «{option.Title}»: {e.Message}";
            return false;
        }

        // Время идёт с обычной скоростью: скорость каждого эффекта и слоя источник учитывает сам
        _runner.Start(source, devices, () => new EffectSettings(RgbColor.White, Brightness, 1));

        _liveSource = source;
        _isLiveSourceOwned = false;
        IsRunning = true;
        _frameTimer.Start();

        StatusText = Layers.Count > 0
            ? $"«{option.Title}» играет на всей сцене, слоёв поверх — {Layers.Count}: устройств — {devices.Count}"
            : $"«{option.Title}» играет на всей сцене: устройств — {devices.Count}";
        return true;
    }

    private void StopEffect()
    {
        if (!IsRunning)
            return;

        _runner.Stop();
        _liveSource = null;
        IsRunning = false;
        _frameTimer.Stop();
        Effects.MarkRunning(null);

        StatusText = "Эффект сцены остановлен";
        Version++;
    }

    /// <summary>
    /// Показывает выбранный эффект только на схеме, ничего не отправляя на ленты.
    /// </summary>
    private void StartPreview()
    {
        if (IsRunning || Effects.SelectedEffect is not { } option)
            return;

        StopPreview();
        Rebuild();

        try
        {
            _liveSource = CreateSource(option);
        }
        catch (Exception e)
        {
            StatusText = $"Не удалось показать «{option.Title}»: {e.Message}";
            return;
        }

        _isLiveSourceOwned = true;
        _previewPhase = 0;
        _previewLastTick = 0;
        _previewClock.Restart();

        IsPreviewing = true;
        _frameTimer.Start();
    }

    private void StopPreview()
    {
        if (!IsPreviewing)
            return;

        if (_isLiveSourceOwned)
            _liveSource?.Dispose();

        _liveSource = null;
        IsPreviewing = false;
        _frameTimer.Stop();
        Version++;
    }

    private void OnFrame()
    {
        // Предпросмотр сам двигает время эффекта; запущенный на лентах эффект двигает EffectRunner
        if (IsPreviewing && _liveSource != null)
        {
            var now = _previewClock.Elapsed.TotalSeconds;
            _previewPhase += now - _previewLastTick;
            _previewLastTick = now;

            _liveSource.BeginFrame(_previewPhase, new EffectSettings(RgbColor.White, Brightness, 1));
        }

        Version++;
    }

    private void OnItemEdited(SceneItemViewModel item)
    {
        Rebuild();
        _save();

        // Устройство включили в эффект сцены или убрали из него — перезапускаем эффект на новом наборе устройств
        if (!IsRunning || EffectDevices().ToHashSet().SetEquals(_runner.Devices))
            return;

        RestartLive();
    }

    private void OnLayerEdited(SceneLayerViewModel layer, bool isEffectChanged)
    {
        _save();

        // Границы играющий эффект подхватывает сам, а новый эффект слоя нужно собрать заново
        if (isEffectChanged)
            RestartLive();
        else
            Version++;
    }

    /// <summary>
    /// Собирает играющий эффект сцены заново — после смены устройств или слоёв.
    /// </summary>
    private void RestartLive()
    {
        if (IsPreviewing)
        {
            StartPreview();
            return;
        }

        if (!IsRunning || Effects.RunningEffect is not { } running)
            return;

        // Перезапуск сбрасывает отметку о запущенном эффекте в галерее — ставим её обратно.
        // Не запустился — значит, играть больше не на чем: из эффекта убрали последнее устройство
        if (StartEffect(running))
            Effects.MarkRunning(running);
        else
            StopEffect();
    }

    /// <summary>
    /// Эффект всей сцены вместе со слоями поверх него.
    /// </summary>
    private SceneLayerSource CreateSource(EffectOption option)
    {
        var layers = Layers
            .Where(l => l.Effect != null)
            .Select(l => (l.Effect!.Definition, l.Config))
            .ToList();

        return new SceneLayerSource(option.Definition, _map, layers, Effects.ValuesFor, Effects.SpeedOf);
    }

    /// <summary>
    /// Доступные устройства сцены, на которых играет её эффект, — все, кроме выключенных из него.
    /// </summary>
    private List<LedDevice> EffectDevices()
    {
        return Items
            .Where(i => i.Config.Projection != SceneProjection.Off)
            .Select(i => i.Shape?.DeviceId)
            .OfType<string>()
            .Select(FindDevice)
            .Where(d => d is { Device.IsReady: true })
            .Select(d => d!.Device)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Ширина и высота элемента на сцене с его текущим поворотом.
    /// </summary>
    private (double Width, double Height) SizeOf(SceneItemViewModel item)
    {
        item.Shape = BuildShape(item);
        var (min, max) = item.Shape.Bounds;

        return (max.X - min.X, max.Y - min.Y);
    }

    /// <summary>
    /// Чем ставить устройство на сцену: клавиатуру — клавиатурой, ленту с собранной формой из шестигранников — панелью, остальное — лентой.
    /// </summary>
    private SceneItemKind DeviceKind(string key)
    {
        var device = FindDevice(key);
        var hasHexShape = device?.Strip != null && _config.HexLayouts.GetValueOrDefault(key)?.Cells.Count > 0;

        return device?.Device is IKeyboardDevice ? SceneItemKind.Keyboard
            : hasHexShape ? SceneItemKind.Panel
            : SceneItemKind.Strip;
    }

    /// <summary>
    /// Пересчитывает формы всех элементов и места светодиодов для эффектов.
    /// </summary>
    private void Rebuild()
    {
        var monitors = Items.Where(i => i.Monitor != null).ToList();

        foreach (var item in Items)
            item.Shape = BuildShape(item);

        var origin = monitors.FirstOrDefault(m => m.Monitor!.IsPrimary) ?? monitors.FirstOrDefault();
        var main = monitors.FirstOrDefault(m => m.Monitor!.Key == MainMonitor.Key) ?? origin;

        _map.Update(Items.Select(i => i.Shape).OfType<SceneShape>().ToList(), origin?.Shape?.Center, main?.Shape);
        Version++;
    }

    private SceneShape BuildShape(SceneItemViewModel item)
    {
        if (item.Monitor is { } monitor)
        {
            var monitorConfig = _config.Monitors.GetValueOrDefault(monitor.Key);
            var rotation = monitorConfig?.Rotation ?? MonitorRotation.None;
            var aspect = MonitorSyncSource.PhysicalAspect(new MonitorBinding(monitor, rotation, null));

            var backlight = monitorConfig?.DeviceId is { } id ? DeviceInfo(id) : null;
            var layout = backlight != null ? _config.ScreenLayouts.GetValueOrDefault(backlight.Id) : null;

            return SceneGeometry.BuildMonitor(item.Config, new SceneMonitorInfo(monitor, aspect, backlight, layout));
        }

        var device = DeviceInfo(item.Config.Key);

        return item.Config.Kind switch
        {
            SceneItemKind.Panel => SceneGeometry.BuildPanel(item.Config, device),
            SceneItemKind.Keyboard => SceneGeometry.BuildKeyboard(item.Config, device),
            _ => SceneGeometry.BuildStrip(item.Config, device)
        };
    }

    /// <summary>
    /// Сведения об устройстве для расчёта формы; у ненайденного устройства — как у обычной ленты одного цвета.
    /// </summary>
    private SceneDeviceInfo DeviceInfo(string id)
    {
        var device = FindDevice(id);

        return new SceneDeviceInfo(id, device?.Strip?.LedCount ?? 0, _config.HexLayouts.GetValueOrDefault(id),
            (device?.Device as IKeyboardDevice)?.Keys);
    }

    /// <summary>
    /// Ленты подсветки подключённых мониторов: они стоят на сцене вместе со своим монитором.
    /// </summary>
    private HashSet<string> BacklightIds(IReadOnlyList<MonitorInfo> monitors)
    {
        return monitors
            .Select(m => _config.Monitors.GetValueOrDefault(m.Key)?.DeviceId)
            .OfType<string>()
            .ToHashSet();
    }

    private DeviceItemViewModel? FindDevice(string id)
    {
        return _devices.FirstOrDefault(d => d.Id == id);
    }

    private static string MonitorTitle(MonitorInfo monitor)
    {
        return monitor.IsPrimary ? $"Монитор {monitor.Number} · основной" : $"Монитор {monitor.Number}";
    }
}

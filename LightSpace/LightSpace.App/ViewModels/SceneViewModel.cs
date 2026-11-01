using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.App.Services;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вкладка «Сцена»: устройства с живыми цветами и что на них играет — одно на всей сцене, своё у каждой группы
/// или своё у каждого устройства. Панель выбранного — название и цвет обводки группы, яркость и содержимое: эффект,
/// сцена, экран, музыка, игра или встроенный эффект с его настройками. Все правки сразу попадают в настройки
/// и уходят движку новым снимком.
/// </summary>
public sealed partial class SceneViewModel : ObservableObject
{
    // Живые цвета и превью обновляются не чаще 30 раз в секунду
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1.0 / 30);

    // Сколько шагов расстановки можно отменить
    private const int UndoLimit = 50;

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly FrameLoop _loop;
    private readonly Action _contentChanged;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private SceneGroupViewModel? _selectedGroup;
    private TimeSpan _lastTick;

    // Панель всей сцены: в режиме «Вся сцена» она открыта всегда; null — устройств нет
    private SceneGroupViewModel? _sceneGroup;

    // Панели устройств в режиме «Устройства», по идентификатору устройства
    private readonly Dictionary<string, SceneGroupViewModel> _deviceLighting = new();

    // Места всех устройств перед каждым шагом в режиме редактирования расстановки, последний шаг — в конце.
    // Ползунок размера двигается мелкими шагами: подряд идущие изменения одного устройства — один шаг отмены
    private readonly LinkedList<Dictionary<string, DevicePlacement?>> _undo = new();
    private string? _lastUndoKey;

    // Режим «Редактировать расстановку»: только в нём устройства двигаются мышью
    [ObservableProperty]
    private bool _isEditingLayout;

    /// <summary>
    /// contentChanged вызывается после каждого изменения: режима сцены, состава групп, названия, цвета, яркости,
    /// содержимого и его настроек. sources — откуда превью экрана, музыки и игры берут картинку, звук и события игр.
    /// </summary>
    public SceneViewModel(AppConfig config, DeviceManager manager, FrameLoop loop, SourceHub sources, Action contentChanged)
    {
        _config = config;
        _manager = manager;
        _loop = loop;
        _contentChanged = contentChanged;

        Editor = new EffectEditorViewModel(contentChanged);
        SceneEditor = new SceneEditorViewModel(config, manager, contentChanged);
        ScreenEditor = new ScreenEditorViewModel(sources, contentChanged);
        MusicEditor = new MusicEditorViewModel(sources, contentChanged);
        GameEditor = new GameEditorViewModel(sources, contentChanged);
        BuiltinEditor = new BuiltinEditorViewModel(manager, contentChanged);

        _manager.DevicesChanged += (_, _) => SyncDevices();
        SyncDevices();

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();
    }

    /// <summary>
    /// Галерея и настройки эффекта, который играет у выбранного.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Холст и слои, которые играют у выбранного, если его содержимое — сцена.
    /// </summary>
    public SceneEditorViewModel SceneEditor { get; }

    /// <summary>
    /// Монитор и цвета, если содержимое выбранного — экран.
    /// </summary>
    public ScreenEditorViewModel ScreenEditor { get; }

    /// <summary>
    /// Источник звука, режим и цвета, если содержимое выбранного — музыка.
    /// </summary>
    public MusicEditorViewModel MusicEditor { get; }

    /// <summary>
    /// Игра, фон, полоски и события, если содержимое выбранного — игра.
    /// </summary>
    public GameEditorViewModel GameEditor { get; }

    /// <summary>
    /// Эффект каждого устройства, скорость и цвет, если содержимое выбранного — встроенный эффект.
    /// </summary>
    public BuiltinEditorViewModel BuiltinEditor { get; }

    /// <summary>
    /// Типы содержимого для сегментов: эффект, сцена, экран, музыка, игра, встроенный эффект.
    /// </summary>
    public IReadOnlyList<ContentKindOption> ContentKinds => ContentKindOption.All;

    /// <summary>
    /// Тип содержимого выбранного. Сцена без слоёв получает первый слой во всю сцену с эффектом, который играл до этого.
    /// </summary>
    public ContentKindOption? SelectedContentKind
    {
        get => _selectedGroup?.Content.Kind switch
        {
            null => null,
            ContentKind.Scene => ContentKindOption.Scene,
            ContentKind.Screen => ContentKindOption.Screen,
            ContentKind.Music => ContentKindOption.Music,
            ContentKind.Game => ContentKindOption.Game,
            ContentKind.Builtin => ContentKindOption.Builtin,
            _ => ContentKindOption.Effect
        };
        set
        {
            var content = _selectedGroup?.Content;

            var kind = value == ContentKindOption.Scene ? ContentKind.Scene
                : value == ContentKindOption.Screen ? ContentKind.Screen
                : value == ContentKindOption.Music ? ContentKind.Music
                : value == ContentKindOption.Game ? ContentKind.Game
                : value == ContentKindOption.Builtin ? ContentKind.Builtin
                : ContentKind.Effect;

            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (content == null || value == null || !value.IsAvailable || kind == content.Kind)
                return;

            content.Kind = kind;

            if (kind == ContentKind.Scene && content.Layers.Count == 0)
            {
                content.Layers.Add(SceneLayerConfig.Covering(content));
                SceneEditor.Edit(content);
            }

            OnPropertyChanged();
            OnContentKindChanged();
            _contentChanged();
        }
    }

    public bool IsEffectContent => _selectedGroup?.Content.Kind == ContentKind.Effect;

    public bool IsSceneContent => _selectedGroup?.Content.Kind == ContentKind.Scene;

    public bool IsScreenContent => _selectedGroup?.Content.Kind == ContentKind.Screen;

    public bool IsMusicContent => _selectedGroup?.Content.Kind == ContentKind.Music;

    public bool IsGameContent => _selectedGroup?.Content.Kind == ContentKind.Game;

    public bool IsBuiltinContent => _selectedGroup?.Content.Kind == ContentKind.Builtin;

    /// <summary>
    /// На всех устройствах играет одно и то же; группы и настройки устройств сохраняются до возврата к ним.
    /// </summary>
    public bool IsWholeScene
    {
        get => _config.Mode == SceneMode.WholeScene;
        set
        {
            if (value)
                SetMode(SceneMode.WholeScene);
        }
    }

    /// <summary>
    /// У групп устройств своё содержимое.
    /// </summary>
    public bool IsGroupsMode
    {
        get => _config.Mode == SceneMode.Groups;
        set
        {
            if (value)
                SetMode(SceneMode.Groups);
        }
    }

    /// <summary>
    /// У каждого устройства своё содержимое, группы не действуют.
    /// </summary>
    public bool IsDevicesMode
    {
        get => _config.Mode == SceneMode.Devices;
        set
        {
            if (value)
                SetMode(SceneMode.Devices);
        }
    }

    public ObservableCollection<SceneDeviceViewModel> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    /// <summary>
    /// Группы на сцене, включая группы-одиночки, в порядке своих первых устройств.
    /// </summary>
    public ObservableCollection<SceneGroupViewModel> Groups { get; } = new();

    /// <summary>
    /// Группа, чья панель открыта справа; null — ничего не выбрано или выделено несколько устройств.
    /// </summary>
    public SceneGroupViewModel? SelectedGroup => _selectedGroup;

    public bool HasSelection => _selectedGroup != null;

    /// <summary>
    /// Выделено несколько устройств — их можно сделать группой.
    /// </summary>
    public bool CanMakeGroup => IsGroupsMode && SelectedDevices.Count >= 2;

    /// <summary>
    /// Сколько устройств выделено, для карточки «Сделать группой».
    /// </summary>
    public string SelectionText => $"Выбрано {DevicesText(SelectedDevices.Count)}";

    /// <summary>
    /// Заголовок панели, когда группа не выбрана.
    /// </summary>
    public string NoSelectionTitle => CanMakeGroup ? SelectionText : "Ничего не выбрано";

    /// <summary>
    /// Подсказка в панели, когда группа не выбрана.
    /// </summary>
    public string NoSelectionHint => CanMakeGroup
        ? "Соберите их в группу кнопкой «Сделать группой» внизу сцены — на всех устройствах группы будет играть одно и то же."
        : IsDevicesMode
            ? "Выберите устройство на сцене, чтобы настроить, что на нём играет."
            : "Выберите группу или устройство на сцене, чтобы настроить, что на нём играет. Ctrl+клик или рамка мышью выделяют несколько устройств — их можно сделать группой.";

    /// <summary>
    /// Показать карточку «Сделать группой»: выделено несколько устройств и расстановку сейчас не редактируют.
    /// </summary>
    public bool IsGroupActionShown => CanMakeGroup && !IsEditingLayout;

    /// <summary>
    /// Подсказка над сценой: что можно сделать мышью сейчас.
    /// </summary>
    public string SceneHint => IsEditingLayout
        ? "Перетаскивайте устройства мышью. Выделите одно устройство, чтобы повернуть его или поменять размер."
        : IsWholeScene
            ? "То, что выбрано справа, играет сразу на всех устройствах."
            : IsDevicesMode
                ? "Кликните устройство, чтобы выбрать, что играет на нём."
                : "Ctrl+клик или рамка мышью выделяют несколько устройств — их можно сделать группой.";

    /// <summary>
    /// Устройство, которое поворачивают и меняют в размере: единственное выделенное в режиме редактирования расстановки.
    /// </summary>
    public SceneDeviceViewModel? EditedDevice => IsEditingLayout && SelectedDevices.Count == 1 ? SelectedDevices[0] : null;

    public bool HasEditedDevice => EditedDevice != null;

    /// <summary>
    /// Размер выбранного в режиме редактирования устройства на сцене.
    /// </summary>
    public double EditedSize
    {
        get => EditedDevice?.SceneSize ?? 0;
        set
        {
            if (EditedDevice is not { } device)
                return;

            RememberPlacements("size:" + device.Id);
            device.SceneSize = value;
            OnPropertyChanged();
            _contentChanged();
        }
    }

    /// <summary>
    /// Поворот выбранного в режиме редактирования устройства в градусах, как он вводится в поле: любой угол, не только
    /// четверть оборота. Не число — поле возвращает прежний угол.
    /// </summary>
    public string EditedAngleText
    {
        get => EditedDevice is { } device ? NumberText.Format(device.Angle) : "";
        set
        {
            if (EditedDevice is not { } device)
                return;

            if (NumberText.TryParse(value, out var degrees))
            {
                RememberPlacements();
                device.SetRotation(degrees);
                _contentChanged();
            }

            OnPropertyChanged();
        }
    }

    private IReadOnlyList<SceneDeviceViewModel> SelectedDevices => Devices.Where(d => d.IsSelected).ToList();

    /// <summary>
    /// Устройства, которые поедут, если потянуть мышью за device: всё выделение, если device в нём, иначе только device.
    /// </summary>
    public IReadOnlyList<SceneDeviceViewModel> DevicesToMove(SceneDeviceViewModel device)
    {
        return device.IsSelected ? SelectedDevices : new[] { device };
    }

    /// <summary>
    /// Устройства начали тянуть мышью: их места до перетаскивания запоминаются для отмены.
    /// </summary>
    public void BeginMove()
    {
        RememberPlacements();
    }

    /// <summary>
    /// Сдвигает устройства на dx, dy единиц сцены, пока их тянут мышью; в настройки и движку уходит в <see cref="FinishMove"/>.
    /// </summary>
    public void MoveDevices(IReadOnlyList<SceneDeviceViewModel> devices, double dx, double dy)
    {
        foreach (var device in devices)
            device.MoveBy(dx, dy);
    }

    /// <summary>
    /// Устройства отпустили: новые места сохраняются, и эффекты сцены ложатся на них по-новому.
    /// </summary>
    public void FinishMove()
    {
        _contentChanged();
    }

    /// <summary>
    /// Клик по устройству: без Ctrl выделяет только его и открывает его группу, с Ctrl — добавляет к выделению или убирает из него.
    /// </summary>
    public void ClickDevice(SceneDeviceViewModel device, bool isAdding)
    {
        if (isAdding)
        {
            device.IsSelected = !device.IsSelected;
        }
        else
        {
            foreach (var other in Devices)
                other.IsSelected = other == device;
        }

        OnDevicesSelected();
    }

    /// <summary>
    /// Клик по рамке группы: без Ctrl открывает панель группы, с Ctrl — добавляет все её устройства к выделению.
    /// </summary>
    public void ClickGroup(SceneGroupViewModel group, bool isAdding)
    {
        if (isAdding)
        {
            foreach (var device in group.Devices)
                device.IsSelected = true;

            OnDevicesSelected();
            return;
        }

        foreach (var device in Devices)
            device.IsSelected = false;

        Select(group);
        OnSelectionCountChanged();
    }

    /// <summary>
    /// Клик по пустому месту сцены снимает выделение и закрывает панель группы.
    /// </summary>
    public void ClickEmpty()
    {
        ClearSelection();
    }

    /// <summary>
    /// Выделение рамкой: выделяет устройства, которых коснулась рамка; с Ctrl — добавляет их к уже выделенным.
    /// </summary>
    public void SelectDevices(IReadOnlyCollection<SceneDeviceViewModel> devices, bool isAdding)
    {
        foreach (var device in Devices)
            device.IsSelected = devices.Contains(device) || (isAdding && device.IsSelected);

        OnDevicesSelected();
    }

    /// <summary>
    /// Включает живые цвета и превью, пока сцену видно, и выключает, когда окно скрыто или открыта другая вкладка.
    /// </summary>
    public void SetLive(bool isLive)
    {
        if (isLive == _liveTimer.IsEnabled)
            return;

        _loop.IsLiveColorsWanted = isLive;

        if (!isLive)
        {
            _liveTimer.Stop();
            _clock.Stop();

            // Пока превью не видно, монитор и звук для него не снимаются
            ScreenEditor.StopPreview();
            MusicEditor.StopPreview();
            GameEditor.StopPreview();
            return;
        }

        // Пока сцену не видели, устройства могли переставить
        SceneEditor.RefreshDevices();

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// Собирает выделенные устройства в новую группу с названием по умолчанию и свободным цветом из палитры.
    /// </summary>
    [RelayCommand]
    private void MakeGroup()
    {
        var ids = SelectedDevices.Select(d => d.Id).ToList();

        if (ids.Count < 2)
            return;

        var group = _config.MakeGroup(ids, NextGroupName(), NextGroupColor());

        foreach (var device in Devices)
            device.IsSelected = false;

        SyncDevices();
        Select(Groups.FirstOrDefault(g => g.Id == group.Id));
        OnSelectionCountChanged();
        _contentChanged();
    }

    /// <summary>
    /// Автокомпоновка: расставляет все устройства заново так, как они обычно стоят на столе; их размеры сохраняются.
    /// </summary>
    [RelayCommand]
    private void AutoArrange()
    {
        RememberPlacements();

        var placements = SceneLayout.Arrange(_manager.Devices
            .Select(d => (d.Id, d.Geometry, d.Settings.Placement?.Size ?? 0))
            .ToList());

        foreach (var device in _manager.Devices)
            device.Settings.Placement = placements[device.Id];

        foreach (var device in Devices)
            device.OnPlacementChanged();

        OnPropertyChanged(nameof(EditedSize));
        OnPropertyChanged(nameof(EditedAngleText));
        _contentChanged();
    }

    /// <summary>
    /// Поворачивает выбранное в режиме редактирования устройство на четверть оборота.
    /// </summary>
    [RelayCommand]
    private void RotateDevice()
    {
        if (EditedDevice is not { } device)
            return;

        RememberPlacements();
        device.Rotate();
        OnPropertyChanged(nameof(EditedAngleText));
        _contentChanged();
    }

    /// <summary>
    /// Выходит из режима редактирования расстановки.
    /// </summary>
    [RelayCommand]
    private void FinishEditing()
    {
        IsEditingLayout = false;
    }

    /// <summary>
    /// Ctrl+Z в режиме редактирования расстановки: возвращает места всех устройств, какими они были до последнего шага.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Last is not { } last)
            return;

        _undo.RemoveLast();
        _lastUndoKey = null;

        foreach (var device in _manager.Devices)
        {
            // Устройство, которое нашлось уже после этого шага, остаётся на своём месте
            if (last.Value.TryGetValue(device.Id, out var placement))
                device.Settings.Placement = placement;
        }

        foreach (var device in Devices)
            device.OnPlacementChanged();

        OnPropertyChanged(nameof(EditedSize));
        OnPropertyChanged(nameof(EditedAngleText));
        UndoCommand.NotifyCanExecuteChanged();
        _contentChanged();
    }

    private bool CanUndo()
    {
        return IsEditingLayout && _undo.Count > 0;
    }

    /// <summary>
    /// Запоминает места всех устройств перед шагом расстановки. key — что меняется: подряд идущие шаги с одним key
    /// отменяются вместе; null — каждый шаг отдельно.
    /// </summary>
    private void RememberPlacements(string? key = null)
    {
        if (key != null && key == _lastUndoKey)
            return;

        _lastUndoKey = key;
        _undo.AddLast(_manager.Devices.ToDictionary(d => d.Id, d => d.Settings.Placement?.Clone()));

        if (_undo.Count > UndoLimit)
            _undo.RemoveFirst();

        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingLayoutChanged(bool value)
    {
        // Отмена — только в пределах одного захода в режим редактирования
        _undo.Clear();
        _lastUndoKey = null;
        UndoCommand.NotifyCanExecuteChanged();

        // Холст сцены «По умолчанию» показывает устройства на их новых местах
        if (!value)
            SceneEditor.RefreshDevices();

        OnPropertyChanged(nameof(SceneHint));
        OnSelectionCountChanged();
    }

    /// <summary>
    /// Снимает выделение устройств и закрывает панель группы.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var device in Devices)
            device.IsSelected = false;

        Select(null);
        OnSelectionCountChanged();
    }

    /// <summary>
    /// Распускает выбранную группу после подтверждения: каждое её устройство продолжает играть то же самое отдельно.
    /// </summary>
    [RelayCommand]
    private async Task UngroupAsync()
    {
        var group = _selectedGroup;

        if (group == null || group.IsSolo)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Разгруппировать «{group.Title}»?",
            "Каждое устройство группы станет отдельным и продолжит играть то же, что сейчас. Название и цвет группы пропадут.",
            "Разгруппировать", isDanger: true);

        if (!confirmed)
            return;

        _config.Ungroup(group.Settings);

        Select(null);
        SyncDevices();
        _contentChanged();
    }

    /// <summary>
    /// Убирает устройство из выбранной группы после подтверждения: оно продолжает играть то же самое отдельно.
    /// </summary>
    [RelayCommand]
    private async Task RemoveFromGroupAsync(SceneDeviceViewModel? device)
    {
        var group = _selectedGroup;

        if (device == null || group == null || group.IsSolo)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Убрать «{device.Name}» из группы «{group.Title}»?",
            "Устройство станет отдельным и продолжит играть то же, что сейчас.", "Убрать", isDanger: true);

        if (!confirmed)
            return;

        _config.TakeOutOfGroup(device.Id);

        // Если в группе никого не осталось, она пропала, и панель закрывается
        SyncDevices();
        _contentChanged();
    }

    /// <summary>
    /// Выделение устройств изменилось: одно устройство открывает панель своей группы (в режиме «Устройства» — свою),
    /// несколько — карточку «Сделать группой».
    /// </summary>
    private void OnDevicesSelected()
    {
        var selected = SelectedDevices;
        SceneGroupViewModel? panel = null;

        if (selected.Count == 1)
        {
            panel = IsDevicesMode
                ? _deviceLighting.GetValueOrDefault(selected[0].Id)
                : Groups.FirstOrDefault(g => g.Devices.Contains(selected[0]));
        }

        Select(panel);
        OnSelectionCountChanged();
    }

    /// <summary>
    /// Переключает, что играет на устройствах: вся сцена, группы или каждое устройство отдельно.
    /// </summary>
    private void SetMode(SceneMode mode)
    {
        if (_config.Mode == mode)
            return;

        _config.Mode = mode;

        // Выделение относилось к прежнему режиму; во всей сцене сразу открывается её панель
        foreach (var device in Devices)
            device.IsSelected = false;

        Select(null);

        OnPropertyChanged(nameof(IsWholeScene));
        OnPropertyChanged(nameof(IsGroupsMode));
        OnPropertyChanged(nameof(IsDevicesMode));
        OnPropertyChanged(nameof(SceneHint));
        OnSelectionCountChanged();
        _contentChanged();
    }

    /// <summary>
    /// Сменился тип содержимого выбранного или само выбранное: показываются редактор и превью этого типа,
    /// превью экрана, музыки и игры, которых больше не видно, отпускают свои источники.
    /// </summary>
    private void OnContentKindChanged()
    {
        OnPropertyChanged(nameof(IsEffectContent));
        OnPropertyChanged(nameof(IsSceneContent));
        OnPropertyChanged(nameof(IsScreenContent));
        OnPropertyChanged(nameof(IsMusicContent));
        OnPropertyChanged(nameof(IsGameContent));
        OnPropertyChanged(nameof(IsBuiltinContent));

        if (!IsScreenContent)
            ScreenEditor.StopPreview();

        if (!IsMusicContent)
            MusicEditor.StopPreview();

        if (!IsGameContent)
            GameEditor.StopPreview();
    }

    private void OnSelectionCountChanged()
    {
        OnPropertyChanged(nameof(CanMakeGroup));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(NoSelectionTitle));
        OnPropertyChanged(nameof(NoSelectionHint));
        OnPropertyChanged(nameof(IsGroupActionShown));
        OnPropertyChanged(nameof(EditedDevice));
        OnPropertyChanged(nameof(HasEditedDevice));
        OnPropertyChanged(nameof(EditedSize));
        OnPropertyChanged(nameof(EditedAngleText));
    }

    /// <summary>
    /// Открывает панель группы или устройства: редакторы показывают его содержимое.
    /// </summary>
    private void Select(SceneGroupViewModel? group)
    {
        // Во всей сцене панель одна — её собственная, что бы ни выбрали на карте
        if (IsWholeScene)
            group = _sceneGroup;

        if (_selectedGroup != null)
            _selectedGroup.IsSelected = false;

        _selectedGroup = group;

        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedContentKind));

        // Рамку на карте рисуют только группы; превью плиток рисуются с настройками эффектов выбранного
        if (group != null)
            group.IsSelected = true;

        var content = group?.Content;

        Editor.Edit(content);
        SceneEditor.Edit(content);
        ScreenEditor.Edit(content);
        MusicEditor.Edit(content);
        GameEditor.Edit(content);
        BuiltinEditor.Edit(content);

        OnContentKindChanged();
    }

    /// <summary>
    /// Название новой группы: «Группа N» с первым свободным номером.
    /// </summary>
    public string NextGroupName()
    {
        var names = _config.Groups.Select(g => g.Name).ToHashSet();
        var number = 1;

        while (names.Contains($"Группа {number}"))
            number++;

        return $"Группа {number}";
    }

    /// <summary>
    /// Цвет обводки новой группы: первый из палитры, которого ещё нет у других групп; если заняты все — по кругу.
    /// </summary>
    public string NextGroupColor()
    {
        var palette = (Application.Current.TryFindResource("GroupPalette") as Color[] ?? Array.Empty<Color>())
            .Select(c => new RgbColor(c.R, c.G, c.B).ToHex())
            .ToList();

        if (palette.Count == 0)
            return "";

        var used = _config.Groups.Where(g => !g.IsSolo).Select(g => g.Color).ToList();

        return palette.FirstOrDefault(c => !used.Contains(c)) ?? palette[used.Count % palette.Count];
    }

    /// <summary>
    /// «1 устройство», «2 устройства», «5 устройств».
    /// </summary>
    private static string DevicesText(int count)
    {
        var lastTwo = count % 100;
        var last = count % 10;

        var word = lastTwo is >= 11 and <= 14 ? "устройств"
            : last == 1 ? "устройство"
            : last is >= 2 and <= 4 ? "устройства"
            : "устройств";

        return $"{count} {word}";
    }

    /// <summary>
    /// Приводит устройства и группы сцены к списку менеджера и настройкам групп: после поиска у устройств могли смениться
    /// возможности, после правок групп — их состав. Выделение и выбранная группа сохраняются, если они ещё есть.
    /// </summary>
    private void SyncDevices()
    {
        var selectedGroupId = _selectedGroup?.Id;
        var selectedDeviceIds = Devices.Where(d => d.IsSelected).Select(d => d.Id).ToHashSet();
        var groups = new List<(GroupConfig Group, List<SceneDeviceViewModel> Members)>();

        PlaceNewDevices();

        Devices.Clear();
        Groups.Clear();

        foreach (var device in _manager.Devices)
        {
            var item = new SceneDeviceViewModel(device) { IsSelected = selectedDeviceIds.Contains(device.Id) };

            Devices.Add(item);

            // Группы идут в порядке своих первых устройств; устройство без группы — само себе группа
            var group = _config.EnsureGroup(device.Id);
            var members = groups.FirstOrDefault(g => g.Group == group).Members;

            if (members == null)
            {
                members = new List<SceneDeviceViewModel>();
                groups.Add((group, members));
            }

            members.Add(item);
        }

        foreach (var (group, members) in groups)
            Groups.Add(new SceneGroupViewModel(group, members, _contentChanged));

        _sceneGroup = Devices.Count > 0
            ? new SceneGroupViewModel(_config.Scene, Devices.ToList(), _contentChanged) { IsWholeScene = true }
            : null;

        _deviceLighting.Clear();

        foreach (var device in Devices)
        {
            var lighting = _config.EnsureDeviceLighting(device.Id);
            _deviceLighting[device.Id] = new SceneGroupViewModel(lighting, new[] { device }, _contentChanged) { IsDeviceLighting = true };
        }

        _selectedGroup = null;
        Select(Groups.Concat(_deviceLighting.Values).FirstOrDefault(g => g.Id == selectedGroupId));

        OnPropertyChanged(nameof(HasDevices));
        OnSelectionCountChanged();
    }

    /// <summary>
    /// Даёт место на сцене устройствам, у которых его ещё нет: если не расставлено ни одно устройство — автокомпоновкой
    /// всей сцены, иначе каждое новое встаёт на первое свободное место. Места попадут в файл со следующим сохранением.
    /// </summary>
    private void PlaceNewDevices()
    {
        var devices = _manager.Devices;

        if (devices.All(d => d.Settings.Placement == null))
        {
            var placements = SceneLayout.Arrange(devices.Select(d => (d.Id, d.Geometry, 0.0)).ToList());

            foreach (var device in devices)
                device.Settings.Placement = placements[device.Id];

            return;
        }

        var taken = devices
            .Where(d => d.Settings.Placement != null)
            .Select(d => SceneProjection.BoundsOf(d.Geometry, d.Settings.Placement!))
            .ToList();

        foreach (var device in devices.Where(d => d.Settings.Placement == null))
        {
            var geometry = device.Geometry;
            var placement = SceneLayout.FindFreeSpot(geometry, taken);

            device.Settings.Placement = placement;
            taken.Add(SceneProjection.BoundsOf(geometry, placement));
        }
    }

    /// <summary>
    /// Один кадр живой сцены: последние цвета устройств от движка и следующий кадр превью на плитках.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // У сцены свой холст и своя галерея — эффекта выбранного слоя
        if (IsSceneContent)
            SceneEditor.Advance(seconds);
        else if (IsScreenContent)
            ScreenEditor.Advance(seconds);
        else if (IsMusicContent)
            MusicEditor.Advance(seconds);
        else if (IsGameContent)
            GameEditor.Advance(seconds);
        else if (IsEffectContent)
            Editor.Advance(seconds);

        var live = _loop.LiveColors;

        foreach (var device in Devices)
        {
            var colors = live is { IsLightingEnabled: true } && live.Devices.TryGetValue(device.Id, out var found) ? found : null;

            device.Show(colors?.Colors, colors?.Brightness ?? 100);
            device.IsBuiltinPlaying = colors?.IsBuiltin == true;
        }
    }
}

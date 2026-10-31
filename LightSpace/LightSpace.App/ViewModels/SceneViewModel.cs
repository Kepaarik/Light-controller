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
using LightSpace.Engine.Apps;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Scenarios;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вкладка «Сцена»: группы устройств с живыми цветами, выделение устройств и сборка их в группы, панель выбранной группы —
/// название, цвет обводки, сценарии, яркость и содержимое «По умолчанию»: галерея плиток с живыми превью и настройки
/// выбранного эффекта. Все правки сразу попадают в настройки группы и уходят движку новым снимком.
/// </summary>
public sealed partial class SceneViewModel : ObservableObject
{
    // Живые цвета и превью обновляются не чаще 30 раз в секунду
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly FrameLoop _loop;
    private readonly AppWatcher _apps;
    private readonly Action _contentChanged;
    private readonly Action<GroupConfig> _addScenario;
    private readonly Action<string> _openScenario;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private SceneGroupViewModel? _selectedGroup;
    private TimeSpan _lastTick;

    // Режим «Редактировать расстановку»: только в нём устройства двигаются мышью
    [ObservableProperty]
    private bool _isEditingLayout;

    /// <summary>
    /// contentChanged вызывается после каждого изменения групп: их состава, названия, цвета, яркости, эффекта и его настроек,
    /// порядка сценариев. addScenario создаёт новый сценарий в группе и открывает его, openScenario открывает сценарий
    /// по идентификатору — оба на вкладке «Сценарии».
    /// </summary>
    public SceneViewModel(AppConfig config, DeviceManager manager, FrameLoop loop, AppWatcher apps, Action contentChanged,
        Action<GroupConfig> addScenario, Action<string> openScenario)
    {
        _config = config;
        _manager = manager;
        _loop = loop;
        _apps = apps;
        _contentChanged = contentChanged;
        _addScenario = addScenario;
        _openScenario = openScenario;

        Editor = new EffectEditorViewModel(contentChanged);
        SceneEditor = new SceneEditorViewModel(config, manager, contentChanged);

        _manager.DevicesChanged += (_, _) => SyncDevices();
        SyncDevices();

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();
    }

    /// <summary>
    /// Галерея и настройки эффекта, который играет в выбранной группе «По умолчанию».
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Холст и слои, которые играют в выбранной группе «По умолчанию», если её содержимое — сцена.
    /// </summary>
    public SceneEditorViewModel SceneEditor { get; }

    /// <summary>
    /// Типы содержимого «По умолчанию» для сегментов: эффект или сцена.
    /// </summary>
    public IReadOnlyList<ContentKindOption> DefaultKinds { get; } = new[] { ContentKindOption.Effect, ContentKindOption.Scene };

    /// <summary>
    /// Тип содержимого «По умолчанию» выбранной группы. Сцена без слоёв получает первый слой во всю сцену с эффектом,
    /// который играл до этого.
    /// </summary>
    public ContentKindOption? SelectedDefaultKind
    {
        get => _selectedGroup == null ? null : IsDefaultScene ? ContentKindOption.Scene : ContentKindOption.Effect;
        set
        {
            var content = _selectedGroup?.Content;
            var kind = value == ContentKindOption.Scene ? ContentKind.Scene : ContentKind.Effect;

            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (content == null || value == null || kind == content.Kind)
                return;

            content.Kind = kind;

            if (kind == ContentKind.Scene && content.Layers.Count == 0)
            {
                content.Layers.Add(SceneLayerConfig.Covering(content));
                SceneEditor.Edit(content);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDefaultScene));
            _contentChanged();
        }
    }

    /// <summary>
    /// Содержимое «По умолчанию» выбранной группы — сцена со слоями.
    /// </summary>
    public bool IsDefaultScene => _selectedGroup?.Content.Kind == ContentKind.Scene;

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
    public bool CanMakeGroup => SelectedDevices.Count >= 2;

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
            return;
        }

        // Пока сцену не видели, устройства могли переставить
        SceneEditor.RefreshDevices();

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// Обновляет списки сценариев групп: их названия, условия и какой сценарий сейчас играет.
    /// </summary>
    public void RefreshScenarios()
    {
        foreach (var group in Groups)
            group.SetScenarios(ScenariosOf(group.Settings));
    }

    /// <summary>
    /// Перетаскивание в списке сценариев выбранной группы: сценарий встаёт на новое место, важность меняется сразу.
    /// </summary>
    [RelayCommand]
    private void MoveScenario(ReorderMove? move)
    {
        var group = _selectedGroup;

        if (move == null || group == null || move.From >= group.Scenarios.Count || move.To >= group.Scenarios.Count)
            return;

        var moved = group.Scenarios[move.From].Id;
        var target = group.Scenarios[move.To].Id;
        var ids = group.Settings.ScenarioIds;

        // Вниз сценарий встаёт после того, на чьё место его протащили, вверх — перед ним
        ids.Remove(moved);
        ids.Insert(ids.IndexOf(target) + (move.To > move.From ? 1 : 0), moved);

        group.Scenarios.Move(move.From, move.To);
        _contentChanged();
    }

    /// <summary>
    /// Новый сценарий в выбранной группе: встаёт над «По умолчанию» и открывается на вкладке «Сценарии».
    /// </summary>
    [RelayCommand]
    private void AddScenario()
    {
        if (_selectedGroup != null)
            _addScenario(_selectedGroup.Settings);
    }

    /// <summary>
    /// Открывает сценарий на вкладке «Сценарии».
    /// </summary>
    [RelayCommand]
    private void OpenScenario(ScenarioRowViewModel? row)
    {
        if (row != null)
            _openScenario(row.Id);
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
        var placements = SceneLayout.Arrange(_manager.Devices
            .Select(d => (d.Id, d.Geometry, d.Settings.Placement?.Size ?? 0))
            .ToList());

        foreach (var device in _manager.Devices)
            device.Settings.Placement = placements[device.Id];

        foreach (var device in Devices)
            device.OnPlacementChanged();

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

    partial void OnIsEditingLayoutChanged(bool value)
    {
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
    /// Выделение устройств изменилось: одно устройство открывает панель своей группы, несколько — карточку «Сделать группой».
    /// </summary>
    private void OnDevicesSelected()
    {
        var selected = SelectedDevices;

        Select(selected.Count == 1 ? Groups.FirstOrDefault(g => g.Devices.Contains(selected[0])) : null);
        OnSelectionCountChanged();
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
    /// Открывает панель группы: галерея и настройки показывают её содержимое «По умолчанию».
    /// </summary>
    private void Select(SceneGroupViewModel? group)
    {
        if (_selectedGroup != null)
            _selectedGroup.IsSelected = false;

        _selectedGroup = group;

        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedDefaultKind));
        OnPropertyChanged(nameof(IsDefaultScene));

        if (group == null)
        {
            Editor.Edit(null);
            SceneEditor.Edit(null);
            return;
        }

        group.IsSelected = true;

        // Превью плиток рисуются с настройками эффектов этой группы
        Editor.Edit(group.Content);
        SceneEditor.Edit(group.Content);
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
            Groups.Add(new SceneGroupViewModel(group, members, ScenariosOf(group), _contentChanged));

        _selectedGroup = null;
        Select(Groups.FirstOrDefault(g => g.Id == selectedGroupId));

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
    /// Строки списка сценариев группы по важности, с отметкой того, который сейчас играет; «По умолчанию» в список не входит.
    /// </summary>
    private IReadOnlyList<ScenarioRowViewModel> ScenariosOf(GroupConfig group)
    {
        var playing = ScenarioSelector.Choose(_config, group, _apps).Scenario;

        return group.ScenarioIds
            .Select(id => _config.Scenarios.FirstOrDefault(s => s.Id == id))
            .OfType<ScenarioConfig>()
            .Select(s => new ScenarioRowViewModel(s.Id, ScenarioItemViewModel.TitleOf(s), ScenarioSelector.Describe(s.Condition),
                s == playing))
            .ToList();
    }

    /// <summary>
    /// Один кадр живой сцены: последние цвета устройств от движка и следующий кадр превью на плитках.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // У сцены «По умолчанию» свой холст и своя галерея — эффекта выбранного слоя
        if (IsDefaultScene)
            SceneEditor.Advance(seconds);
        else
            Editor.Advance(seconds);

        var live = _loop.LiveColors;

        foreach (var device in Devices)
        {
            var colors = live is { IsLightingEnabled: true } && live.Devices.TryGetValue(device.Id, out var found) ? found : null;

            device.Show(colors?.Colors, colors?.Brightness ?? 100);
        }
    }
}

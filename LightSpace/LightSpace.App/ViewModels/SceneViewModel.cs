using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Frames;

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
    private readonly Action _contentChanged;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private EffectTileViewModel _selectedTile;
    private SceneGroupViewModel? _selectedGroup;
    private TimeSpan _lastTick;

    /// <summary>
    /// contentChanged вызывается после каждого изменения групп: их состава, названия, цвета, яркости, эффекта и его настроек.
    /// </summary>
    public SceneViewModel(AppConfig config, DeviceManager manager, FrameLoop loop, Action contentChanged)
    {
        _config = config;
        _manager = manager;
        _loop = loop;
        _contentChanged = contentChanged;

        // Настройки плиток и выбранный эффект берутся у группы, когда её выбирают
        Tiles = EffectLibrary.All
            .Select(e => new EffectTileViewModel(e, EffectValues.Empty))
            .ToList();

        _selectedTile = Tiles[0];

        _manager.DevicesChanged += (_, _) => SyncDevices();
        SyncDevices();

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();
    }

    /// <summary>
    /// Плитки галереи по разделам: цвета, движение, природа.
    /// </summary>
    public IReadOnlyList<EffectTileViewModel> Tiles { get; }

    /// <summary>
    /// Эффект, который играет в выбранной группе «По умолчанию».
    /// </summary>
    public EffectTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            // Галерея сбрасывает выбор в null, когда пересобирает плитки, — играющий эффект от этого не меняется
            if (value == null || _selectedGroup == null || !SetProperty(ref _selectedTile, value))
                return;

            _selectedGroup.Content.EffectId = value.Definition.Id;
            BuildParameters();
            _contentChanged();
        }
    }

    /// <summary>
    /// Настройки выбранного эффекта.
    /// </summary>
    public ObservableCollection<EffectParameterViewModel> Parameters { get; } = new();

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

    private IReadOnlyList<SceneDeviceViewModel> SelectedDevices => Devices.Where(d => d.IsSelected).ToList();

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

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// Возвращает все настройки выбранного эффекта к значениям по умолчанию.
    /// </summary>
    [RelayCommand]
    private void ResetParameters()
    {
        foreach (var parameter in Parameters)
            parameter.Reset();

        OnParameterChanged();
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

        if (group == null)
        {
            Parameters.Clear();
            return;
        }

        group.IsSelected = true;

        // Превью плиток рисуются с настройками эффектов этой группы
        foreach (var tile in Tiles)
        {
            tile.Values = EffectValues.From(group.Content.Effects.GetValueOrDefault(tile.Definition.Id));
            tile.Advance(0);
        }

        _selectedTile = Tiles.FirstOrDefault(t => t.Definition.Id == group.Content.EffectId) ?? Tiles[0];
        OnPropertyChanged(nameof(SelectedTile));
        BuildParameters();
    }

    /// <summary>
    /// Название новой группы: «Группа N» с первым свободным номером.
    /// </summary>
    private string NextGroupName()
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
    private string NextGroupColor()
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
    /// Пересобирает настройки под выбранный эффект; правки пишутся в его сохранённые настройки.
    /// </summary>
    private void BuildParameters()
    {
        Parameters.Clear();

        if (_selectedGroup == null)
            return;

        var values = ValuesConfigOf(_selectedTile.Definition);

        foreach (var parameter in _selectedTile.Definition.Parameters)
        {
            EffectParameterViewModel item = parameter.Kind switch
            {
                EffectParameterKind.Color => new ColorParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Choice => new ChoiceParameterViewModel(parameter, values, OnParameterChanged),
                EffectParameterKind.Toggle => new ToggleParameterViewModel(parameter, values, OnParameterChanged),
                _ => new NumberParameterViewModel(parameter, values, OnParameterChanged)
            };

            Parameters.Add(item);
        }
    }

    private void OnParameterChanged()
    {
        // Превью плитки рисуется с теми же настройками, что и эффект на устройствах
        _selectedTile.Values = EffectValues.From(ValuesConfigOf(_selectedTile.Definition));

        _contentChanged();
    }

    /// <summary>
    /// Сохранённые настройки эффекта; заводятся пустыми, если эффект ещё не настраивали.
    /// </summary>
    private EffectValuesConfig ValuesConfigOf(EffectDefinition effect)
    {
        var effects = _selectedGroup!.Content.Effects;

        if (!effects.TryGetValue(effect.Id, out var values))
        {
            values = new EffectValuesConfig();
            effects[effect.Id] = values;
        }

        return values;
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
    /// Строки списка сценариев группы: её сценарии по важности и внизу «По умолчанию».
    /// Условий у сценариев пока нет, поэтому играет всегда «По умолчанию».
    /// </summary>
    private IReadOnlyList<ScenarioRowViewModel> ScenariosOf(GroupConfig group)
    {
        var rows = group.ScenarioIds
            .Select(id => _config.Scenarios.FirstOrDefault(s => s.Id == id))
            .OfType<ScenarioConfig>()
            .Select(s => new ScenarioRowViewModel(s.Name, false))
            .ToList();

        rows.Add(new ScenarioRowViewModel("По умолчанию", true));

        return rows;
    }

    /// <summary>
    /// Один кадр живой сцены: последние цвета устройств от движка и следующий кадр превью на плитках.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        foreach (var tile in Tiles)
            tile.Advance(seconds);

        var live = _loop.LiveColors;

        foreach (var device in Devices)
        {
            var colors = live is { IsLightingEnabled: true } && live.Devices.TryGetValue(device.Id, out var found) ? found : null;

            device.Show(colors?.Colors, colors?.Brightness ?? 100);
        }
    }
}

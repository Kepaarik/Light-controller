using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Apps;
using LightSpace.Engine.Scenarios;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вкладка «Сценарии»: список всех сценариев, редактор выбранного — название, условие, содержимое с живым превью,
/// группы, где он стоит, — и диагностика: что играет в каждой группе и почему. Правки сразу попадают в настройки.
/// </summary>
public sealed partial class ScenariosViewModel : ObservableObject
{
    // Живое превью обновляется не чаще 30 раз в секунду
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly AppConfig _config;
    private readonly DeviceManager _manager;
    private readonly AppWatcher _apps;
    private readonly SourceHub _sources;
    private readonly Action _contentChanged;
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _clock = new();

    private ScenarioItemViewModel? _selectedItem;
    private TimeSpan _lastTick;

    [ObservableProperty]
    private IReadOnlyList<AppOptionViewModel> _runningApps = Array.Empty<AppOptionViewModel>();

    /// <summary>
    /// Почему игры сейчас не могут присылать события, например порт занят; null — всё в порядке или события не нужны.
    /// </summary>
    [ObservableProperty]
    private string? _gamesProblem;

    /// <summary>
    /// В выборе приложения видны и фоновые процессы без окон, а не только приложения с окнами.
    /// </summary>
    [ObservableProperty]
    private bool _showsAllProcesses;

    /// <summary>
    /// contentChanged вызывается после каждой правки сценариев: их состава, названия, условия, содержимого и групп.
    /// </summary>
    public ScenariosViewModel(AppConfig config, DeviceManager manager, AppWatcher apps, SourceHub sources, Action contentChanged)
    {
        _config = config;
        _manager = manager;
        _apps = apps;
        _sources = sources;
        _contentChanged = contentChanged;

        Editor = new EffectEditorViewModel(contentChanged);
        SceneEditor = new SceneEditorViewModel(config, manager, contentChanged);
        ScreenEditor = new ScreenEditorViewModel(sources, contentChanged);
        MusicEditor = new MusicEditorViewModel(sources, contentChanged);
        GameEditor = new GameEditorViewModel(sources, contentChanged);
        BuiltinEditor = new BuiltinEditorViewModel(manager, contentChanged);

        foreach (var scenario in config.Scenarios)
            Items.Add(new ScenarioItemViewModel(scenario, contentChanged));

        _liveTimer = new DispatcherTimer { Interval = LiveInterval };
        _liveTimer.Tick += (_, _) => OnLiveTick();

        SelectedItem = Items.FirstOrDefault();
        Refresh();
    }

    /// <summary>
    /// Все сценарии в порядке создания.
    /// </summary>
    public ObservableCollection<ScenarioItemViewModel> Items { get; } = new();

    /// <summary>
    /// Сценарий, открытый в редакторе.
    /// </summary>
    public ScenarioItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedContentKind));
            OnContentKindChanged();

            Editor.Edit(value?.Settings.Content);
            SceneEditor.Edit(value?.Settings.Content);
            ScreenEditor.Edit(value?.Settings.Content);
            MusicEditor.Edit(value?.Settings.Content);
            GameEditor.Edit(value?.Settings.Content);
            BuiltinEditor.Edit(value?.Settings.Content);
            BuildGroupChips();
        }
    }

    public bool HasSelection => _selectedItem != null;

    public bool HasScenarios => Items.Count > 0;

    /// <summary>
    /// Галерея и настройки эффекта, который играет, пока сценарий активен.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Холст и слои, которые играют, пока сценарий активен, если его содержимое — сцена.
    /// </summary>
    public SceneEditorViewModel SceneEditor { get; }

    /// <summary>
    /// Монитор и настройки картинки, которые играют, пока сценарий активен, если его содержимое — экран.
    /// </summary>
    public ScreenEditorViewModel ScreenEditor { get; }

    /// <summary>
    /// Звук и настройки музыки, которые играют, пока сценарий активен, если его содержимое — музыка.
    /// </summary>
    public MusicEditorViewModel MusicEditor { get; }

    /// <summary>
    /// Игра и её настройки, которые играют, пока сценарий активен, если его содержимое — игра.
    /// </summary>
    public GameEditorViewModel GameEditor { get; }

    /// <summary>
    /// Встроенные эффекты устройств и их настройки, которые играют, пока сценарий активен, если его содержимое —
    /// встроенный эффект.
    /// </summary>
    public BuiltinEditorViewModel BuiltinEditor { get; }

    /// <summary>
    /// Режимы условия для сегментов «Когда».
    /// </summary>
    public IReadOnlyList<ConditionModeOption> Modes => ConditionModeOption.All;

    /// <summary>
    /// Типы содержимого для сегментов «Что»: эффект, сцена, экран, музыка, игра и встроенный эффект.
    /// </summary>
    public IReadOnlyList<ContentKindOption> ContentKinds => ContentKindOption.All;

    /// <summary>
    /// Выбранный тип содержимого: эффект, сцена, экран, музыка, игра или встроенный эффект. Сцена без слоёв получает
    /// первый слой во всю сцену с эффектом, который играл до этого.
    /// </summary>
    public ContentKindOption SelectedContentKind
    {
        get => _selectedItem?.Settings.Content.Kind switch
        {
            ContentKind.Scene => ContentKindOption.Scene,
            ContentKind.Screen => ContentKindOption.Screen,
            ContentKind.Music => ContentKindOption.Music,
            ContentKind.Game => ContentKindOption.Game,
            ContentKind.Builtin => ContentKindOption.Builtin,
            _ => ContentKindOption.Effect
        };
        set
        {
            var content = _selectedItem?.Settings.Content;

            var kind = value == ContentKindOption.Scene ? ContentKind.Scene
                : value == ContentKindOption.Screen ? ContentKind.Screen
                : value == ContentKindOption.Music ? ContentKind.Music
                : value == ContentKindOption.Game ? ContentKind.Game
                : value == ContentKindOption.Builtin ? ContentKind.Builtin
                : ContentKind.Effect;

            // Недоступные типы выбрать нельзя; сегменты сбрасывают выбор в null, когда пересобираются
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

    /// <summary>
    /// Содержимое открытого сценария — эффект из галереи.
    /// </summary>
    public bool IsEffectContent => _selectedItem?.Settings.Content.Kind == ContentKind.Effect;

    /// <summary>
    /// Содержимое открытого сценария — сцена со слоями.
    /// </summary>
    public bool IsSceneContent => _selectedItem?.Settings.Content.Kind == ContentKind.Scene;

    /// <summary>
    /// Содержимое открытого сценария — экран.
    /// </summary>
    public bool IsScreenContent => _selectedItem?.Settings.Content.Kind == ContentKind.Screen;

    /// <summary>
    /// Содержимое открытого сценария — музыка.
    /// </summary>
    public bool IsMusicContent => _selectedItem?.Settings.Content.Kind == ContentKind.Music;

    /// <summary>
    /// Содержимое открытого сценария — игра.
    /// </summary>
    public bool IsGameContent => _selectedItem?.Settings.Content.Kind == ContentKind.Game;

    /// <summary>
    /// Содержимое открытого сценария — встроенный эффект устройств.
    /// </summary>
    public bool IsBuiltinContent => _selectedItem?.Settings.Content.Kind == ContentKind.Builtin;

    /// <summary>
    /// Группы для чипов «Группы»: отмечены те, где стоит открытый сценарий.
    /// </summary>
    public ObservableCollection<ScenarioGroupChipViewModel> GroupChips { get; } = new();

    /// <summary>
    /// Диагностика: что сейчас играет в каждой группе и почему.
    /// </summary>
    public ObservableCollection<GroupPlayingViewModel> Playing { get; } = new();

    /// <summary>
    /// Включает живое превью, пока вкладку видно, и выключает, когда окно скрыто или открыта другая вкладка.
    /// </summary>
    public void SetLive(bool isLive)
    {
        if (isLive == _liveTimer.IsEnabled)
            return;

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

        // Пока вкладку не видели, устройства могли переставить на сцене
        SceneEditor.RefreshDevices();

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _liveTimer.Start();
    }

    /// <summary>
    /// Создаёт сценарий и открывает его; если указана группа — сценарий сразу встаёт в ней над «По умолчанию».
    /// </summary>
    public void Create(GroupConfig? group)
    {
        var scenario = new ScenarioConfig { Id = GroupConfig.NewId(), Name = NextName() };

        _config.Scenarios.Add(scenario);
        group?.ScenarioIds.Add(scenario.Id);

        var item = new ScenarioItemViewModel(scenario, _contentChanged);

        Items.Add(item);
        OnPropertyChanged(nameof(HasScenarios));

        SelectedItem = item;
        _contentChanged();
    }

    /// <summary>
    /// Открывает сценарий в редакторе.
    /// </summary>
    public void Open(string scenarioId)
    {
        SelectedItem = Items.FirstOrDefault(i => i.Id == scenarioId) ?? SelectedItem;
    }

    /// <summary>
    /// Показывает сценарии, которые появились в настройках не на этой вкладке, например после импорта.
    /// </summary>
    public void AddMissing()
    {
        foreach (var scenario in _config.Scenarios.Where(s => Items.All(i => i.Settings != s)))
            Items.Add(new ScenarioItemViewModel(scenario, _contentChanged));

        OnPropertyChanged(nameof(HasScenarios));
        SelectedItem ??= Items.FirstOrDefault();
    }

    /// <summary>
    /// Обновляет то, что зависит от групп и запущенных приложений: где играет каждый сценарий, чипы групп
    /// и диагностику.
    /// </summary>
    public void Refresh()
    {
        var choices = _config.Groups.Select(g => (Group: g, Choice: ScenarioSelector.Choose(_config, g, _apps))).ToList();

        foreach (var item in Items)
        {
            var playing = choices.Where(c => c.Choice.Scenario == item.Settings).Select(c => TitleOf(c.Group)).ToList();
            var groupCount = _config.Groups.Count(g => g.ScenarioIds.Contains(item.Id));

            item.SetPlaying(playing, groupCount);
        }

        // Состав групп мог измениться на сцене — тогда чипы собираются заново
        if (!GroupChips.Select(c => c.Group).SequenceEqual(_config.Groups) ||
            GroupChips.Any(c => c.Title != TitleOf(c.Group)))
            BuildGroupChips();

        Playing.Clear();

        foreach (var (group, choice) in choices)
        {
            var title = choice.Scenario == null ? "По умолчанию" : ScenarioItemViewModel.TitleOf(choice.Scenario);

            Playing.Add(new GroupPlayingViewModel(TitleOf(group), title, choice.Reason));
        }
    }

    [RelayCommand]
    private void CreateScenario()
    {
        Create(null);
    }

    /// <summary>
    /// Удаляет открытый сценарий после подтверждения.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var item = _selectedItem;

        if (item == null)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Удалить сценарий «{item.Title}»?",
            "Сценарий пропадёт из всех групп, где стоит. В них будет играть следующий по важности сценарий или «По умолчанию».",
            "Удалить", isDanger: true);

        if (!confirmed)
            return;

        _config.RemoveScenario(item.Settings);

        var index = Items.IndexOf(item);

        Items.Remove(item);
        OnPropertyChanged(nameof(HasScenarios));

        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        _contentChanged();
    }

    /// <summary>
    /// Перечитывает запущенные приложения, когда открывают выбор приложения.
    /// </summary>
    [RelayCommand]
    private void RefreshRunningApps()
    {
        RunningApps = Services.RunningApps.List(ShowsAllProcesses);
    }

    partial void OnShowsAllProcessesChanged(bool value)
    {
        RefreshRunningApps();
    }

    /// <summary>
    /// Выбор exe-файла приложения, которое сейчас не запущено.
    /// </summary>
    [RelayCommand]
    private void BrowseApp()
    {
        if (_selectedItem == null)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Приложение для сценария",
            Filter = "Приложения (*.exe)|*.exe"
        };

        if (dialog.ShowDialog() == true)
            _selectedItem.App = AppOptionViewModel.From(dialog.FileName);
    }

    /// <summary>
    /// Чипы групп для открытого сценария.
    /// </summary>
    private void BuildGroupChips()
    {
        GroupChips.Clear();

        if (_selectedItem == null)
            return;

        foreach (var group in _config.Groups)
            GroupChips.Add(new ScenarioGroupChipViewModel(group, TitleOf(group), group.ScenarioIds.Contains(_selectedItem.Id), OnChipToggled));
    }

    /// <summary>
    /// Чип отметили — сценарий встаёт в группе над «По умолчанию», то есть ниже всех её сценариев; сняли — уходит из группы.
    /// </summary>
    private void OnChipToggled(ScenarioGroupChipViewModel chip)
    {
        if (_selectedItem == null)
            return;

        var ids = chip.Group.ScenarioIds;

        if (chip.IsChecked && !ids.Contains(_selectedItem.Id))
            ids.Add(_selectedItem.Id);
        else if (!chip.IsChecked)
            ids.Remove(_selectedItem.Id);

        _contentChanged();
    }

    /// <summary>
    /// Как группа подписана в чипах и диагностике: у группы-одиночки — названием её устройства.
    /// </summary>
    private string TitleOf(GroupConfig group)
    {
        if (group.IsSolo)
            return _manager.Devices.FirstOrDefault(d => group.DeviceIds.Contains(d.Id))?.Name ?? "Устройство";

        return string.IsNullOrWhiteSpace(group.Name) ? "Без названия" : group.Name;
    }

    /// <summary>
    /// Название нового сценария: «Сценарий N» с первым свободным номером.
    /// </summary>
    private string NextName()
    {
        var names = _config.Scenarios.Select(s => s.Name).ToHashSet();
        var number = 1;

        while (names.Contains($"Сценарий {number}"))
            number++;

        return $"Сценарий {number}";
    }

    /// <summary>
    /// Один кадр живого превью: плитки галереи и крупное превью открытого сценария.
    /// </summary>
    private void OnLiveTick()
    {
        var now = _clock.Elapsed;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Занятый порт игр видно в диагностике, даже когда редактор игры не открыт
        GamesProblem = _sources.GamesProblem;

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
    }

    /// <summary>
    /// Сменился тип содержимого открытого сценария или сам сценарий: показываются редактор и превью этого типа,
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
}

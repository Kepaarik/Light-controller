using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.Core.Config;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Игра в сегментах редактора игры: Dota 2, Counter-Strike 2 или другие игры.
/// </summary>
public sealed record GameOption(GameKind Game, string Title)
{
    public static readonly IReadOnlyList<GameOption> All = new GameOption[]
    {
        new(GameKind.Dota2, "Dota 2"),
        new(GameKind.CounterStrike2, "Counter-Strike 2"),
        new(GameKind.Other, "Другие игры")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Фон игры в сегментах: картинка экрана, цвет команды или без фона.
/// </summary>
public sealed record GameBackgroundOption(GameBackground Background, string Title)
{
    public static readonly IReadOnlyList<GameBackgroundOption> All = new GameBackgroundOption[]
    {
        new(GameBackground.Screen, "Картинка экрана"),
        new(GameBackground.Team, "Цвет команды"),
        new(GameBackground.Off, "Без фона")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Переключатель события игры: включённое свет показывает вспышкой или своим цветом, выключенное — нет.
/// </summary>
public sealed class GameEventViewModel : ObservableObject
{
    private readonly GameConfig _game;
    private readonly string _key;
    private readonly Action _changed;

    /// <summary>
    /// changed вызывается, когда событие включают или выключают.
    /// </summary>
    public GameEventViewModel(GameConfig game, GameEvent gameEvent, Action changed)
    {
        _game = game;
        _key = GameConfig.EventKey(game.Game, gameEvent.Key);
        _changed = changed;

        Title = gameEvent.Title;
        Description = gameEvent.Description;
    }

    public string Title { get; }

    public string Description { get; }

    public bool IsEnabled
    {
        get => !_game.DisabledEvents.Contains(_key);
        set
        {
            if (value == IsEnabled)
                return;

            if (value)
                _game.DisabledEvents.Remove(_key);
            else
                _game.DisabledEvents.Add(_key);

            OnPropertyChanged();
            _changed();
        }
    }
}

/// <summary>
/// Редактор содержимого-игры: игра, подключение игры к Light Space, фон, где показывать полоски, события и живое
/// превью тем же рендером, что и кадры устройств. Все правки сразу попадают в содержимое.
/// </summary>
public sealed partial class GameEditorViewModel : ObservableObject
{
    private const int PreviewColumns = 32;
    private const int PreviewRows = 12;

    // Под этим именем превью помнит свои сглаженные цвета экрана
    private const string PreviewKey = "preview";

    private static readonly DeviceGeometry PreviewSpace = DeviceGeometry.Grid(PreviewColumns, PreviewRows);

    private readonly SourceHub _sources;
    private readonly Action _changed;
    private readonly ColorBitmap _bitmap = new(PreviewColumns, PreviewRows);

    private GameConfig? _game;
    private GamePlayback _playback = new();
    private IReadOnlyList<MonitorOption> _monitors = Array.Empty<MonitorOption>();
    private IReadOnlyList<GameEventViewModel> _events = Array.Empty<GameEventViewModel>();

    // Время показа примера, пока игра молчит: по нему меняются придуманные здоровье и мана
    private double _demoTime;

    /// <summary>
    /// Игра сейчас не в матче, и превью показывает пример с придуманными здоровьем и маной или бронёй.
    /// Пример только в превью: устройства по-прежнему ждут начала матча.
    /// </summary>
    [ObservableProperty]
    private bool _isDemo;

    /// <summary>
    /// Подключена ли выбранная игра: лежит ли в её папке файл, по которому она шлёт события.
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// Что с подключением выбранной игры: подключена, не найдена или почему не удалось подключить.
    /// </summary>
    [ObservableProperty]
    private string _connectionText = "";

    /// <summary>
    /// Почему игры сейчас не могут присылать события, например порт занят; null — всё в порядке.
    /// </summary>
    [ObservableProperty]
    private string? _problem;

    /// <summary>
    /// sources — откуда превью берёт состояние игры и картинку монитора; changed вызывается после каждой правки.
    /// </summary>
    public GameEditorViewModel(SourceHub sources, Action changed)
    {
        _sources = sources;
        _changed = changed;

        RefreshMonitors();
    }

    /// <summary>
    /// Кисть с текущим кадром превью.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    public IReadOnlyList<GameOption> Games => GameOption.All;

    public IReadOnlyList<GameBackgroundOption> Backgrounds => GameBackgroundOption.All;

    public GameOption? SelectedGame
    {
        get => _game == null ? null : Games.First(o => o.Game == _game.Game);
        set
        {
            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (value == null || _game == null || value.Game == _game.Game)
                return;

            _game.Game = value.Game;

            OnPropertyChanged();
            OnGameChanged();
            _changed();
        }
    }

    /// <summary>
    /// У выбранной игры есть своя интеграция: полоски, события и подключение. У других игр — только картинка монитора.
    /// </summary>
    public bool HasIntegration => _game != null && _game.Game != GameKind.Other;

    /// <summary>
    /// Нужен выбор монитора: другая игра светится его картинкой, игра с интеграцией — фоном «Картинка экрана».
    /// </summary>
    public bool ShowsMonitor => _game != null && (_game.Game == GameKind.Other || _game.Background == GameBackground.Screen);

    /// <summary>
    /// Что показывает свет в выбранной игре.
    /// </summary>
    public string GameHint => _game?.Game switch
    {
        GameKind.Dota2 => "Здоровье героя — зелёный → красный, мана — синий. Выбор героя — мигание, оглушение — белое мерцание, " +
                          "урон — красная вспышка, смерть — тусклый красный, победа — радуга.",
        GameKind.CounterStrike2 => "Здоровье — зелёный → красный, броня — голубой. Ослепление — белый, горение — оранжевое мерцание, " +
                                   "урон — красная вспышка, заложенная бомба пищит красным всё чаще, победа в раунде — радуга.",
        _ => "Игры без своей интеграции: каждый светодиод берёт цвет картинки монитора под своим местом на сцене."
    };

    /// <summary>
    /// Что ещё нужно сделать в самой игре; пусто — ничего.
    /// </summary>
    public string GameNote => _game?.Game == GameKind.Dota2
        ? "В Steam добавьте Dota 2 параметр запуска -gamestateintegration (Свойства → Параметры запуска), иначе игра не присылает события."
        : "";

    public GameBackgroundOption? SelectedBackground
    {
        get => _game == null ? null : Backgrounds.First(o => o.Background == _game.Background);
        set
        {
            if (value == null || _game == null || value.Background == _game.Background)
                return;

            _game.Background = value.Background;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsMonitor));
            _changed();
        }
    }

    public double BackgroundBrightness
    {
        get => _game?.BackgroundBrightness ?? GameConfig.DefaultBackgroundBrightness;
        set
        {
            var number = (int)Math.Round(value);

            if (_game == null || number == _game.BackgroundBrightness)
                return;

            _game.BackgroundBrightness = number;
            OnPropertyChanged();
            _changed();
        }
    }

    public bool KeyboardBars
    {
        get => _game?.KeyboardBars ?? true;
        set => SetFlag(value, g => g.KeyboardBars, (g, v) => g.KeyboardBars = v);
    }

    public bool StripBars
    {
        get => _game?.StripBars ?? true;
        set => SetFlag(value, g => g.StripBars, (g, v) => g.StripBars = v);
    }

    /// <summary>
    /// Мониторы для выбора: основной монитор Windows и все подключённые.
    /// </summary>
    public IReadOnlyList<MonitorOption> Monitors
    {
        get => _monitors;
        private set => SetProperty(ref _monitors, value);
    }

    /// <summary>
    /// Монитор, чья картинка светится в другой игре или фоном.
    /// </summary>
    public MonitorOption? SelectedMonitor
    {
        get => _game == null ? null : _monitors.FirstOrDefault(m => m.Key == _game.MonitorKey);
        set
        {
            // Выпадающая панель сбрасывает выбор в null, когда пересобирает список, — монитор от этого не меняется
            if (value == null || _game == null || value.Key == _game.MonitorKey)
                return;

            _game.MonitorKey = value.Key;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// События выбранной игры, которые можно выключить.
    /// </summary>
    public IReadOnlyList<GameEventViewModel> Events
    {
        get => _events;
        private set => SetProperty(ref _events, value);
    }

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _game = content?.Game;

        RefreshMonitors();
        OnPropertyChanged(nameof(SelectedGame));
        OnPropertyChanged(nameof(SelectedBackground));
        OnPropertyChanged(nameof(ShowsMonitor));
        OnPropertyChanged(nameof(BackgroundBrightness));
        OnPropertyChanged(nameof(KeyboardBars));
        OnPropertyChanged(nameof(StripBars));
        OnGameChanged();
    }

    /// <summary>
    /// Рисует следующий кадр превью: прошло seconds секунд. Пока превью рисуется, события игр принимаются,
    /// а монитор снимается, если он нужен.
    /// </summary>
    public void Advance(double seconds)
    {
        if (_game == null)
        {
            StopPreview();
            return;
        }

        var settings = GameSettings.From(_game);
        var monitors = settings.NeedsScreen ? new HashSet<string> { settings.Screen.MonitorKey } : new HashSet<string>();

        _sources.Require(this, new SourceDemand(monitors, new HashSet<AudioInput>(), settings.NeedsGameState));
        Problem = settings.NeedsGameState ? _sources.GamesProblem : null;

        var state = _sources.Games;

        IsDemo = settings.NeedsGameState && !GamePlayback.IsInMatch(settings.Game, state);

        if (IsDemo)
        {
            _demoTime += seconds;
            state = DemoState(_demoTime);
        }

        _playback.Advance(settings, state, _sources.PictureOf(settings.Screen.MonitorKey), seconds);
        _bitmap.Write(_playback.Render(PreviewKey, settings, PreviewSpace, PreviewSpace.Points, PreviewSpace.Width, PreviewSpace.Height));
    }

    /// <summary>
    /// Превью больше не видно: его источники больше не нужны.
    /// </summary>
    public void StopPreview()
    {
        _sources.Require(this, SourceDemand.None);
    }

    /// <summary>
    /// Кладёт в папку выбранной игры файл, по которому она шлёт события Light Space.
    /// </summary>
    [RelayCommand]
    private void Connect()
    {
        if (_game == null || !HasIntegration)
            return;

        try
        {
            GsiInstaller.Install(_game.Game);

            IsConnected = true;
            ConnectionText = "Подключена. Если игра уже запущена, перезапустите её.";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            IsConnected = false;
            ConnectionText = $"Не удалось подключить: {e.Message}";
        }
    }

    /// <summary>
    /// Перечитывает подключённые мониторы, когда открывают выбор монитора.
    /// </summary>
    [RelayCommand]
    private void RefreshMonitors()
    {
        var options = new List<MonitorOption> { new("", "Основной монитор Windows") };

        foreach (var monitor in Engine.Sources.Monitors.List())
        {
            var title = $"Монитор {monitor.Number} · {monitor.Width}×{monitor.Height}";

            options.Add(new MonitorOption(monitor.Key, monitor.IsPrimary ? title + " · основной" : title));
        }

        // Выбранный монитор сейчас отключён: он остаётся в списке, а картинка пока берётся с основного
        if (_game != null && options.All(o => o.Key != _game.MonitorKey))
            options.Add(new MonitorOption(_game.MonitorKey, "Отключённый монитор"));

        Monitors = options;
        OnPropertyChanged(nameof(SelectedMonitor));
    }

    /// <summary>
    /// Сменилась игра или открыто другое содержимое: свои события, подсказки и подключение, превью начинается заново.
    /// </summary>
    private void OnGameChanged()
    {
        _playback = new GamePlayback();

        Events = _game == null
            ? Array.Empty<GameEventViewModel>()
            : GameEvents.Of(_game.Game).Select(e => new GameEventViewModel(_game, e, _changed)).ToList();

        OnPropertyChanged(nameof(HasIntegration));
        OnPropertyChanged(nameof(ShowsMonitor));
        OnPropertyChanged(nameof(GameHint));
        OnPropertyChanged(nameof(GameNote));
        RefreshConnection();
    }

    /// <summary>
    /// Придуманный матч для превью: здоровье ступеньками падает со 100 до 16 % и восстанавливается, мана и броня
    /// плавно меняются. time — сколько секунд идёт пример.
    /// </summary>
    private static GameState DemoState(double time)
    {
        var step = (int)(time % 8);
        var health = step < 7 ? 100 - step * 14 : 100;
        var other = (int)Math.Round(55 + 40 * Math.Sin(time * 0.9));
        var now = DateTime.UtcNow;

        var dota = new DotaState(DotaState.InProgress, true, "", "radiant", true, true, health, other, 0,
            false, false, false, false, now);
        var cs = new CsState(true, "CT", health, other, 0, 0, "live", "live", "", "", now);

        return new GameState(dota, cs);
    }

    /// <summary>
    /// Проверяет, подключена ли выбранная игра.
    /// </summary>
    private void RefreshConnection()
    {
        if (_game == null || !HasIntegration)
        {
            IsConnected = false;
            ConnectionText = "";
            return;
        }

        IsConnected = GsiInstaller.IsInstalled(_game.Game);

        ConnectionText = IsConnected ? "Подключена: игра шлёт события Light Space."
            : GsiInstaller.IsGameFound(_game.Game) ? "Не подключена: нажмите «Подключить игру»."
            : "Не найдена в библиотеках Steam.";
    }

    private void SetFlag(bool value, Func<GameConfig, bool> get, Action<GameConfig, bool> set, [CallerMemberName] string? name = null)
    {
        if (_game == null || value == get(_game))
            return;

        set(_game, value);
        OnPropertyChanged(name);
        _changed();
    }
}

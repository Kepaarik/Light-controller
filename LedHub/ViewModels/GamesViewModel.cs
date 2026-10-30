using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Core;
using LedHub.Services;
using LedHub.Services.Effects;
using LedHub.Services.Games;

namespace LedHub.ViewModels;

/// <summary>
/// Игровые режимы: следит, какая игра запущена на главном мониторе, и сам включает подходящую подсветку.
/// </summary>
public sealed partial class GamesViewModel : ObservableObject
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly GamesConfig _config;
    private readonly Action _save;
    private readonly Func<bool, bool, IReadOnlyDictionary<LedDevice, MonitorBinding?>> _targets;
    private readonly Action<IReadOnlyCollection<LedDevice>> _stopConflicts;
    private readonly EffectPickerViewModel _effects;
    private readonly Func<SceneConfig, Task> _applyPreset;
    private readonly GsiServer _server = new();
    private readonly EffectRunner _runner = new();
    private readonly DispatcherTimer _timer;

    private DetectedGame? _game;
    private bool _isChecking;

    // ВРЕМЕННО: диагностика поиска игры — сколько раз прошла проверка и сколько процессов dota2 она увидела
    private int _checkCount;
    private int _dotaProcessCount;

    // Подсветку этой игры остановили вручную или заняли её ленты другим — сами не включаем до следующей игры
    private bool _isSuspended;

    // Пресет этой игры уже включён — повторно не включаем до следующей игры
    private bool _isPresetApplied;

    [ObservableProperty]
    private string _status = "Игра не запущена";

    /// <summary>
    /// Название запущенной игры; null — игры нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGame))]
    private string? _gameTitle;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _brightness = 100;

    /// <summary>
    /// Показаны ли дополнительные настройки: мониторы, слои и события.
    /// </summary>
    [ObservableProperty]
    private bool _showAdvanced;

    /// <summary>
    /// Почему игры не могут присылать события; null — всё в порядке.
    /// </summary>
    [ObservableProperty]
    private string? _serverError;

    /// <summary>
    /// targets — ленты для игры: лента главного монитора с его привязкой, а при «всех лентах» и «клавиатурах» —
    /// и остальные ленты или клавиатуры без привязки.
    /// stopConflicts — останавливает то, что уже играет на этих лентах.
    /// scenes — пресеты, которые можно включать при запуске игры, applyPreset — включает пресет.
    /// </summary>
    public GamesViewModel(GamesConfig config, Action save, Func<bool, bool, IReadOnlyDictionary<LedDevice, MonitorBinding?>> targets,
        Action<IReadOnlyCollection<LedDevice>> stopConflicts, EffectPickerViewModel effects,
        ObservableCollection<SceneConfig> scenes, Func<SceneConfig, Task> applyPreset)
    {
        _config = config;
        _save = save;
        _targets = targets;
        _stopConflicts = stopConflicts;
        _effects = effects;
        _applyPreset = applyPreset;

        ScreenEffects = EffectLibrary.All.Where(e => e.Category == EffectCategory.Screen).ToList();
        DotaTriggers = GameTriggers.Dota.Select(t => new GameTriggerViewModel(GameKind.Dota2, t, config, save)).ToList();
        CsTriggers = GameTriggers.CounterStrike.Select(t => new GameTriggerViewModel(GameKind.CounterStrike2, t, config, save)).ToList();

        Profiles = new[]
        {
            new GameProfileViewModel(GameKind.Dota2, "Dota 2", "Здоровье и мана героя, урон, смерть и победа",
                "Игровая подсветка",
                "В Steam добавьте Dota 2 параметр запуска -gamestateintegration (Свойства → Параметры запуска), иначе игра не присылает события.",
                config.Dota, scenes, save, OnProfileChanged),
            new GameProfileViewModel(GameKind.CounterStrike2, "Counter-Strike 2", "Здоровье и броня, ослепление, бомба и конец раунда",
                "Игровая подсветка", null, config.CounterStrike, scenes, save, OnProfileChanged),
            new GameProfileViewModel(GameKind.Other, "Другие игры", "Любая программа на весь главный монитор",
                "Подсветка по экрану", null, config.OtherGames, scenes, save, OnProfileChanged)
        };

        ServerError = _server.Start();

        // Файл настроек пишем при каждом запуске: так игра всегда знает, куда слать события
        foreach (var profile in Profiles.Where(p => p.IsEnabled && p.NeedsConnection))
            Connect(profile.Kind);

        RefreshConnections();

        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    /// <summary>
    /// Режимы по картинке экрана для лент вдали от монитора в играх без своей интеграции.
    /// </summary>
    public IReadOnlyList<EffectDefinition> ScreenEffects { get; }

    /// <summary>
    /// Игры, которые LedHub узнаёт, и что включать при запуске каждой.
    /// </summary>
    public IReadOnlyList<GameProfileViewModel> Profiles { get; }

    /// <summary>
    /// События Dota 2, которые можно выключить.
    /// </summary>
    public IReadOnlyList<GameTriggerViewModel> DotaTriggers { get; }

    /// <summary>
    /// События Counter-Strike 2, которые можно выключить.
    /// </summary>
    public IReadOnlyList<GameTriggerViewModel> CsTriggers { get; }

    public IReadOnlyList<GameChoice<GameBackground>> BackgroundOptions { get; } = new[]
    {
        new GameChoice<GameBackground>(GameBackground.Screen, "Картинка экрана"),
        new GameChoice<GameBackground>(GameBackground.Team, "Цвет команды"),
        new GameChoice<GameBackground>(GameBackground.Off, "Без фона")
    };

    public IReadOnlyList<GameChoice<KeyboardHud>> KeyboardHudOptions { get; } = new[]
    {
        new GameChoice<KeyboardHud>(KeyboardHud.WholeKeyboard, "Вся клавиатура: здоровье сверху, мана или броня снизу"),
        new GameChoice<KeyboardHud>(KeyboardHud.Off, "Без полосок")
    };

    public bool HasGame => GameTitle != null;

    public bool UseAllDevices
    {
        get => _config.UseAllDevices;
        set
        {
            if (_config.UseAllDevices == value)
                return;

            _config.UseAllDevices = value;
            _save();
            OnPropertyChanged();
            Restart();
        }
    }

    public bool UseKeyboards
    {
        get => _config.UseKeyboards;
        set
        {
            if (_config.UseKeyboards == value)
                return;

            _config.UseKeyboards = value;
            _save();
            OnPropertyChanged();
            Restart();
        }
    }

    public GameBackground Background
    {
        get => _config.Background;
        set
        {
            if (_config.Background == value)
                return;

            _config.Background = value;
            _save();
            OnPropertyChanged();

            // Картинку экрана для фона снимают, только когда она нужна, — запускаем подсветку заново
            Restart();
        }
    }

    /// <summary>
    /// Яркость фона в процентах; подсветка подхватывает её на ходу.
    /// </summary>
    public int BackgroundBrightness
    {
        get => _config.BackgroundBrightness;
        set
        {
            if (_config.BackgroundBrightness == value)
                return;

            _config.BackgroundBrightness = value;
            _save();
            OnPropertyChanged();
        }
    }

    public KeyboardHud KeyboardHud
    {
        // Прежние варианты с полосками на рядах теперь тоже на всю клавиатуру
        get => _config.KeyboardHud == KeyboardHud.Off ? KeyboardHud.Off : KeyboardHud.WholeKeyboard;
        set
        {
            if (_config.KeyboardHud == value)
                return;

            _config.KeyboardHud = value;
            _save();
            OnPropertyChanged();
        }
    }

    public bool BarsOnOtherDevices
    {
        get => _config.BarsOnOtherDevices;
        set
        {
            if (_config.BarsOnOtherDevices == value)
                return;

            _config.BarsOnOtherDevices = value;
            _save();
            OnPropertyChanged();
        }
    }

    public EffectDefinition? SelectedScreenEffect
    {
        get => ScreenEffects.FirstOrDefault(e => e.Id == _config.OtherGamesEffect) ?? ScreenEffects.FirstOrDefault();
        set
        {
            if (value == null || _config.OtherGamesEffect == value.Id)
                return;

            _config.OtherGamesEffect = value.Id;
            _save();
            OnPropertyChanged();

            if (_game?.Kind == GameKind.Other || Background == GameBackground.Screen)
                Restart();
        }
    }

    public void StopIfUsing(IReadOnlyCollection<LedDevice> devices)
    {
        if (!IsRunning || !_runner.Devices.Any(d => devices.Contains(d)))
            return;

        StopLighting();
        _isSuspended = true;
        Status = $"{_game?.Title}: подсветка игры уступила место другому режиму";
    }

    public void Shutdown()
    {
        _timer.Stop();
        _runner.Stop();
        _server.Dispose();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        _isSuspended = false;
        _isPresetApplied = false;

        // Ищем игру сразу, не дожидаясь очередной проверки по таймеру
        await CheckAsync();

        // Кнопка включает подсветку и для игры, которую в списке выключили
        if (_game == null || IsRunning || _isPresetApplied)
            return;

        StartLighting(_game);
    }

    [RelayCommand]
    private void Stop()
    {
        StopLighting();
        _isSuspended = _game != null;
        Status = _game != null ? $"{_game.Title}: подсветка игры остановлена" : "Подсветка игры остановлена";
    }

    private void OnProfileChanged(GameProfileViewModel profile)
    {
        if (profile.IsEnabled && profile.NeedsConnection)
        {
            Connect(profile.Kind);
            RefreshConnections();
        }

        if (_game?.Kind != profile.Kind)
            return;

        // Запущенная игра сразу переходит на новый выбор
        StopLighting();
        _isSuspended = false;
        _isPresetApplied = false;
        _ = CheckAsync();
    }

    /// <summary>
    /// Кладёт игре файл настроек; если Steam или игра не найдены, игра просто остаётся неподключённой.
    /// </summary>
    private void Connect(GameKind kind)
    {
        if (ServerError != null)
            return;

        try
        {
            GsiInstaller.Install(kind, _server.Address);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void RefreshConnections()
    {
        foreach (var profile in Profiles)
            profile.IsConnected = profile.NeedsConnection && GsiInstaller.IsInstalled(profile.Kind);
    }

    private GameProfileViewModel ProfileFor(GameKind kind)
    {
        return Profiles.First(p => p.Kind == kind);
    }

    private async Task CheckAsync()
    {
        if (_isChecking)
            return;

        _isChecking = true;

        try
        {
            // Перебор процессов и окон заметно тормозит, поэтому не на потоке окна
            var current = _game;
            var game = await Task.Run(() =>
            {
                // ВРЕМЕННО: диагностика поиска игры
                var dotaProcesses = System.Diagnostics.Process.GetProcessesByName("dota2");
                _dotaProcessCount = dotaProcesses.Length;

                foreach (var process in dotaProcesses)
                    process.Dispose();

                var found = GameWatcher.Detect(MainMonitor.Resolve());

                // Из полноэкранной игры ненадолго вышли на рабочий стол — подсветку не бросаем, пока игра запущена
                if (found == null && current is { Kind: GameKind.Other } && GameWatcher.IsRunning(current.ProcessId))
                    return current;

                return found;
            });

            if (game?.Kind != _game?.Kind || game?.ProcessId != _game?.ProcessId)
            {
                StopLighting();
                _isSuspended = false;
                _isPresetApplied = false;
                _game = game;
            }

            GameTitle = _game?.Title;
            _checkCount++;

            if (_game == null)
            {
                // ВРЕМЕННО: диагностика поиска игры в конце строки
                Status = $"Игра не запущена — подсветка включится, как только она начнётся "
                         + $"[проверка №{_checkCount} в {DateTime.Now:HH:mm:ss}, процессов dota2: {_dotaProcessCount}]";
                return;
            }

            if (IsRunning || _isSuspended || _isPresetApplied)
                return;

            var profile = ProfileFor(_game.Kind);

            if (!profile.IsEnabled)
            {
                Status = $"{_game.Title}: игра запущена, подсветка для неё выключена в списке «Мои игры»";
                return;
            }

            if (profile.Scene is { } scene)
            {
                await ApplyPresetAsync(_game, scene);
                return;
            }

            StartLighting(_game);
        }
        finally
        {
            _isChecking = false;
        }
    }

    private async Task ApplyPresetAsync(DetectedGame game, SceneConfig scene)
    {
        _isPresetApplied = true;

        await _applyPreset(scene);

        Status = $"{game.Title}: включён пресет «{scene.Name}»";
    }

    private void Restart()
    {
        if (IsRunning && _game != null)
            StartLighting(_game);
    }

    private void StartLighting(DetectedGame game)
    {
        var targets = _targets(UseAllDevices, UseKeyboards);

        if (targets.Count == 0)
        {
            Status = $"{game.Title}: главному монитору не выбрана лента — выберите её в разделе «Мониторы» или включите «Все ленты»";
            return;
        }

        // Сначала гасим себя, чтобы общая остановка конфликтов не приняла наш же запуск за чужой режим
        _runner.Stop();
        _stopConflicts(targets.Keys.ToList());

        var effect = SelectedScreenEffect;

        try
        {
            // Картинка экрана нужна прочим играм целиком, а играм с интеграцией — как фон
            var needsScreen = game.Kind == GameKind.Other || Background == GameBackground.Screen;
            var screenEffect = needsScreen && effect != null
                ? new PatternSource(effect, _effects.ValuesFor(effect), new StripPointMap())
                : null;

            var source = new GameSource(game.Kind, _server, device => targets.TryGetValue(device, out var binding) ? binding : null,
                screenEffect, _config);
            var speed = effect != null ? _effects.SpeedOf(effect) : 1;

            _runner.Start(source, targets.Keys.ToList(), () => new EffectSettings(RgbColor.White, Brightness, speed));
        }
        catch (Exception e)
        {
            IsRunning = false;
            Status = $"{game.Title}: не удалось запустить подсветку — {e.Message}";
            return;
        }

        IsRunning = true;
        Status = game.Kind switch
        {
            GameKind.Dota2 when !ProfileFor(GameKind.Dota2).IsConnected => "Dota 2: игра не найдена в библиотеках Steam, события не придут",
            GameKind.Dota2 => "Dota 2: здоровье и мана героя на подсветке",
            GameKind.CounterStrike2 when !ProfileFor(GameKind.CounterStrike2).IsConnected => "Counter-Strike 2: игра не найдена в библиотеках Steam, события не придут",
            GameKind.CounterStrike2 => "Counter-Strike 2: здоровье, броня и события раунда на подсветке",
            _ => $"{game.Title}: подсветка по картинке экрана"
        };
    }

    private void StopLighting()
    {
        _runner.Stop();
        IsRunning = false;
    }
}

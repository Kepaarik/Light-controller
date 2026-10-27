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
    private readonly Func<bool, IReadOnlyDictionary<LedDevice, MonitorBinding?>> _targets;
    private readonly Action<IReadOnlyCollection<LedDevice>> _stopConflicts;
    private readonly EffectPickerViewModel _effects;
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

    [ObservableProperty]
    private bool _isDotaConnected;

    [ObservableProperty]
    private bool _isCsConnected;

    /// <summary>
    /// Почему игры не могут присылать события; null — всё в порядке.
    /// </summary>
    [ObservableProperty]
    private string? _serverError;

    /// <summary>
    /// targets — ленты для игры: лента главного монитора с его привязкой, а при «всех лентах» — и остальные без привязки.
    /// stopConflicts — останавливает то, что уже играет на этих лентах.
    /// </summary>
    public GamesViewModel(GamesConfig config, Action save, Func<bool, IReadOnlyDictionary<LedDevice, MonitorBinding?>> targets,
        Action<IReadOnlyCollection<LedDevice>> stopConflicts, EffectPickerViewModel effects)
    {
        _config = config;
        _save = save;
        _targets = targets;
        _stopConflicts = stopConflicts;
        _effects = effects;

        ScreenEffects = EffectLibrary.All.Where(e => e.Category == EffectCategory.Screen).ToList();
        ServerError = _server.Start();
        RefreshConnections();

        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    /// <summary>
    /// Режимы по картинке экрана для лент вдали от монитора в играх без своей интеграции.
    /// </summary>
    public IReadOnlyList<EffectDefinition> ScreenEffects { get; }

    public bool HasGame => GameTitle != null;

    public bool AutoMode
    {
        get => _config.AutoMode;
        set
        {
            if (_config.AutoMode == value)
                return;

            _config.AutoMode = value;
            _save();
            OnPropertyChanged();

            if (!value)
                StopLighting();
        }
    }

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

            if (_game?.Kind == GameKind.Other)
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

        // Ищем игру сразу, не дожидаясь очередной проверки по таймеру
        await CheckAsync();

        if (_game == null || IsRunning)
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

    [RelayCommand]
    private void ConnectDota()
    {
        Connect(GameKind.Dota2, "Dota 2");
    }

    [RelayCommand]
    private void ConnectCs()
    {
        Connect(GameKind.CounterStrike2, "Counter-Strike 2");
    }

    private void Connect(GameKind kind, string title)
    {
        try
        {
            GsiInstaller.Install(kind, _server.Address);
            Status = $"{title} подключена — перезапустите игру, чтобы она начала присылать события";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Status = $"Не удалось подключить {title}: {e.Message}";
        }

        RefreshConnections();
    }

    private void RefreshConnections()
    {
        IsDotaConnected = GsiInstaller.IsInstalled(GameKind.Dota2);
        IsCsConnected = GsiInstaller.IsInstalled(GameKind.CounterStrike2);
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

            if (IsRunning || _isSuspended)
                return;

            if (!AutoMode)
            {
                Status = $"{_game.Title}: игра запущена, автоматическая подсветка выключена";
                return;
            }

            StartLighting(_game);
        }
        finally
        {
            _isChecking = false;
        }
    }

    private void Restart()
    {
        if (IsRunning && _game != null)
            StartLighting(_game);
    }

    private void StartLighting(DetectedGame game)
    {
        var targets = _targets(UseAllDevices);

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
            var screenEffect = game.Kind == GameKind.Other && effect != null
                ? new PatternSource(effect, _effects.ValuesFor(effect), new StripPointMap())
                : null;

            var source = new GameSource(game.Kind, _server, device => targets.TryGetValue(device, out var binding) ? binding : null, screenEffect);
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
            GameKind.Dota2 when !IsDotaConnected => "Dota 2: игра не подключена — нажмите «Подключить Dota 2», пока подсветка ждёт событий",
            GameKind.Dota2 => "Dota 2: здоровье и мана героя на подсветке",
            GameKind.CounterStrike2 when !IsCsConnected => "Counter-Strike 2: игра не подключена — нажмите «Подключить CS2», пока подсветка ждёт событий",
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

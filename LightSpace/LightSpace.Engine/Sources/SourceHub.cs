namespace LightSpace.Engine.Sources;

/// <summary>
/// Что нужно одному потребителю источников: какие мониторы снимать, какие входы звука слушать и нужны ли события игр.
/// </summary>
/// <param name="Monitors">Системные имена мониторов; пустое имя — основной монитор Windows.</param>
/// <param name="WantsGameState">Нужно последнее состояние, которое присылают игры.</param>
public sealed record SourceDemand(IReadOnlySet<string> Monitors, IReadOnlySet<AudioInput> Inputs, bool WantsGameState = false)
{
    /// <summary>
    /// Ничего не нужно.
    /// </summary>
    public static readonly SourceDemand None = new(new HashSet<string>(), new HashSet<AudioInput>());

    /// <summary>
    /// Тот же набор мониторов и входов и та же нужда в событиях игр, что у other.
    /// </summary>
    public bool IsSameAs(SourceDemand other)
    {
        return Monitors.SetEquals(other.Monitors) && Inputs.SetEquals(other.Inputs) && WantsGameState == other.WantsGameState;
    }
}

/// <summary>
/// Источники данных для экрана, музыки и игр: каждый монитор и каждый вход звука снимается в своём потоке, а события
/// игр принимает сервер, только пока они нужны хотя бы одному потребителю — потоку кадров или живому превью.
/// Читать последнее состояние источников можно из любого потока без ожидания.
/// </summary>
public sealed class SourceHub : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<object, SourceDemand> _demands = new();

    // Работающие источники; словари заменяются целиком атомарной записью ссылки и потом не меняются
    private IReadOnlyDictionary<string, ScreenCapture> _screens = new Dictionary<string, ScreenCapture>();
    private IReadOnlyDictionary<AudioInput, AudioCapture> _inputs = new Dictionary<AudioInput, AudioCapture>();

    // Сервер событий игр; null — события сейчас никому не нужны
    private GsiServer? _games;
    private bool _isDisposed;

    /// <summary>
    /// Запоминает, что нужно потребителю user, вместо того, что ему было нужно раньше; источники, которые стали
    /// нужны, запускаются, ненужные никому — останавливаются. Можно вызывать из любого потока; если нужное
    /// не изменилось, ничего не происходит.
    /// </summary>
    public void Require(object user, SourceDemand demand)
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            if (_demands.TryGetValue(user, out var previous) && previous.IsSameAs(demand))
                return;

            _demands[user] = demand;

            var monitors = _demands.Values.SelectMany(d => d.Monitors).ToHashSet();
            var inputs = _demands.Values.SelectMany(d => d.Inputs).ToHashSet();

            Volatile.Write(ref _screens, Reconcile(_screens, monitors, key => new ScreenCapture(key)));
            Volatile.Write(ref _inputs, Reconcile(_inputs, inputs, input => new AudioCapture(input)));

            ReconcileGames(_demands.Values.Any(d => d.WantsGameState));
        }
    }

    /// <summary>
    /// Последняя картинка монитора; пустая — монитор сейчас никому не нужен, ещё не снят или снять его не удалось.
    /// </summary>
    public ScreenPicture PictureOf(string monitorKey)
    {
        return Volatile.Read(ref _screens).TryGetValue(monitorKey, out var capture) ? capture.Picture : ScreenPicture.Empty;
    }

    /// <summary>
    /// Последний снимок звука входа; тишина — вход сейчас никому не нужен или звука нет.
    /// </summary>
    public AudioLevels LevelsOf(AudioInput input)
    {
        return Volatile.Read(ref _inputs).TryGetValue(input, out var capture) ? capture.Levels : AudioLevels.Silent;
    }

    /// <summary>
    /// Последнее, что прислали игры; пусто — события сейчас никому не нужны или игры ещё ничего не прислали.
    /// </summary>
    public GameState Games => Volatile.Read(ref _games)?.State ?? GameState.None;

    /// <summary>
    /// Почему игры сейчас не могут присылать события, например порт занят; null — всё в порядке или события не нужны.
    /// </summary>
    public string? GamesProblem => Volatile.Read(ref _games)?.Problem;

    /// <summary>
    /// Останавливает все источники; новые больше не запускаются.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _isDisposed = true;
            _demands.Clear();

            Volatile.Write(ref _screens, Reconcile(_screens, new HashSet<string>(), key => new ScreenCapture(key)));
            Volatile.Write(ref _inputs, Reconcile(_inputs, new HashSet<AudioInput>(), input => new AudioCapture(input)));

            ReconcileGames(false);
        }
    }

    /// <summary>
    /// Запускает сервер событий игр, если он стал нужен, или останавливает, если больше не нужен никому.
    /// </summary>
    private void ReconcileGames(bool isWanted)
    {
        if (isWanted && _games == null)
        {
            Volatile.Write(ref _games, new GsiServer());
        }
        else if (!isWanted && _games != null)
        {
            _games.Dispose();
            Volatile.Write(ref _games, null);
        }
    }

    /// <summary>
    /// Новый набор работающих источников: нужные, которых не было, запускаются, ненужные — останавливаются.
    /// </summary>
    private static IReadOnlyDictionary<TKey, TSource> Reconcile<TKey, TSource>(IReadOnlyDictionary<TKey, TSource> running,
        IReadOnlySet<TKey> wanted, Func<TKey, TSource> start)
        where TKey : notnull
        where TSource : IDisposable
    {
        if (wanted.Count == running.Count && running.Keys.All(wanted.Contains))
            return running;

        var result = new Dictionary<TKey, TSource>();

        foreach (var (key, source) in running)
        {
            if (wanted.Contains(key))
                result[key] = source;
            else
                source.Dispose();
        }

        foreach (var key in wanted)
        {
            if (!result.ContainsKey(key))
                result[key] = start(key);
        }

        return result;
    }
}

namespace LightSpace.Engine.Sources;

/// <summary>
/// Что нужно одному потребителю источников: какие мониторы снимать и какие входы звука слушать.
/// </summary>
/// <param name="Monitors">Системные имена мониторов; пустое имя — основной монитор Windows.</param>
public sealed record SourceDemand(IReadOnlySet<string> Monitors, IReadOnlySet<AudioInput> Inputs)
{
    /// <summary>
    /// Ничего не нужно.
    /// </summary>
    public static readonly SourceDemand None = new(new HashSet<string>(), new HashSet<AudioInput>());

    /// <summary>
    /// Тот же набор мониторов и входов, что у other.
    /// </summary>
    public bool IsSameAs(SourceDemand other)
    {
        return Monitors.SetEquals(other.Monitors) && Inputs.SetEquals(other.Inputs);
    }
}

/// <summary>
/// Источники данных для экрана и музыки: каждый монитор и каждый вход звука снимается в своём потоке, только пока
/// он нужен хотя бы одному потребителю — потоку кадров или живому превью. Читать последнее состояние источников
/// можно из любого потока без ожидания.
/// </summary>
public sealed class SourceHub : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<object, SourceDemand> _demands = new();

    // Работающие источники; словари заменяются целиком атомарной записью ссылки и потом не меняются
    private IReadOnlyDictionary<string, ScreenCapture> _screens = new Dictionary<string, ScreenCapture>();
    private IReadOnlyDictionary<AudioInput, AudioCapture> _inputs = new Dictionary<AudioInput, AudioCapture>();
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

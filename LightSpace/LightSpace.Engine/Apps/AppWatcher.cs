using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core.Config;

namespace LightSpace.Engine.Apps;

/// <summary>
/// Насколько выполнено условие сценария.
/// </summary>
public enum ConditionState
{
    /// <summary>Условие не выполнено.</summary>
    NotMet,

    /// <summary>Условие выполнено: приложение запущено или его окно на переднем плане.</summary>
    Met,

    /// <summary>Приложение только что ушло с переднего плана; фокусное условие держится, пока не пройдёт задержка снятия.</summary>
    Releasing
}

/// <summary>
/// Следит за приложениями: какие запущены и чьё окно на переднем плане. Наружу отвечает только, выполнено ли условие
/// сценария, и сообщает, когда ответы могли измениться. Создаётся и используется в потоке окна: смену окна на переднем
/// плане Windows сообщает через очередь сообщений этого потока, список процессов читается раз в секунду в фоне.
/// </summary>
public sealed class AppWatcher : IDisposable
{
    // Как часто перечитывается список запущенных процессов
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Запас, чтобы задержка снятия к моменту проверки точно истекла
    private static readonly TimeSpan ReleaseSlack = TimeSpan.FromMilliseconds(50);

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly SynchronizationContext _context;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Timer _pollTimer;
    private readonly Timer _releaseTimer;
    private readonly IntPtr _hook;

    // Делегат держим в поле: Windows вызывает его, пока хук установлен, и сборщик мусора не должен его забрать
    private readonly WinEventProc _onForeground;

    // Имена процессов без .exe; читаются и меняются только в потоке окна
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private string? _focused;

    // Когда приложения ушли с переднего плана, по времени часов _clock
    private readonly Dictionary<string, TimeSpan> _releasedAt = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _focusReleaseDelay = TimeSpan.FromSeconds(2);
    private bool _isDisposed;

    /// <summary>
    /// Создаётся в потоке окна: в нём приходят все события и вызываются все методы.
    /// </summary>
    public AppWatcher()
    {
        _context = SynchronizationContext.Current
                   ?? throw new InvalidOperationException("Слежение за приложениями создаётся в потоке окна.");

        _focused = ProcessNameOf(GetForegroundWindow());

        _onForeground = OnForeground;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _onForeground, 0, 0, WinEventOutOfContext);

        _releaseTimer = new Timer(_ => _context.Post(_ => OnReleaseDue(), null));
        _pollTimer = new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);
    }

    /// <summary>
    /// Выполнение условий могло измениться: запустилось или закрылось приложение, сменилось окно на переднем плане,
    /// истекла задержка снятия. Приходит в потоке окна.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Сколько фокусное условие ещё держится после того, как приложение ушло с переднего плана.
    /// </summary>
    public TimeSpan FocusReleaseDelay
    {
        get => _focusReleaseDelay;
        set
        {
            if (value == _focusReleaseDelay)
                return;

            _focusReleaseDelay = value < TimeSpan.Zero ? TimeSpan.Zero : value;

            ScheduleRelease();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Выполнено ли условие сценария сейчас. Условие без приложения не выполняется никогда.
    /// </summary>
    public ConditionState StateOf(ScenarioCondition condition)
    {
        if (!condition.IsSet)
            return ConditionState.NotMet;

        var name = NameOf(condition.ExeName);

        if (condition.Mode == ConditionMode.Running)
            return _running.Contains(name) ? ConditionState.Met : ConditionState.NotMet;

        if (string.Equals(_focused, name, StringComparison.OrdinalIgnoreCase))
            return ConditionState.Met;

        return _releasedAt.TryGetValue(name, out var released) && _clock.Elapsed - released < _focusReleaseDelay
            ? ConditionState.Releasing
            : ConditionState.NotMet;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);

        _pollTimer.Dispose();
        _releaseTimer.Dispose();
    }

    /// <summary>
    /// Имя процесса для exe-файла: имя файла без расширения .exe — так Windows называет процессы.
    /// </summary>
    private static string NameOf(string exeName)
    {
        return exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;
    }

    /// <summary>
    /// Читает список процессов в фоне и передаёт его в поток окна.
    /// </summary>
    private void Poll()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    running.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    // Процесс завершился, пока его читали
                }
            }
        }

        _context.Post(_ => OnPolled(running), null);
    }

    private void OnPolled(HashSet<string> running)
    {
        if (_isDisposed || running.SetEquals(_running))
            return;

        _running = running;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Окно на переднем плане сменилось; Windows вызывает это в потоке окна, через его очередь сообщений.
    /// </summary>
    private void OnForeground(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (_isDisposed)
            return;

        var focused = ProcessNameOf(window);

        if (string.Equals(focused, _focused, StringComparison.OrdinalIgnoreCase))
            return;

        // Ушедшее приложение держит фокусное условие ещё на время задержки снятия, пришедшее — включается сразу
        if (_focused != null)
            _releasedAt[_focused] = _clock.Elapsed;

        if (focused != null)
            _releasedAt.Remove(focused);

        _focused = focused;

        ScheduleRelease();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnReleaseDue()
    {
        if (_isDisposed)
            return;

        ScheduleRelease();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Забывает истёкшие задержки снятия и заводит таймер на ближайшую из оставшихся.
    /// </summary>
    private void ScheduleRelease()
    {
        var now = _clock.Elapsed;

        foreach (var name in _releasedAt.Where(r => now - r.Value >= _focusReleaseDelay).Select(r => r.Key).ToList())
            _releasedAt.Remove(name);

        if (_releasedAt.Count == 0)
        {
            _releaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var due = _releasedAt.Values.Min() + _focusReleaseDelay - now + ReleaseSlack;

        _releaseTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Имя процесса, которому принадлежит окно; null — окна нет или процесс уже завершился.
    /// </summary>
    private static string? ProcessNameOf(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(window, out var processId);

        if (processId == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);

            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId,
        uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

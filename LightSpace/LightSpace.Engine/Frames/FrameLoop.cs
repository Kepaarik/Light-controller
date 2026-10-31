using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Поток кадров, один на приложение. Каждый кадр берёт последний снимок настроек, считает кадр для каждого устройства
/// и кладёт его в вывод устройства. С окном поток делит только неизменяемые снимки: окно подменяет снимок настроек,
/// поток подменяет снимок цветов и время расчёта.
/// </summary>
public sealed class FrameLoop : IDisposable
{
    // Окну цвета нужны не чаще 30 раз в секунду, даже если устройства получают 60 кадров.
    // Порог чуть меньше 1/30 с: кадры приходят с разбросом, и при 30 к/с ровный порог пропускал бы каждый второй
    private static readonly TimeSpan LiveColorsInterval = TimeSpan.FromMilliseconds(30);

    // Сколько остановка ждёт, пока досчитается последний кадр
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMilliseconds(500);

    // Вес нового кадра в среднем времени расчёта: среднее примерно по последним 20 кадрам
    private const double AverageWeight = 0.05;

    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;

    // Снимки заменяются целиком атомарной записью ссылки
    private FrameSnapshot _snapshot;
    private LiveColors? _liveColors;
    private FrameTiming _timing = FrameTiming.None;
    private volatile bool _isLiveColorsWanted;

    public FrameLoop(FrameSnapshot snapshot)
    {
        _snapshot = snapshot;

        _thread = new Thread(Run) { IsBackground = true, Name = "Light Space: кадры" };
        _thread.Start();
    }

    /// <summary>
    /// Время расчёта кадров: последнего и в среднем.
    /// </summary>
    public FrameTiming Timing => Volatile.Read(ref _timing);

    /// <summary>
    /// Последний снимок отправленных цветов; null — окну цвета сейчас не нужны или кадров ещё не было.
    /// </summary>
    public LiveColors? LiveColors => Volatile.Read(ref _liveColors);

    /// <summary>
    /// Окну нужны живые цвета устройств; пока окно скрыто, снимок цветов не собирается.
    /// </summary>
    public bool IsLiveColorsWanted
    {
        get => _isLiveColorsWanted;
        set
        {
            _isLiveColorsWanted = value;

            if (!value)
                Volatile.Write(ref _liveColors, null);
        }
    }

    /// <summary>
    /// Подменяет снимок настроек; поток кадров возьмёт его со следующего кадра. Можно вызывать из любого потока.
    /// </summary>
    public void Update(FrameSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
    }

    public void Dispose()
    {
        _stop.Set();

        // Последний кадр мог ещё считаться — дожидаемся, чтобы он не ушёл на уже закрытые устройства
        if (_thread.Join(StopTimeout))
            _stop.Dispose();
    }

    private void Run()
    {
        // Обычный системный таймер Windows тикает раз в 15,6 мс — для 30 и 60 кадров нужен точный
        TimeBeginPeriod(1);

        try
        {
            var clock = Stopwatch.StartNew();
            var lastTick = clock.Elapsed;
            var lastLive = -LiveColorsInterval;
            var next = clock.Elapsed;
            var time = 0.0;
            string? effectId = null;

            while (true)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                var now = clock.Elapsed;

                // Новый эффект начинается с начала; у текущего время копится по шагам, чтобы смена скорости не давала скачка
                if (snapshot.Effect.Id != effectId)
                {
                    effectId = snapshot.Effect.Id;
                    time = 0;
                }
                else
                {
                    time += (now - lastTick).TotalSeconds * snapshot.Speed;
                }

                lastTick = now;

                var isLiveDue = _isLiveColorsWanted && now - lastLive >= LiveColorsInterval;

                if (isLiveDue)
                    lastLive = now;

                RenderFrame(snapshot, time, isLiveDue);

                // Отставший поток не догоняет пачкой кадров, а продолжает с текущего момента
                next += TimeSpan.FromSeconds(1.0 / Math.Max(snapshot.FrameRate, 1));

                var wait = next - clock.Elapsed;

                if (wait < TimeSpan.Zero)
                {
                    next = clock.Elapsed;
                    wait = TimeSpan.Zero;
                }

                if (_stop.Wait(wait))
                    break;
            }
        }
        finally
        {
            TimeEndPeriod(1);
        }
    }

    /// <summary>
    /// Считает кадр для каждого устройства снимка и отправляет его; isLiveDue — заодно собрать снимок цветов для окна.
    /// </summary>
    private void RenderFrame(FrameSnapshot snapshot, double time, bool isLiveDue)
    {
        var live = isLiveDue ? new Dictionary<string, IReadOnlyList<RgbColor>>() : null;

        // Выключенная подсветка ничего не показывает — и считать для неё нечего
        if (!snapshot.IsLightingEnabled)
        {
            if (live != null)
                Volatile.Write(ref _liveColors, new LiveColors(false, snapshot.Brightness, live));

            return;
        }

        var started = Stopwatch.GetTimestamp();

        foreach (var device in snapshot.Devices)
        {
            var colors = EffectRenderer.Render(snapshot.Effect, snapshot.Values, device.Geometry, time);

            var frame = device.Geometry.IsAddressable
                ? OutputFrame.PerLed(colors, snapshot.Brightness)
                : OutputFrame.Solid(colors[0], snapshot.Brightness);

            try
            {
                device.Output.Submit(frame);
            }
            catch (ObjectDisposedException)
            {
                // Устройство отключили в потоке окна, пока считался кадр; новый снимок без него придёт со следующим кадром
            }

            live?.Add(device.Id, colors);
        }

        RecordTiming(Stopwatch.GetElapsedTime(started));

        if (live != null)
            Volatile.Write(ref _liveColors, new LiveColors(true, snapshot.Brightness, live));
    }

    private void RecordTiming(TimeSpan duration)
    {
        var previous = _timing;

        var average = previous.Frames == 0
            ? duration
            : previous.Average + (duration - previous.Average) * AverageWeight;

        Volatile.Write(ref _timing, new FrameTiming(duration, average, previous.Frames + 1));
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

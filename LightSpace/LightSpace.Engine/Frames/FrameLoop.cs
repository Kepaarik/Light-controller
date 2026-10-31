using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;

namespace LightSpace.Engine.Frames;

/// <summary>
/// Поток кадров, один на приложение. Каждый кадр берёт последний снимок настроек, для каждой группы считает кадр
/// каждого её устройства содержимым группы и кладёт его в вывод устройства. С окном поток делит только неизменяемые
/// снимки: окно подменяет снимок настроек, поток подменяет снимок цветов и время расчёта.
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

            // Время эффекта у каждой группы своё; часы живут только в потоке кадров
            var clocks = new Dictionary<string, EffectClock>();

            while (true)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                var now = clock.Elapsed;

                AdvanceClocks(clocks, snapshot, (now - lastTick).TotalSeconds);
                lastTick = now;

                var isLiveDue = _isLiveColorsWanted && now - lastLive >= LiveColorsInterval;

                if (isLiveDue)
                    lastLive = now;

                RenderFrame(snapshot, clocks, isLiveDue);

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
    /// Двигает время эффекта каждой группы на seconds секунд с учётом его скорости.
    /// </summary>
    private static void AdvanceClocks(Dictionary<string, EffectClock> clocks, FrameSnapshot snapshot, double seconds)
    {
        foreach (var group in snapshot.Groups)
        {
            var effectId = group.Active.Effect.Id;

            // Новый эффект начинается с начала; у текущего время копится по шагам, чтобы смена скорости не давала скачка
            if (!clocks.TryGetValue(group.Id, out var clock) || clock.EffectId != effectId)
                clocks[group.Id] = new EffectClock(effectId);
            else
                clock.Time += seconds * group.Active.Speed;
        }

        // Часы распущенных и удалённых групп больше не нужны
        if (clocks.Count > snapshot.Groups.Count)
        {
            foreach (var id in clocks.Keys.Except(snapshot.Groups.Select(g => g.Id)).ToList())
                clocks.Remove(id);
        }
    }

    /// <summary>
    /// Считает кадр для каждого устройства каждой группы её содержимым и отправляет его;
    /// isLiveDue — заодно собрать снимок цветов для окна.
    /// </summary>
    private void RenderFrame(FrameSnapshot snapshot, Dictionary<string, EffectClock> clocks, bool isLiveDue)
    {
        var live = isLiveDue ? new Dictionary<string, LiveDeviceColors>() : null;

        // Выключенная подсветка ничего не показывает — и считать для неё нечего
        if (!snapshot.IsLightingEnabled)
        {
            if (live != null)
                Volatile.Write(ref _liveColors, new LiveColors(false, live));

            return;
        }

        var started = Stopwatch.GetTimestamp();

        foreach (var group in snapshot.Groups)
        {
            var content = group.Active;
            var time = clocks[group.Id].Time;

            foreach (var device in group.Devices)
            {
                var colors = EffectRenderer.Render(content.Effect, content.Values, device.Geometry, time);

                var frame = device.Geometry.IsAddressable
                    ? OutputFrame.PerLed(colors, group.Brightness)
                    : OutputFrame.Solid(colors[0], group.Brightness);

                try
                {
                    device.Output.Submit(frame);
                }
                catch (ObjectDisposedException)
                {
                    // Устройство отключили в потоке окна, пока считался кадр; новый снимок без него придёт со следующим кадром
                }

                if (live != null)
                    live[device.Id] = new LiveDeviceColors(colors, group.Brightness);
            }
        }

        RecordTiming(Stopwatch.GetElapsedTime(started));

        if (live != null)
            Volatile.Write(ref _liveColors, new LiveColors(true, live));
    }

    private void RecordTiming(TimeSpan duration)
    {
        var previous = _timing;

        var average = previous.Frames == 0
            ? duration
            : previous.Average + (duration - previous.Average) * AverageWeight;

        Volatile.Write(ref _timing, new FrameTiming(duration, average, previous.Frames + 1));
    }

    /// <summary>
    /// Время эффекта одной группы: сколько секунд эффекта прошло с его начала с учётом скорости.
    /// </summary>
    private sealed class EffectClock
    {
        public EffectClock(string effectId)
        {
            EffectId = effectId;
        }

        /// <summary>
        /// Эффект, время которого считают эти часы; другой эффект начинается с нуля.
        /// </summary>
        public string EffectId { get; }

        public double Time { get; set; }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

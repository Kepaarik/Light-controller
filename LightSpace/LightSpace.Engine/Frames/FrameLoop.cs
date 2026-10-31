using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Space;

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

            // Время эффекта у каждой группы своё; часы живут только в потоке кадров.
            // Вместе с часами группа помнит, что играло до смены сценария, — для плавного перехода
            var playbacks = new Dictionary<string, GroupPlayback>();

            while (true)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                var now = clock.Elapsed;

                AdvanceClocks(playbacks, snapshot, (now - lastTick).TotalSeconds);
                lastTick = now;

                var isLiveDue = _isLiveColorsWanted && now - lastLive >= LiveColorsInterval;

                if (isLiveDue)
                    lastLive = now;

                RenderFrame(snapshot, playbacks, isLiveDue);

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
    /// Двигает время эффекта каждой группы на seconds секунд с учётом его скорости; если в группе сменился сценарий,
    /// начинает плавный переход от прежнего содержимого к новому.
    /// </summary>
    private static void AdvanceClocks(Dictionary<string, GroupPlayback> playbacks, FrameSnapshot snapshot, double seconds)
    {
        var transition = snapshot.TransitionMilliseconds / 1000.0;

        foreach (var group in snapshot.Groups)
        {
            if (!playbacks.TryGetValue(group.Id, out var playback))
                playbacks[group.Id] = new GroupPlayback(group.Active);
            else
                playback.Advance(group.Active, seconds, transition);
        }

        // Часы распущенных и удалённых групп больше не нужны
        if (playbacks.Count > snapshot.Groups.Count)
        {
            foreach (var id in playbacks.Keys.Except(snapshot.Groups.Select(g => g.Id)).ToList())
                playbacks.Remove(id);
        }
    }

    /// <summary>
    /// Считает кадр для каждого устройства каждой группы её содержимым и отправляет его; во время перехода — смесь
    /// уходящего и нового содержимого. isLiveDue — заодно собрать снимок цветов для окна.
    /// </summary>
    private void RenderFrame(FrameSnapshot snapshot, Dictionary<string, GroupPlayback> playbacks, bool isLiveDue)
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
            var playback = playbacks[group.Id];

            foreach (var device in group.Devices)
            {
                var colors = playback.Render(device.Geometry);

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

    /// <summary>
    /// Что играет в одной группе: содержимое и его часы, а во время плавного перехода — ещё уходящее содержимое
    /// со своими часами, чтобы оно доигрывало, а не замирало.
    /// </summary>
    private sealed class GroupPlayback
    {
        // Уходящее содержимое; null — перехода сейчас нет
        private (FrameContent Content, EffectClock Clock)? _leaving;

        // Сколько секунд идёт переход и сколько он длится всего
        private double _transitionTime;
        private double _transitionLength;

        public GroupPlayback(FrameContent content)
        {
            Content = content;
            Clock = new EffectClock(content.Effect.Id);
        }

        /// <summary>
        /// Содержимое, которое играет в группе сейчас или к которому идёт переход.
        /// </summary>
        public FrameContent Content { get; private set; }

        public EffectClock Clock { get; private set; }

        /// <summary>
        /// Двигает время на seconds секунд. content — что должно играть по последнему снимку; если это другой сценарий,
        /// начинается переход длиной transition секунд (0 — смена сразу).
        /// </summary>
        public void Advance(FrameContent content, double seconds, double transition)
        {
            if (_leaving is { } leaving)
            {
                leaving.Clock.Time += seconds * leaving.Content.Speed;
                _transitionTime += seconds;

                if (_transitionTime >= _transitionLength)
                    _leaving = null;
            }

            if (content.ScenarioId != Content.ScenarioId)
            {
                StartTransition(content, transition);
                return;
            }

            Content = content;

            // Новый эффект начинается с начала; у текущего время копится по шагам, чтобы смена скорости не давала скачка
            if (Clock.EffectId != content.Effect.Id)
                Clock = new EffectClock(content.Effect.Id);
            else
                Clock.Time += seconds * content.Speed;
        }

        /// <summary>
        /// Цвета светодиодов пространства: кадр содержимого, во время перехода — плавно смешанный с кадром уходящего.
        /// </summary>
        public RgbColor[] Render(DeviceGeometry space)
        {
            var colors = EffectRenderer.Render(Content.Effect, Content.Values, space, Clock.Time);

            if (_leaving is not { } leaving)
                return colors;

            var previous = EffectRenderer.Render(leaving.Content.Effect, leaving.Content.Values, space, leaving.Clock.Time);
            var progress = EffectMath.Smooth(_transitionTime / _transitionLength);

            for (var i = 0; i < colors.Length; i++)
                colors[i] = EffectMath.Mix(previous[i], colors[i], progress);

            return colors;
        }

        private void StartTransition(FrameContent content, double transition)
        {
            // Сценарий вернулся, пока его ещё уводили: переход разворачивается с того же места, без скачка
            if (_leaving is { } leaving && leaving.Content.ScenarioId == content.ScenarioId)
            {
                _leaving = (Content, Clock);
                Content = content;
                Clock = leaving.Clock;
                _transitionTime = _transitionLength - _transitionTime;
                return;
            }

            // Прежнее содержимое плавно уходит, новое начинается с начала
            _leaving = transition > 0 ? (Content, Clock) : null;
            _transitionTime = 0;
            _transitionLength = transition;

            Content = content;
            Clock = new EffectClock(content.Effect.Id);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

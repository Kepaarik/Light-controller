using System.Diagnostics;
using System.Runtime.InteropServices;
using LightSpace.Core;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Output;
using LightSpace.Engine.Sources;
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
    private readonly SourceHub _sources;

    // Снимки заменяются целиком атомарной записью ссылки
    private FrameSnapshot _snapshot;
    private LiveColors? _liveColors;
    private FrameTiming _timing = FrameTiming.None;
    private volatile bool _isLiveColorsWanted;

    /// <summary>
    /// sources — откуда экран и музыка берут картинку монитора и звук; поток кадров сам говорит, какие источники ему нужны.
    /// </summary>
    public FrameLoop(FrameSnapshot snapshot, SourceHub sources)
    {
        _snapshot = snapshot;
        _sources = sources;

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

                AdvanceClocks(playbacks, snapshot, (now - lastTick).TotalSeconds, _sources);
                lastTick = now;

                // Экран и звук снимаются, а события игр принимаются, только пока их играет хоть одна группа, в том числе уходящим сценарием
                _sources.Require(this, DemandOf(snapshot, playbacks));

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

            // Остановленному потоку кадров источники больше не нужны
            _sources.Require(this, SourceDemand.None);
        }
    }

    /// <summary>
    /// Какие мониторы и входы звука и нужны ли события игр группам, у которых есть устройства: для того, что играет,
    /// и того, что уходит.
    /// Выключенной подсветке не нужно ничего.
    /// </summary>
    private static SourceDemand DemandOf(FrameSnapshot snapshot, Dictionary<string, GroupPlayback> playbacks)
    {
        if (!snapshot.IsLightingEnabled)
            return SourceDemand.None;

        var monitors = new HashSet<string>();
        var inputs = new HashSet<AudioInput>();
        var wantsGameState = false;

        foreach (var group in snapshot.Groups)
        {
            if (group.Devices.Count == 0)
                continue;

            foreach (var content in playbacks[group.Id].Playing)
            {
                if (content.Screen is { } screen)
                    monitors.Add(screen.MonitorKey);

                if (content.Music is { } music)
                    inputs.Add(music.Input);

                if (content.Game is { } game)
                {
                    if (game.NeedsScreen)
                        monitors.Add(game.Screen.MonitorKey);

                    wantsGameState |= game.NeedsGameState;
                }
            }
        }

        return new SourceDemand(monitors, inputs, wantsGameState);
    }

    /// <summary>
    /// Двигает время эффекта каждой группы на seconds секунд с учётом его скорости; если в группе сменился сценарий,
    /// начинает плавный переход от прежнего содержимого к новому.
    /// </summary>
    private static void AdvanceClocks(Dictionary<string, GroupPlayback> playbacks, FrameSnapshot snapshot, double seconds, SourceHub sources)
    {
        var transition = snapshot.TransitionMilliseconds / 1000.0;

        foreach (var group in snapshot.Groups)
        {
            if (!playbacks.TryGetValue(group.Id, out var playback))
                playbacks[group.Id] = new GroupPlayback(group.Active);
            else
                playback.Advance(group.Active, seconds, transition, sources);
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
            var builtin = playback.Builtin;

            foreach (var device in group.Devices)
            {
                // Встроенный эффект устройство играет само: ему уходит запуск эффекта, а не кадр. Живых цветов у него нет
                if (builtin?.StartFor(device.Id, device.Output.Device, group.Brightness) is { } start)
                {
                    try
                    {
                        device.Output.StartBuiltin(start);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Устройство отключили в потоке окна; новый снимок без него придёт со следующим кадром
                    }

                    continue;
                }

                var colors = playback.Render(device);

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

        /// <summary>
        /// Время слоёв, когда часы считают сцену.
        /// </summary>
        public SceneClock Layers { get; } = new();

        /// <summary>
        /// Сглаженные цвета, когда часы считают экран.
        /// </summary>
        public ScreenPlayback Screen { get; } = new();

        /// <summary>
        /// Сглаженный звук, когда часы считают музыку.
        /// </summary>
        public MusicPlayback Music { get; } = new();

        /// <summary>
        /// Урон, бомба и сглаженные цвета экрана, когда часы считают игру.
        /// </summary>
        public GamePlayback Game { get; } = new();

        /// <summary>
        /// Устройства, на которых кадр этого содержимого сейчас не считается: о серии ошибок предупреждаем один раз.
        /// </summary>
        public HashSet<string> FailingDevices { get; } = new();

        /// <summary>
        /// Двигает время содержимого на seconds секунд с учётом его скорости, у сцены — время каждого её слоя,
        /// у экрана, музыки и игры — берёт последнюю картинку монитора, звук или состояние игры из источников.
        /// </summary>
        public void Advance(FrameContent content, double seconds, SourceHub sources)
        {
            Time += seconds * content.Speed;

            if (content.Layers != null)
                Layers.Advance(content.Layers, seconds);

            if (content.Screen is { } screen)
                Screen.Advance(screen, sources.PictureOf(screen.MonitorKey), seconds);

            if (content.Music is { } music)
                Music.Advance(music, sources.LevelsOf(music.Input), seconds);

            if (content.Game is { } game)
                Game.Advance(game, sources.Games, sources.PictureOf(game.Screen.MonitorKey), seconds);
        }
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
            Clock = new EffectClock(content.ClockId);
        }

        /// <summary>
        /// Содержимое, которое играет в группе сейчас или к которому идёт переход.
        /// </summary>
        public FrameContent Content { get; private set; }

        public EffectClock Clock { get; private set; }

        /// <summary>
        /// Встроенный эффект, который устройства группы играют сами; null — играет что-то другое или идёт переход.
        /// Переход к встроенному эффекту — уходящее содержимое плавно гаснет, и только потом устройства запускают эффект;
        /// переход от него — новое содержимое плавно проявляется из темноты.
        /// </summary>
        public BuiltinSettings? Builtin => _leaving == null ? Content.Builtin : null;

        /// <summary>
        /// Всё, что сейчас звучит в группе: содержимое и, во время перехода, уходящее содержимое.
        /// </summary>
        public IEnumerable<FrameContent> Playing
        {
            get
            {
                yield return Content;

                if (_leaving is { } leaving)
                    yield return leaving.Content;
            }
        }

        /// <summary>
        /// Двигает время на seconds секунд. content — что должно играть по последнему снимку; если это другой сценарий,
        /// начинается переход длиной transition секунд (0 — смена сразу).
        /// </summary>
        public void Advance(FrameContent content, double seconds, double transition, SourceHub sources)
        {
            if (_leaving is { } leaving)
            {
                leaving.Clock.Advance(leaving.Content, seconds, sources);
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
            if (Clock.EffectId != content.ClockId)
                Clock = new EffectClock(content.ClockId);
            else
                Clock.Advance(content, seconds, sources);
        }

        /// <summary>
        /// Цвета светодиодов пространства: кадр содержимого, во время перехода — плавно смешанный с кадром уходящего.
        /// </summary>
        public RgbColor[] Render(FrameDevice device)
        {
            var colors = RenderContent(Content, Clock, device);

            if (_leaving is not { } leaving)
                return colors;

            var previous = RenderContent(leaving.Content, leaving.Clock, device);
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
            Clock = new EffectClock(content.ClockId);
        }

        /// <summary>
        /// Кадр содержимого на устройстве: эффект и музыка играют в пространстве самого устройства, сцена и экран — по местам его
        /// светодиодов на сцене, полоски игры — в пространстве устройства, а картинка экрана в игре — по местам на сцене.
        /// У устройства одного цвета во всех случаях один цвет.
        /// </summary>
        private static RgbColor[] RenderContent(FrameContent content, EffectClock clock, FrameDevice device)
        {
            // Встроенный эффект кадрами не показать: в переходах он — темнота, устройство без встроенных эффектов не светится
            if (content.Builtin != null)
                return new RgbColor[Math.Max(device.ScenePoints.Count, 1)];

            if (content.Screen != null || content.Music != null || content.Game != null)
                return RenderSource(content, clock, device);

            // Ошибка эффекта или сцены, как и у экрана, не останавливает поток кадров: устройство на этом кадре гаснет
            try
            {
                var colors = content.Layers is { } layers
                    ? SceneRenderer.Render(layers, clock.Layers, device.ScenePoints)
                    : EffectRenderer.Render(content.Effect, content.Values, device.Geometry, clock.Time);

                clock.FailingDevices.Remove(device.Id);

                return colors;
            }
            catch (Exception e)
            {
                // Кадр повторяется десятки раз в секунду — предупреждение одно на всю серию ошибок
                if (clock.FailingDevices.Add(device.Id))
                    Trace.TraceWarning($"Light Space: не удалось посчитать кадр устройства {device.Id}: {e.Message}");

                return new RgbColor[Math.Max(device.ScenePoints.Count, 1)];
            }
        }

        /// <summary>
        /// Кадр экрана, музыки или игры. Ошибка в нём не останавливает поток кадров: устройство на этом кадре гаснет.
        /// </summary>
        private static RgbColor[] RenderSource(FrameContent content, EffectClock clock, FrameDevice device)
        {
            try
            {
                RgbColor[] colors;

                if (content.Game is { } game)
                    colors = clock.Game.Render(device.Id, game, device.Geometry, device.ScenePoints, SceneArea.Width, SceneArea.Height);
                else
                    colors = content.Screen is { } screen
                        ? clock.Screen.Render(device.Id, screen, device.ScenePoints, SceneArea.Width, SceneArea.Height)
                        : clock.Music.Render(content.Music!, device.Geometry);

                clock.FailingDevices.Remove(device.Id);

                return colors;
            }
            catch (Exception e)
            {
                // Кадр повторяется десятки раз в секунду — предупреждение одно на всю серию ошибок
                if (clock.FailingDevices.Add(device.Id))
                    Trace.TraceWarning($"Light Space: не удалось посчитать кадр устройства {device.Id}: {e.Message}");

                return new RgbColor[Math.Max(device.ScenePoints.Count, 1)];
            }
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

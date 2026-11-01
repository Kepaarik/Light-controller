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
    // Кадры считаются 20 раз в секунду — ровно с той частотой, с какой их принимают устройства. Если считать чаще,
    // устройство берёт то каждый второй, то каждый третий кадр, и анимация на нём идёт рывками
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    // Окну цвета нужны не чаще 30 раз в секунду.
    // Порог чуть меньше 1/30 с: кадры приходят с разбросом, и ровный порог пропускал бы кадры
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
        // Обычный системный таймер Windows тикает раз в 15,6 мс — для ровных кадров нужен точный
        TimeBeginPeriod(1);

        try
        {
            var clock = Stopwatch.StartNew();
            var lastTick = clock.Elapsed;
            var lastLive = -LiveColorsInterval;
            var next = clock.Elapsed;

            // Время эффекта у каждой группы своё; часы живут только в потоке кадров
            var playbacks = new Dictionary<string, GroupPlayback>();

            while (true)
            {
                var snapshot = Volatile.Read(ref _snapshot);
                var now = clock.Elapsed;

                AdvanceClocks(playbacks, snapshot, (now - lastTick).TotalSeconds, _sources);
                lastTick = now;

                // Экран и звук снимаются, а события игр принимаются, только пока их играет хоть одна группа
                _sources.Require(this, DemandOf(snapshot, playbacks));

                var isLiveDue = _isLiveColorsWanted && now - lastLive >= LiveColorsInterval;

                if (isLiveDue)
                    lastLive = now;

                RenderFrame(snapshot, playbacks, isLiveDue);

                // Отставший поток не догоняет пачкой кадров, а продолжает с текущего момента
                next += FrameInterval;

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
    /// Какие мониторы и входы звука и нужны ли события игр группам, у которых есть устройства.
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

            var content = playbacks[group.Id].Content;

            if (content.Screen is { } screen)
            {
                monitors.Add(screen.MonitorKey);

                // Устройства, привязанные к монитору, берут цвета с краёв своего монитора
                foreach (var device in group.Devices)
                {
                    if (device.Monitor is { } binding)
                        monitors.Add(binding.MonitorKey);
                }
            }

            if (content.Music is { } music)
                inputs.Add(music.Input);

            if (content.Game is { } game)
            {
                if (game.NeedsScreen)
                    monitors.Add(game.Screen.MonitorKey);

                wantsGameState |= game.NeedsGameState;
            }
        }

        return new SourceDemand(monitors, inputs, wantsGameState);
    }

    /// <summary>
    /// Двигает время эффекта каждой группы на seconds секунд с учётом его скорости. Если эффект группы сменился,
    /// её устройства сбрасываются: прежний эффект останавливается и стирается, прежде чем придёт кадр нового.
    /// </summary>
    private static void AdvanceClocks(Dictionary<string, GroupPlayback> playbacks, FrameSnapshot snapshot, double seconds, SourceHub sources)
    {
        foreach (var group in snapshot.Groups)
        {
            if (!playbacks.TryGetValue(group.Id, out var playback))
            {
                playbacks[group.Id] = new GroupPlayback(group.Active);
                continue;
            }

            if (playback.Advance(group.Active, seconds, sources))
                ResetDevices(group);
        }

        // Часы распущенных и удалённых групп больше не нужны
        if (playbacks.Count > snapshot.Groups.Count)
        {
            foreach (var id in playbacks.Keys.Except(snapshot.Groups.Select(g => g.Id)).ToList())
                playbacks.Remove(id);
        }
    }

    private static void ResetDevices(FrameGroup group)
    {
        foreach (var device in group.Devices)
        {
            try
            {
                device.Output.Reset();
            }
            catch (ObjectDisposedException)
            {
                // Устройство отключили в потоке окна; новый снимок без него придёт со следующим кадром
            }
        }
    }

    /// <summary>
    /// Считает кадр для каждого устройства каждой группы её содержимым и отправляет его.
    /// isLiveDue — заодно собрать снимок цветов для окна.
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

                    // Окно вместо цветов показывает, что устройство играет встроенный эффект
                    if (live != null)
                        live[device.Id] = new LiveDeviceColors(Array.Empty<RgbColor>(), group.Brightness, true);

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
                Screen.Advance(screen, sources.PictureOf(screen.MonitorKey), seconds, sources.PictureOf);

            if (content.Music is { } music)
                Music.Advance(music, sources.LevelsOf(music.Input), seconds);

            if (content.Game is { } game)
                Game.Advance(game, sources.Games, sources.PictureOf(game.Screen.MonitorKey), seconds);
        }
    }

    /// <summary>
    /// Что играет в одной группе: содержимое и его часы.
    /// </summary>
    private sealed class GroupPlayback
    {
        public GroupPlayback(FrameContent content)
        {
            Content = content;
            Clock = new EffectClock(content.ClockId);
        }

        /// <summary>
        /// Содержимое, которое играет в группе.
        /// </summary>
        public FrameContent Content { get; private set; }

        public EffectClock Clock { get; private set; }

        /// <summary>
        /// Встроенный эффект, который устройства группы играют сами; null — играет что-то другое.
        /// </summary>
        public BuiltinSettings? Builtin => Content.Builtin;

        /// <summary>
        /// Двигает время на seconds секунд. content — что должно играть по последнему снимку.
        /// true — эффект сменился: прежний остановлен, его часы со всем, что они помнили, отпущены.
        /// </summary>
        public bool Advance(FrameContent content, double seconds, SourceHub sources)
        {
            Content = content;

            // Новый эффект начинается с начала; у текущего время копится по шагам, чтобы смена скорости не давала скачка
            if (Clock.EffectId != content.ClockId)
            {
                Clock = new EffectClock(content.ClockId);
                return true;
            }

            Clock.Advance(content, seconds, sources);

            return false;
        }

        /// <summary>
        /// Цвета светодиодов пространства: кадр содержимого.
        /// </summary>
        public RgbColor[] Render(FrameDevice device)
        {
            return RenderContent(Content, Clock, device);
        }

        /// <summary>
        /// Кадр содержимого на устройстве: эффект и музыка играют в пространстве самого устройства, сцена и экран — по местам его
        /// светодиодов на сцене, полоски игры — в пространстве устройства, а картинка экрана в игре — по местам на сцене.
        /// У устройства одного цвета во всех случаях один цвет.
        /// </summary>
        private static RgbColor[] RenderContent(FrameContent content, EffectClock clock, FrameDevice device)
        {
            // Встроенный эффект кадрами не показать: устройство без встроенных эффектов не светится
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
                else if (content.Screen is { } screen)
                    colors = device.Monitor is { } binding
                        ? clock.Screen.RenderBound(device.Id, screen, binding)
                        : clock.Screen.Render(device.Id, screen, device.ScenePoints, SceneArea.Width, SceneArea.Height);
                else
                    colors = clock.Music.Render(content.Music!, device.Geometry);

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

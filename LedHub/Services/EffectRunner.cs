using System.Diagnostics;
using System.Runtime.InteropServices;
using LedHub.Core;

namespace LedHub.Services;

/// <summary>
/// Текущие параметры эффекта; читаются на каждом кадре, поэтому их можно менять на лету.
/// </summary>
public sealed record EffectSettings(RgbColor Color, int Brightness, double Speed);

/// <summary>
/// Проигрывает источник света — эффект, экран или музыку, — отправляя кадры на ленты. Адресные ленты получают
/// цвет каждого светодиода, остальные — один цвет на всю ленту.
/// </summary>
public sealed class EffectRunner
{
    // Таймер срабатывает с небольшим разбросом — без запаса устройство с тем же интервалом пропускало бы каждый второй кадр
    private static readonly TimeSpan FrameTolerance = TimeSpan.FromMilliseconds(5);

    // Какая доля разницы гасится за 50 мс, когда клавиша на клавиатуре темнеет; при другой частоте шаг пересчитывается
    private const double FadePer50Ms = 0.6;

    /// <summary>
    /// Сколько кадров в секунду считают эффекты; меняется на ходу.
    /// </summary>
    public static int FramesPerSecond { get; set; } = 20;

    private CancellationTokenSource? _cts;
    private ILightSource? _source;

    /// <summary>
    /// Ленты, на которых сейчас что-то играет; пусто, если ничего не запущено.
    /// </summary>
    public IReadOnlyList<LedDevice> Devices { get; private set; } = Array.Empty<LedDevice>();

    public bool IsRunning => _cts != null;

    public void Start(ILightSource source, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings)
    {
        Stop();

        _source = source;
        Devices = devices;
        _cts = new CancellationTokenSource();
        _ = RunAsync(source, devices, getSettings, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        Devices = Array.Empty<LedDevice>();

        // Захват экрана и звука держит системные ресурсы — освобождаем их вместе с остановкой
        (_source as IDisposable)?.Dispose();
        _source = null;
    }

    private static async Task RunAsync(ILightSource source, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings, CancellationToken ct)
    {
        var pending = new Dictionary<LedDevice, Task>();
        var lastSent = new Dictionary<LedDevice, TimeSpan>();
        var smoothed = new Dictionary<LedDevice, RgbColor[]>();
        var clock = Stopwatch.StartNew();
        var phase = 0.0;
        var lastTick = 0.0;
        var interval = FrameIntervalOf(FramesPerSecond);

        using var timer = new PeriodicTimer(interval);

        // Обычный системный таймер Windows тикает раз в 15,6 мс — для 30 и 60 кадров нужен точный
        TimeBeginPeriod(1);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Частоту меняют в настройках, пока эффект играет
                interval = FrameIntervalOf(FramesPerSecond);
                if (timer.Period != interval)
                    timer.Period = interval;

                var settings = getSettings();

                // Фаза копится по шагам, чтобы смена скорости не вызывала скачок эффекта
                var now = clock.Elapsed.TotalSeconds;
                phase += (now - lastTick) * settings.Speed;
                lastTick = now;

                source.BeginFrame(phase, settings);

                foreach (var device in devices)
                {
                    // Медленное устройство пропускает кадры, а не копит очередь
                    if (pending.TryGetValue(device, out var previous) && !previous.IsCompleted)
                        continue;

                    // Устройству, которое не принимает кадры так часто, шлём свежий кадр в его момент, а не старый с задержкой
                    var elapsed = clock.Elapsed;
                    if (lastSent.TryGetValue(device, out var sent) && elapsed - sent < device.MinFrameInterval - FrameTolerance)
                        continue;

                    lastSent[device] = elapsed;

                    // Сглаживание считается по времени, поэтому от частоты кадров зависит доля, гасимая за кадр
                    var deviceInterval = device.MinFrameInterval > interval ? device.MinFrameInterval : interval;
                    var fade = 1 - Math.Pow(1 - FadePer50Ms, deviceInterval.TotalMilliseconds / 50);

                    pending[device] = SendFrameAsync(source, device, settings, smoothed, fade, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            TimeEndPeriod(1);
        }
    }

    private static TimeSpan FrameIntervalOf(int framesPerSecond)
    {
        return TimeSpan.FromSeconds(1.0 / Math.Clamp(framesPerSecond, 1, 60));
    }

    /// <summary>
    /// smoothed — последние отправленные цвета клавиатур; меняется только до первого await, то есть в потоке кадров.
    /// fade — какая доля разницы гасится за этот кадр, когда клавиша темнеет.
    /// </summary>
    private static async Task SendFrameAsync(ILightSource source, LedDevice device, EffectSettings settings,
        Dictionary<LedDevice, RgbColor[]> smoothed, double fade, CancellationToken ct)
    {
        try
        {
            if (device is IAddressableLedDevice { LedCount: > 0 } strip)
            {
                var colors = source.RenderStrip(device, strip.LedCount);

                // Клавиши видно вблизи, и на них заметна каждая мелкая просадка яркости между кадрами
                if (device is IKeyboardDevice)
                    colors = Smooth(device, colors, smoothed, fade);

                await strip.SetLedColorsAsync(colors, settings.Brightness, ct);
                return;
            }

            await device.SetColorAsync(source.RenderSolid(device), settings.Brightness, ct);
        }
        catch (Exception)
        {
            // Ошибка одного кадра не должна останавливать эффект — следующий кадр попробует снова
        }
    }

    /// <summary>
    /// Убирает дрожание: светлеет клавиша сразу, поэтому вспышки не запаздывают, а темнеет плавно, за несколько кадров.
    /// </summary>
    private static IReadOnlyList<RgbColor> Smooth(LedDevice device, IReadOnlyList<RgbColor> colors, Dictionary<LedDevice, RgbColor[]> smoothed,
        double fade)
    {
        if (!smoothed.TryGetValue(device, out var last) || last.Length != colors.Count)
        {
            smoothed[device] = colors.ToArray();
            return colors;
        }

        for (var i = 0; i < last.Length; i++)
            last[i] = new RgbColor(Fade(last[i].R, colors[i].R, fade), Fade(last[i].G, colors[i].G, fade), Fade(last[i].B, colors[i].B, fade));

        // Отдаём копию: массив меняется уже на следующем кадре
        return (RgbColor[])last.Clone();
    }

    private static byte Fade(byte from, byte to, double fade)
    {
        if (to >= from)
            return to;

        // Шаг не меньше единицы, иначе цвет навсегда застрял бы рядом с целью
        var step = Math.Max(1, (int)Math.Ceiling((from - to) * fade));

        return (byte)Math.Max(to, from - step);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}

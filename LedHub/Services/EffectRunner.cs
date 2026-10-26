using System.Diagnostics;
using LedHub.Core;

namespace LedHub.Services;

public enum EffectKind
{
    Rainbow,
    ColorCycle,
    Breathing,
    Wave,
    Comet,
    Twinkle,
    Fire,
    Police,
    Strobe,
    Candle
}

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
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private CancellationTokenSource? _cts;
    private ILightSource? _source;

    public void Start(ILightSource source, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings)
    {
        Stop();

        _source = source;
        _cts = new CancellationTokenSource();
        _ = RunAsync(source, devices, getSettings, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Захват экрана и звука держит системные ресурсы — освобождаем их вместе с остановкой
        (_source as IDisposable)?.Dispose();
        _source = null;
    }

    private static async Task RunAsync(ILightSource source, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings, CancellationToken ct)
    {
        var pending = new Dictionary<LedDevice, Task>();
        var clock = Stopwatch.StartNew();
        var phase = 0.0;
        var lastTick = 0.0;

        using var timer = new PeriodicTimer(FrameInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
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

                    pending[device] = SendFrameAsync(source, device, settings, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task SendFrameAsync(ILightSource source, LedDevice device, EffectSettings settings, CancellationToken ct)
    {
        try
        {
            if (device is IAddressableLedDevice { LedCount: > 0 } strip)
            {
                var colors = source.RenderStrip(device, strip.LedCount);

                await strip.SetLedColorsAsync(colors, settings.Brightness, ct);
                return;
            }

            await device.SetColorAsync(source.RenderSolid(), settings.Brightness, ct);
        }
        catch (Exception)
        {
            // Ошибка одного кадра не должна останавливать эффект — следующий кадр попробует снова
        }
    }
}

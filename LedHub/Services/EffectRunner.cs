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
/// Проигрывает световой эффект, отправляя кадры на ленты. Адресные ленты получают
/// цвет каждого светодиода, остальные — один цвет на всю ленту.
/// </summary>
public sealed class EffectRunner
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private CancellationTokenSource? _cts;

    public void Start(EffectKind kind, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings)
    {
        Stop();

        _cts = new CancellationTokenSource();
        _ = RunAsync(kind, devices, getSettings, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static async Task RunAsync(EffectKind kind, IReadOnlyList<LedDevice> devices, Func<EffectSettings> getSettings, CancellationToken ct)
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

                foreach (var device in devices)
                {
                    // Медленное устройство пропускает кадры, а не копит очередь
                    if (pending.TryGetValue(device, out var previous) && !previous.IsCompleted)
                        continue;

                    pending[device] = SendFrameAsync(kind, device, phase, settings, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task SendFrameAsync(EffectKind kind, LedDevice device, double phase, EffectSettings settings, CancellationToken ct)
    {
        try
        {
            if (device is IAddressableLedDevice { LedCount: > 0 } strip)
            {
                var colors = Enumerable.Range(0, strip.LedCount)
                    .Select(i => RenderPixel(kind, phase, settings.Color, i / (double)strip.LedCount))
                    .ToList();

                await strip.SetLedColorsAsync(colors, settings.Brightness, ct);
                return;
            }

            await device.SetColorAsync(RenderPixel(kind, phase, settings.Color, 0), settings.Brightness, ct);
        }
        catch (Exception)
        {
            // Ошибка одного кадра не должна останавливать эффект — следующий кадр попробует снова
        }
    }

    /// <summary>
    /// Цвет точки ленты в момент времени; position — положение на ленте от 0 до 1.
    /// Обычные ленты получают точку position = 0, поэтому «бегущие» эффекты на них пульсируют целиком.
    /// </summary>
    private static RgbColor RenderPixel(EffectKind kind, double phase, RgbColor color, double position)
    {
        switch (kind)
        {
            case EffectKind.Rainbow:
                return RgbColor.FromHsv(phase * 60 + position * 360, 1, 1);

            case EffectKind.ColorCycle:
                return RgbColor.FromHsv(phase * 45, 1, 1);

            case EffectKind.Wave:
                var wave = (1 + Math.Cos(2 * Math.PI * (position - phase / 4))) / 2;
                return color.Scale((int)Math.Round(8 + 92 * wave * wave));

            case EffectKind.Comet:
                // Расстояние позади «головы» кометы, которая пробегает ленту за три единицы фазы
                var behindHead = ((phase / 3 - position) % 1 + 1) % 1;
                var tail = behindHead < 0.3 ? Math.Pow(1 - behindHead / 0.3, 2) : 0;
                return color.Scale((int)Math.Round(100 * tail));

            case EffectKind.Twinkle:
                return Random.Shared.NextDouble() < 0.05 ? RgbColor.White : color.Scale(12);

            case EffectKind.Fire:
                var heat = Random.Shared.NextDouble();
                return RgbColor.FromHsv(8 + 30 * heat, 1, 0.45 + 0.55 * heat);

            case EffectKind.Police:
                var isLeftHalf = position < 0.5;
                var isSwapped = (int)(phase * 2) % 2 == 1;
                return isLeftHalf ^ isSwapped ? new RgbColor(255, 0, 0) : new RgbColor(0, 40, 255);

            case EffectKind.Breathing:
                var breath = (1 - Math.Cos(phase * Math.PI / 2)) / 2;
                return color.Scale((int)Math.Round(5 + 95 * breath));

            case EffectKind.Strobe:
                return phase * 4 % 1 < 0.5 ? color : new RgbColor(0, 0, 0);

            default:
                // Свеча: каждая точка независимо и неравномерно подрагивает
                return color.Scale(55 + Random.Shared.Next(46));
        }
    }
}

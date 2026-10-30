using System.Diagnostics;
using LightSpace.Core;
using LightSpace.Devices;

namespace LightSpace.Engine.Output;

/// <summary>
/// Вывод на одно устройство с почтовым ящиком на один кадр: новый кадр заменяет неотправленный, поэтому медленное
/// устройство пропускает кадры, а не копит очередь и не задерживает остальные. Кадры принимаются из любого потока.
/// </summary>
public sealed class DeviceOutput : IDisposable
{
    // Таймер срабатывает с небольшим разбросом — без запаса устройство с тем же интервалом пропускало бы каждый второй кадр
    private static readonly TimeSpan FrameTolerance = TimeSpan.FromMilliseconds(5);

    // Сколько остановка ждёт, пока уйдёт последняя команда
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMilliseconds(500);

    // Какая доля разницы гасится за 50 мс, когда клавиша на клавиатуре темнеет; при другой частоте шаг пересчитывается
    private const double FadePer50Ms = 0.6;

    private readonly LedDevice _device;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _worker;

    // Ящик, последний кадр и диагностика меняются только под _sync
    private OutputFrame? _pending;
    private bool _isPendingRepeat;
    private OutputFrame? _latest;
    private bool? _pendingPower;
    private bool _isEnabled;
    private long _sentFrames;
    private long _droppedFrames;
    private TimeSpan _lastSendDuration;
    private DateTime? _lastSentAt;
    private string? _lastError;
    private DateTime? _lastErrorAt;
    private bool _isFailing;

    // Эти поля трогает только поток отправки
    private TimeSpan? _lastFrameAt;
    private RgbColor[]? _smoothed;

    public DeviceOutput(LedDevice device, bool isEnabled)
    {
        _device = device;
        _isEnabled = isEnabled;

        // Отправка идёт не в потоке окна и не в потоке кадров: медленное устройство не задерживает ни то, ни другое
        var ct = _cts.Token;
        _worker = Task.Run(() => RunAsync(ct));
    }

    public LedDevice Device => _device;

    /// <summary>
    /// Включена ли подсветка устройства; выключенное устройство кадры запоминает, но не показывает.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            lock (_sync)
                return _isEnabled;
        }
    }

    /// <summary>
    /// Последний присланный кадр; null — кадров ещё не было.
    /// </summary>
    public OutputFrame? LatestFrame
    {
        get
        {
            lock (_sync)
                return _latest;
        }
    }

    public OutputDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
                return new OutputDiagnostics(_sentFrames, _droppedFrames, _lastSendDuration, _lastSentAt, _lastError, _lastErrorAt, _isFailing);
        }
    }

    /// <summary>
    /// Кладёт кадр в ящик; неотправленный кадр, который там лежал, пропадает.
    /// </summary>
    public void Submit(OutputFrame frame)
    {
        lock (_sync)
        {
            _latest = frame;

            if (!_isEnabled)
                return;

            // Повтор кадра для сглаживания — не потеря: его место просто занял более свежий кадр
            if (_pending != null && !_isPendingRepeat)
                _droppedFrames++;

            _pending = frame;
            _isPendingRepeat = false;
            Signal();
        }
    }

    /// <summary>
    /// Выключает подсветку устройства или включает её обратно с последним кадром.
    /// </summary>
    public void SetEnabled(bool isEnabled)
    {
        lock (_sync)
        {
            if (_isEnabled == isEnabled)
                return;

            _isEnabled = isEnabled;
            _pendingPower = isEnabled;

            // Выключение отменяет неотправленный кадр, включение возвращает последний
            _pending = isEnabled ? _latest : null;
            _isPendingRepeat = true;
            Signal();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _worker.Wait(StopTimeout);
        }
        catch (AggregateException)
        {
        }

        // Зависшая отправка может ещё держать токен и сигнал — тогда их освободит сборщик мусора
        if (!_worker.IsCompleted)
            return;

        _cts.Dispose();
        _signal.Dispose();
    }

    /// <summary>
    /// Будит поток отправки; вызывается под _sync, поэтому сигнал не переполняется.
    /// </summary>
    private void Signal()
    {
        if (_signal.CurrentCount == 0)
            _signal.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(ct);

                while (await SendNextAsync(ct))
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Отправляет одну команду из ящика; false — ящик пуст.
    /// </summary>
    private async Task<bool> SendNextAsync(CancellationToken ct)
    {
        bool? power;
        bool hasFrame;

        lock (_sync)
        {
            power = _pendingPower;
            _pendingPower = null;
            hasFrame = _pending != null;
        }

        if (power is { } isOn)
        {
            await RunCommandAsync(() => _device.SetPowerAsync(isOn, ct), isFrame: false, ct);
            return true;
        }

        if (!hasFrame)
            return false;

        // Устройству, которое не принимает кадры так часто, уходит свежий кадр в его момент: пока ждём, кадр в ящике может смениться
        if (_lastFrameAt is { } last)
        {
            var wait = _device.MinFrameInterval - FrameTolerance - (_clock.Elapsed - last);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
        }

        OutputFrame? frame;

        lock (_sync)
        {
            frame = _pending;
            _pending = null;
            _isPendingRepeat = false;
        }

        if (frame == null)
            return true;

        var now = _clock.Elapsed;
        var since = _lastFrameAt is { } previous ? now - previous : _device.MinFrameInterval;
        _lastFrameAt = now;

        await RunCommandAsync(() => SendFrameAsync(frame, since, ct), isFrame: true, ct);

        return true;
    }

    private Task SendFrameAsync(OutputFrame frame, TimeSpan since, CancellationToken ct)
    {
        if (_device is IAddressableLedDevice { LedCount: > 0 } strip)
        {
            if (frame.Leds is { } leds)
            {
                // Клавиши видно вблизи, и на них заметна каждая мелкая просадка яркости между кадрами
                if (_device is IKeyboardDevice)
                    leds = Smooth(frame, leds, since);

                return strip.SetLedColorsAsync(leds, frame.Brightness, ct);
            }

            // После сплошного цвета сглаживание начинается заново, а не от давно ушедшего кадра
            _smoothed = null;
        }

        return _device.SetColorAsync(frame.Color, frame.Brightness, ct);
    }

    /// <summary>
    /// Убирает дрожание: светлеет клавиша сразу, поэтому вспышки не запаздывают, а темнеет плавно, за несколько кадров.
    /// Сглаживание считается по настоящему времени с прошлого кадра: если кадры пропускались, клавиши всё равно гаснут вовремя.
    /// </summary>
    private IReadOnlyList<RgbColor> Smooth(OutputFrame frame, IReadOnlyList<RgbColor> leds, TimeSpan since)
    {
        if (_smoothed == null || _smoothed.Length != leds.Count)
        {
            _smoothed = leds.ToArray();
            return leds;
        }

        var fade = 1 - Math.Pow(1 - FadePer50Ms, Math.Min(since.TotalMilliseconds, 200) / 50);
        var isSettled = true;

        for (var i = 0; i < _smoothed.Length; i++)
        {
            var last = _smoothed[i];
            var target = leds[i];

            _smoothed[i] = new RgbColor(Fade(last.R, target.R, fade), Fade(last.G, target.G, fade), Fade(last.B, target.B, fade));

            if (_smoothed[i] != target)
                isSettled = false;
        }

        // Клавиши ещё не догасли: кадр остаётся в ящике и повторится, даже если новых кадров больше не будет
        if (!isSettled)
        {
            lock (_sync)
            {
                if (_isEnabled && _pending == null)
                {
                    _pending = frame;
                    _isPendingRepeat = true;
                }
            }
        }

        // Отдаём копию: массив меняется уже на следующем кадре
        return (RgbColor[])_smoothed.Clone();
    }

    private static byte Fade(byte from, byte to, double fade)
    {
        if (to >= from)
            return to;

        // Шаг не меньше единицы, иначе цвет навсегда застрял бы рядом с целью
        var step = Math.Max(1, (int)Math.Ceiling((from - to) * fade));

        return (byte)Math.Max(to, from - step);
    }

    /// <summary>
    /// Выполняет команду устройства и записывает в диагностику её время или ошибку.
    /// </summary>
    private async Task RunCommandAsync(Func<Task> command, bool isFrame, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            await command();

            var duration = Stopwatch.GetElapsedTime(started);

            lock (_sync)
            {
                if (isFrame)
                    _sentFrames++;

                _lastSendDuration = duration;
                _lastSentAt = DateTime.Now;
                _isFailing = false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Ошибка одной команды не останавливает вывод — следующий кадр попробует снова
            lock (_sync)
            {
                _lastError = e.Message;
                _lastErrorAt = DateTime.Now;
                _isFailing = true;
            }
        }
    }
}

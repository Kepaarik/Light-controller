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

    // Сколько повтор кадра для догасания клавиш ждёт свежего кадра, прежде чем уйти сам
    private static readonly TimeSpan RepeatGrace = TimeSpan.FromMilliseconds(15);

    // Сколько остановка ждёт, пока уйдёт последняя команда
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMilliseconds(500);

    // Через сколько повторяется запуск встроенного эффекта, который не дошёл до устройства
    private static readonly TimeSpan BuiltinRetryDelay = TimeSpan.FromSeconds(5);

    // Как часто повторяется кадр, который не меняется: устройство, потерявшее его, всё равно его получит
    private static readonly TimeSpan SameFrameResendInterval = TimeSpan.FromSeconds(2);

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
    private bool _isPendingReset;
    private bool _isEnabled;
    private long _sentFrames;
    private long _droppedFrames;
    private TimeSpan _lastSendDuration;
    private DateTime? _lastSentAt;
    private string? _lastError;
    private DateTime? _lastErrorAt;
    private bool _isFailing;

    // Встроенный эффект, который сейчас играет устройство, и его ещё не отправленный запуск; null — устройство показывает кадры
    private BuiltinEffectStart? _builtin;
    private BuiltinEffectStart? _pendingBuiltin;

    // Эти поля трогает только поток отправки
    private TimeSpan? _lastFrameAt;
    private RgbColor[]? _smoothed;

    // Последний кадр, который дошёл до устройства; такой же следующий не отправляется. null — отправлять обязательно
    private OutputFrame? _lastSent;

    // Клавиши ещё догасают: такой же кадр всё равно уходит, иначе они застыли бы на полпути
    private bool _isSmoothing;

    public DeviceOutput(LedDevice device, bool isEnabled)
    {
        _device = device;
        _isEnabled = isEnabled;

        // Устройство могли выключить с пульта или телефона: включение уйдёт раньше первого кадра и эффекта
        if (isEnabled && device.NeedsPowerOnAtStart)
        {
            _pendingPower = true;
            Signal();
            DeviceLog.Write($"«{device.Name}»: подключено — сначала отправляется включение");
        }

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

            // Кадр останавливает встроенный эффект: устройство снова показывает кадры
            _builtin = null;
            _pendingBuiltin = null;

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
    /// Запускает на устройстве встроенный эффект: устройство играет его само, пока не придёт следующий кадр.
    /// Тот же запуск, что уже играет, повторно не отправляется. Устройство должно уметь встроенные эффекты.
    /// </summary>
    public void StartBuiltin(BuiltinEffectStart start)
    {
        lock (_sync)
        {
            if (start == _builtin)
                return;

            DeviceLog.Write($"«{_device.Name}»: запрошен встроенный эффект {start.EffectId}, скорость {start.Speed}, "
                + $"яркость {start.Brightness}, цвет {start.Color?.ToHex() ?? "свой"}{(_isEnabled ? "" : " — подсветка выключена, не отправляется")}");

            // Неотправленный кадр затёр бы эффект
            _builtin = start;
            _pending = null;
            _isPendingRepeat = false;

            if (!_isEnabled)
                return;

            _pendingBuiltin = start;
            Signal();
        }
    }

    /// <summary>
    /// Эффект сменился: прежний полностью останавливается и стирается с устройства, прежде чем уйдёт первый кадр
    /// или запуск нового.
    /// </summary>
    public void Reset()
    {
        lock (_sync)
        {
            DeviceLog.Write($"«{_device.Name}»: эффект сменился — сброс устройства");

            // Неотправленный кадр и запуск относятся к прежнему эффекту; встроенный эффект после сброса запустится заново
            _pending = null;
            _isPendingRepeat = false;
            _builtin = null;
            _pendingBuiltin = null;

            if (!_isEnabled)
                return;

            _isPendingReset = true;
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

            DeviceLog.Write($"«{_device.Name}»: подсветка {(isEnabled ? "включена" : "выключена")}");

            _isEnabled = isEnabled;
            _pendingPower = isEnabled;

            // Выключение отменяет неотправленный кадр, включение возвращает последний
            _pending = isEnabled ? _latest : null;
            _isPendingRepeat = true;

            // Встроенный эффект после включения запускается заново вместо кадра; выключение отменяет его неотправленный запуск
            _pendingBuiltin = isEnabled ? _builtin : null;

            // Выключенное устройство гаснет само — сбрасывать его незачем
            if (!isEnabled)
                _isPendingReset = false;

            if (_builtin != null)
                _pending = null;

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
        bool isReset;
        bool hasFrame;
        BuiltinEffectStart? builtin;

        lock (_sync)
        {
            power = _pendingPower;
            _pendingPower = null;
            hasFrame = _pending != null;

            // Сброс уходит после включения, но раньше запуска и кадра нового эффекта
            isReset = power == null && _isPendingReset;

            if (isReset)
                _isPendingReset = false;

            // Запуск встроенного эффекта уходит после включения и сброса, а не вместо них
            builtin = power == null && !isReset ? _pendingBuiltin : null;

            if (builtin != null)
                _pendingBuiltin = null;
        }

        // После включения, сброса и встроенного эффекта на устройстве уже не последний кадр
        if (power != null || isReset || builtin != null)
            _lastSent = null;

        if (power is { } isOn)
        {
            await RunCommandAsync(() => _device.SetPowerAsync(isOn, ct), isFrame: false, ct);
            return true;
        }

        if (isReset)
        {
            // Клавиши нового эффекта не догасают из цветов прежнего
            _smoothed = null;

            await RunCommandAsync(() => _device.ResetAsync(ct), isFrame: false, ct);
            return true;
        }

        if (builtin != null)
        {
            // Кадров, которые повторили бы попытку, у встроенного эффекта нет — запуск повторяется сам
            if (!await RunCommandAsync(() => StartBuiltinAsync(builtin, ct), isFrame: false, ct))
            {
                DeviceLog.Write($"«{_device.Name}»: запуск встроенного эффекта не дошёл, повтор через {BuiltinRetryDelay.TotalSeconds:0} с");
                _ = RetryBuiltinAsync(builtin, ct);
            }

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

            // Повтор уступает свежему кадру: свежий приходит как раз к этому моменту, и если бы повтор ушёл раньше,
            // свежий ждал бы ещё целый интервал — анимация запаздывала бы и дёргалась
            if (IsRepeatPending())
            {
                // Сигнал от уже отправленного кадра не должен оборвать ожидание сразу
                _signal.Wait(0);

                if (IsRepeatPending())
                    await _signal.WaitAsync(RepeatGrace, ct);
            }
        }

        OutputFrame? frame;
        bool isRepeat;

        lock (_sync)
        {
            frame = _pending;
            isRepeat = _isPendingRepeat;
            _pending = null;
            _isPendingRepeat = false;
        }

        if (frame == null)
            return true;

        var now = _clock.Elapsed;

        // Неподвижный эффект не гоняет по проводу и радио одно и то же 20 раз в секунду
        if (!isRepeat && !_isSmoothing && _lastSent is { } sent && _lastFrameAt is { } sentAt
            && now - sentAt < SameFrameResendInterval && IsSameFrame(frame, sent))
            return true;

        var since = _lastFrameAt is { } previous ? now - previous : _device.MinFrameInterval;
        _lastFrameAt = now;

        // Не дошедший кадр следующий такой же повторит
        _lastSent = await RunCommandAsync(() => SendFrameAsync(frame, since, ct), isFrame: true, ct) ? frame : null;

        return true;
    }

    /// <summary>
    /// Кадры зажигают устройство одинаково.
    /// </summary>
    private static bool IsSameFrame(OutputFrame a, OutputFrame b)
    {
        if (a.Brightness != b.Brightness || a.Color != b.Color)
            return false;

        if (a.Leds == null || b.Leds == null)
            return a.Leds == null && b.Leds == null;

        return a.Leds.SequenceEqual(b.Leds);
    }

    /// <summary>
    /// В ящике лежит повтор последнего кадра для догасания клавиш, а не свежий кадр.
    /// </summary>
    private bool IsRepeatPending()
    {
        lock (_sync)
            return _pending != null && _isPendingRepeat;
    }

    private Task StartBuiltinAsync(BuiltinEffectStart start, CancellationToken ct)
    {
        if (_device is not IBuiltinEffectsDevice builtin)
        {
            DeviceLog.Write($"«{_device.Name}»: устройство не умеет встроенные эффекты — запуск пропущен");
            return Task.CompletedTask;
        }

        DeviceLog.Write($"«{_device.Name}»: отправка встроенного эффекта {start.EffectId}");

        // После эффекта клавиши сглаживаются заново, а не от кадра, который был до него
        _smoothed = null;

        return builtin.StartBuiltinEffectAsync(start.EffectId, start.Speed, start.Brightness, start.Color, ct);
    }

    /// <summary>
    /// Через несколько секунд снова кладёт в ящик запуск встроенного эффекта, если устройство всё ещё должно его играть.
    /// </summary>
    private async Task RetryBuiltinAsync(BuiltinEffectStart start, CancellationToken ct)
    {
        try
        {
            await Task.Delay(BuiltinRetryDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            // За это время могли прийти кадр, другой эффект или выключение подсветки
            if (ct.IsCancellationRequested || !Equals(_builtin, start) || !_isEnabled || _pendingBuiltin != null)
                return;

            _pendingBuiltin = start;
            Signal();
        }
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
            _isSmoothing = false;
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
            _isSmoothing = false;
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

        _isSmoothing = !isSettled;

        // Клавиши ещё не догасли: кадр остаётся в ящике и повторится, даже если новых кадров больше не будет
        if (!isSettled)
        {
            lock (_sync)
            {
                if (_isEnabled && _pending == null && _builtin == null)
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
    /// Выполняет команду устройства и записывает в диагностику её время или ошибку. false — команда не дошла.
    /// </summary>
    private async Task<bool> RunCommandAsync(Func<Task> command, bool isFrame, CancellationToken ct)
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

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            DeviceLog.Write($"«{_device.Name}»: {(isFrame ? "кадр" : "команда")} не дошла: {e.GetType().Name}: {e.Message}");

            // Ошибка одной команды не останавливает вывод — следующий кадр попробует снова
            lock (_sync)
            {
                _lastError = e.Message;
                _lastErrorAt = DateTime.Now;
                _isFailing = true;
            }

            return false;
        }
    }
}

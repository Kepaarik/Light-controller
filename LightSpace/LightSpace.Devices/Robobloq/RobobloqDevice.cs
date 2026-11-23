using System.Diagnostics;
using HidSharp;
using LightSpace.Core;

namespace LightSpace.Devices.Robobloq;

/// <summary>
/// Подсветка монитора Robobloq (приложение SyncLight), подключённая по USB HID.
/// Формат команд соответствует драйверу Robobloq Monitor Light Strip из OpenRGB.
/// </summary>
public sealed class RobobloqDevice : LedDevice, IAddressableLedDevice, IBuiltinEffectsDevice
{
    public const int VendorId = 0x1A86;
    public const int ProductId = 0xFE07;

    // Отчёт: байт идентификатора (0) и 64 байта данных
    private const int ReportLength = 65;
    private const int ReplyAttempts = 3;

    // Столько диапазонов цветов за кадр отправляет официальное приложение SyncLight
    private const int MaxColorRanges = 34;

    private const byte CommandSetSyncScreen = 0x80;
    private const byte CommandReadDeviceInfo = 0x82;
    private const byte CommandSetEffect = 0x85;
    private const byte CommandSetColor = 0x86;
    private const byte CommandSetBrightness = 0x87;
    private const byte CommandSetEffectSpeed = 0x8A;
    private const byte CommandTurnOff = 0x97;

    private const byte EffectCategoryDynamic = 0x02;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(1);

    // Пауза между отчётами: команды, пришедшие вплотную друг к другу, контроллер ленты может потерять.
    // Столько же, сколько между отчётами одного кадра: за кадром уходят ещё и повторы для проталкивания
    private static readonly TimeSpan ReportGap = TimeSpan.FromMilliseconds(1);

    // Лента 24" (прошивка 1.7.3) выполняет команду, только когда за ней придут ещё около семи: новый цвет при
    // повторе раз в 2 секунды доходил через ~15 секунд. Столько повторов с запасом проталкивают последнюю команду сразу
    private const int PushCount = 8;

    private static readonly IReadOnlyList<BuiltinEffect> DynamicEffects = new BuiltinEffect[]
    {
        new(0x00, "Радужная волна"),
        new(0x01, "Дыхание"),
        new(0x02, "Вращение"),
        new(0x03, "Ритм"),
        new(0x04, "Вихрь"),
        new(0x05, "Лимон"),
        new(0x06, "Электро")
    };

    private readonly HidStream _stream;
    private readonly string _devicePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private byte _packetIndex = 0x02;
    private byte _ledCount;
    private byte _screenSize;
    private string? _serial;
    private string? _firmware;
    private byte? _sentBrightness;

    // Когда ушёл последний отчёт (отметка Stopwatch); меняется только под _lock
    private long _lastWriteAt;

    // Встроенный эффект и скорость, которые сейчас играет лента; null — она показывает цвет, кадры или выключена
    private (int EffectId, int Speed)? _playingBuiltin;

    // Отдельной команды включения у ленты нет: включение — это повторная отправка последнего цвета
    private RgbColor _lastColor = RgbColor.White;
    private int _lastBrightness = 100;

    private RobobloqDevice(HidStream stream, string devicePath)
    {
        _stream = stream;
        _devicePath = devicePath;
    }

    public override string Id => _serial != null ? $"robobloq:{_serial}" : $"robobloq:{_devicePath}";

    public override string Name => _serial != null
        ? $"Robobloq {_screenSize}\" ({_serial[^4..]})"
        : "Robobloq";

    public override string Protocol => "USB · Robobloq";

    public override string Address => _firmware != null ? $"прошивка {_firmware}" : "USB HID";

    public override bool IsReady => _ledCount > 0;

    public int LedCount => _ledCount;

    public IReadOnlyList<BuiltinEffect> BuiltinEffects => DynamicEffects;

    public bool HasBuiltinColor => false;

    public override string? StatusText => IsReady
        ? null
        : "Не удалось прочитать параметры ленты. Закройте SyncLight и повторите поиск";

    /// <summary>
    /// Открывает ленту и запрашивает её параметры; null, если устройство открыть не удалось.
    /// </summary>
    public static RobobloqDevice? Connect(HidDevice hidDevice)
    {
        if (!hidDevice.TryOpen(out var stream))
            return null;

        stream.ReadTimeout = (int)ReplyTimeout.TotalMilliseconds;
        stream.WriteTimeout = 1000;

        var device = new RobobloqDevice(stream, hidDevice.DevicePath);
        device.TryReadDeviceInfo();

        return device;
    }

    public override async Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        // После включения эффект запускается заново целиком
        _playingBuiltin = null;

        if (isOn)
        {
            await SetColorAsync(_lastColor, _lastBrightness, ct);
            await PushAsync(ct);
            return;
        }

        // Лента не выключается полностью: останавливаем её эффекты и гасим все светодиоды
        await SendAsync(new[] { CommandTurnOff }, ct);
        await SendColorAsync(new RgbColor(0, 0, 0), ct);

        // Чёрный кадр гасит ленту так же, как нулевая яркость: лента, которая показывала кадры, слушается именно их
        await SendBlackFrameAsync(ct);

        // Некоторые ленты (прошивка 1.7.3) выполняют команду, только когда приходит следующая, а после выключения
        // следующей нет — повтор чёрного цвета проталкивает предыдущий; ленте, которая уже погасла, он ничего не меняет
        await SendColorAsync(new RgbColor(0, 0, 0), ct);
        await PushAsync(ct);
    }

    public override async Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        // Цвет останавливает встроенный эффект
        _playingBuiltin = null;
        _lastColor = color;
        _lastBrightness = brightness;

        await SendColorAsync(color, ct);
        await SendBrightnessAsync(brightness, ct);
    }

    public async Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        // Кадр останавливает встроенный эффект
        _playingBuiltin = null;

        var ranges = MergeRanges(colors, MaxColorRanges);
        var payload = new byte[ranges.Count * 5];

        // Каждый диапазон — пять байт: первый светодиод (с 1), R, G, B, последний светодиод
        for (var i = 0; i < ranges.Count; i++)
        {
            var (start, end, color) = ranges[i];
            var at = i * 5;

            payload[at] = (byte)start;
            payload[at + 1] = color.R;
            payload[at + 2] = color.G;
            payload[at + 3] = color.B;
            payload[at + 4] = (byte)end;
        }

        // Кадр уходит одной пачкой: ни одна другая команда не встаёт между ним и его повторами
        await WriteLockedAsync(() =>
        {
            WriteMultiReport(CommandSetSyncScreen, payload);
            WriteBrightness(brightness);

            // Отстающая лента показывает кадр сразу, а не когда за ним придут следующие
            WritePush();
        }, ct);
    }

    /// <summary>
    /// Лента играет эффект своими цветами: меняются скорость и яркость. Если тот же эффект с той же скоростью уже играет,
    /// меняется только яркость — эффект не начинается заново.
    /// </summary>
    public async Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default)
    {
        if (_playingBuiltin != (effectId, speed))
        {
            await SendAsync(new[] { CommandSetEffect, EffectCategoryDynamic, (byte)effectId }, ct);

            // Лента ждёт обратную шкалу: 0 — быстро, 100 — медленно
            await SendAsync(new[] { CommandSetEffectSpeed, (byte)(100 - Math.Clamp(speed, 0, 100)) }, ct);

            _playingBuiltin = (effectId, speed);
        }

        await SendBrightnessAsync(brightness, ct);

        // За встроенным эффектом кадры не идут — отстающей ленте последнюю команду проталкиваем сами
        await PushAsync(ct);
    }

    /// <summary>
    /// Эффект ленты останавливается, все светодиоды гаснут; следующий цвет или кадр включает ленту снова.
    /// </summary>
    public override async Task ResetAsync(CancellationToken ct = default)
    {
        _playingBuiltin = null;

        await SendAsync(new[] { CommandTurnOff }, ct);
        await SendColorAsync(new RgbColor(0, 0, 0), ct);
    }

    public override void Dispose()
    {
        _stream.Dispose();
        _lock.Dispose();
    }

    private Task SendColorAsync(RgbColor color, CancellationToken ct)
    {
        // Цвет задаётся диапазонами (начало, R, G, B, конец); второй, пустой диапазон повторяет поведение SyncLight
        var command = new byte[]
        {
            CommandSetColor,
            0x01, color.R, color.G, color.B, _ledCount,
            (byte)(_ledCount + 1), 0x00, 0x00, 0x00, 0xFE
        };

        return SendAsync(command, ct);
    }

    private Task SendBrightnessAsync(int brightness, CancellationToken ct)
    {
        return WriteLockedAsync(() => WriteBrightness(brightness), ct);
    }

    /// <summary>
    /// Отправляет яркость, если на ленте другая; вызывается под _lock.
    /// </summary>
    private void WriteBrightness(int brightness)
    {
        // Яркость ленты 1–255; ноль лента воспринимает как максимум
        var level = (byte)Math.Max(1, Math.Round(Math.Clamp(brightness, 0, 100) * 255 / 100.0));

        // Эффекты шлют кадры часто, а яркость меняется редко — не повторяем одну и ту же команду
        if (level == _sentBrightness)
            return;

        WriteCommand(new[] { CommandSetBrightness, level });
        _sentBrightness = level;
    }

    /// <summary>
    /// Повторяет яркость, которая уже на ленте, <see cref="PushCount"/> раз. Лента, выполняющая команду только с
    /// приходом следующих, выполняет всё, что отправлено до повторов; ленте без задержки повторы ничего не меняют.
    /// </summary>
    private Task PushAsync(CancellationToken ct)
    {
        return WriteLockedAsync(WritePush, ct);
    }

    /// <summary>
    /// Повторы яркости для проталкивания; вызывается под _lock.
    /// </summary>
    private void WritePush()
    {
        if (_sentBrightness is not { } level)
            return;

        var command = new[] { CommandSetBrightness, level };
        var started = Stopwatch.GetTimestamp();

        for (var i = 0; i < PushCount; i++)
        {
            WaitReportGap();
            _stream.Write(BuildReport(command));
            _lastWriteAt = Stopwatch.GetTimestamp();
        }

        // Повторы уходят за каждым кадром — в журнал одна строка на все
        if (DeviceLog.IsEnabled)
            DeviceLog.Write($"Robobloq «{Name}» → повтор яркости {level} ×{PushCount}, "
                + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} мс");
    }

    /// <summary>
    /// Все светодиоды ленты гаснут командой кадра.
    /// </summary>
    private Task SendBlackFrameAsync(CancellationToken ct)
    {
        if (_ledCount == 0)
            return Task.CompletedTask;

        var payload = new byte[] { 0x01, 0x00, 0x00, 0x00, _ledCount };

        return WriteLockedAsync(() => WriteMultiReport(CommandSetSyncScreen, payload), ct);
    }

    /// <summary>
    /// Отправляет длинную команду несколькими отчётами: заголовок "SC", двухбайтовая длина,
    /// номер пакета, команда, данные, контрольная сумма, дополнение нулями до кратного 64. Вызывается под _lock.
    /// </summary>
    private void WriteMultiReport(byte command, byte[] payload)
    {
        var length = payload.Length + 7;

        // Значимая часть команды для журнала — до дополнения нулями
        var meaningful = length;

        // Массив сразу кратен 64: хвост остаётся нулями
        var data = new byte[(length + 63) / 64 * 64];

        data[0] = 0x53;
        data[1] = 0x43;
        data[2] = (byte)(length >> 8);
        data[3] = (byte)length;
        data[4] = _packetIndex++;
        data[5] = command;
        payload.CopyTo(data, 6);

        var checksum = 0;

        for (var i = 0; i < length - 1; i++)
            checksum += data[i];

        data[length - 1] = (byte)checksum;

        WaitReportGap();

        var started = Stopwatch.GetTimestamp();

        for (var offset = 0; offset < data.Length; offset += 64)
        {
            var report = new byte[ReportLength];
            Array.Copy(data, offset, report, 1, 64);
            _stream.Write(report);

            // Пауза между отчётами, чтобы не перегружать контроллер ленты
            Thread.Sleep(1);
        }

        _lastWriteAt = Stopwatch.GetTimestamp();

        if (DeviceLog.IsEnabled)
            DeviceLog.Write($"Robobloq «{Name}» → {DeviceLog.Hex(data[..meaningful])} ({data.Length / 64} отч.), "
                + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} мс");
    }

    /// <summary>
    /// Сворачивает цвета светодиодов в диапазоны одинакового цвета. Если диапазонов больше,
    /// чем принимает лента, сливает самые похожие соседние диапазоны.
    /// </summary>
    private static List<(int Start, int End, RgbColor Color)> MergeRanges(IReadOnlyList<RgbColor> colors, int maxRanges)
    {
        var ranges = new List<(int Start, int End, RgbColor Color)>();

        for (var i = 0; i < colors.Count; i++)
        {
            if (ranges.Count > 0 && ranges[^1].Color == colors[i])
                ranges[^1] = (ranges[^1].Start, i + 1, colors[i]);
            else
                ranges.Add((i + 1, i + 1, colors[i]));
        }

        while (ranges.Count > maxRanges)
        {
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < ranges.Count - 1; i++)
            {
                var distance = ColorDistance(ranges[i].Color, ranges[i + 1].Color);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            var left = ranges[best];
            var right = ranges[best + 1];
            var leftLength = left.End - left.Start + 1;
            var rightLength = right.End - right.Start + 1;
            var total = leftLength + rightLength;

            var merged = new RgbColor(
                (byte)((left.Color.R * leftLength + right.Color.R * rightLength) / total),
                (byte)((left.Color.G * leftLength + right.Color.G * rightLength) / total),
                (byte)((left.Color.B * leftLength + right.Color.B * rightLength) / total));

            ranges[best] = (left.Start, right.End, merged);
            ranges.RemoveAt(best + 1);
        }

        return ranges;
    }

    private static int ColorDistance(RgbColor a, RgbColor b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;

        return dr * dr + dg * dg + db * db;
    }

    private Task SendAsync(byte[] command, CancellationToken ct)
    {
        return WriteLockedAsync(() => WriteCommand(command), ct);
    }

    /// <summary>
    /// Выполняет запись в ленту вне вызывающего потока и под _lock: всё, что пишет write, уходит подряд.
    /// </summary>
    private async Task WriteLockedAsync(Action write, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(write, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Отправляет короткую команду одним отчётом; вызывается под _lock.
    /// </summary>
    private void WriteCommand(byte[] command)
    {
        WaitReportGap();

        var report = BuildReport(command);
        var started = Stopwatch.GetTimestamp();

        _stream.Write(report);
        _lastWriteAt = Stopwatch.GetTimestamp();

        // Значимая часть отчёта: заголовок, команда и контрольная сумма, без дополнения нулями
        if (DeviceLog.IsEnabled)
            DeviceLog.Write($"Robobloq «{Name}» → {DeviceLog.Hex(report[1..(command.Length + 6)])}, "
                + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} мс");
    }

    /// <summary>
    /// Ждёт, пока с прошлого отчёта пройдёт пауза <see cref="ReportGap"/>; вызывается под _lock.
    /// </summary>
    private void WaitReportGap()
    {
        var wait = ReportGap - Stopwatch.GetElapsedTime(_lastWriteAt);

        if (wait > TimeSpan.Zero)
            Thread.Sleep(wait);
    }

    /// <summary>
    /// Собирает отчёт: "RB", длина, номер пакета, команда, контрольная сумма (сумма байтов), дополнение нулями.
    /// </summary>
    private byte[] BuildReport(byte[] command)
    {
        var report = new byte[ReportLength];
        var length = command.Length + 5;

        report[1] = 0x52;
        report[2] = 0x42;
        report[3] = (byte)length;
        report[4] = _packetIndex++;
        command.CopyTo(report, 5);

        var checksum = 0;
        for (var i = 1; i < length; i++)
            checksum += report[i];

        report[length] = (byte)checksum;

        return report;
    }

    /// <summary>
    /// Параметры ленты из её ответа на запрос параметров (с байтом идентификатора отчёта в начале);
    /// null — это ответ на другую команду или не ответ ленты. Номер пакета не важен: ответ мог прийти на прошлый запрос.
    /// </summary>
    public static RobobloqDeviceInfo? ParseDeviceInfo(IReadOnlyList<byte> report)
    {
        if (report.Count < 25 || report[1] != 0x52 || report[2] != 0x42 || report[5] != CommandReadDeviceInfo)
            return null;

        var serial = string.Concat(Enumerable.Range(13, 8).Select(i => report[i].ToString("x2")));

        return new RobobloqDeviceInfo(report[9], report[12], serial, $"{report[22]}.{report[23]}.{report[24]}");
    }

    /// <summary>
    /// Запрашивает число светодиодов, размер экрана, серийный номер и версию прошивки.
    /// </summary>
    private bool TryReadDeviceInfo()
    {
        var buffer = new byte[ReportLength];

        for (var attempt = 0; attempt < ReplyAttempts; attempt++)
        {
            var expectedIndex = _packetIndex;

            // Ответы, которые пришли, но не на этот запрос: чужие пакеты или ответы на старые команды
            var foreign = 0;

            try
            {
                _stream.Write(BuildReport(new[] { CommandReadDeviceInfo }));

                // Во входном буфере могут лежать ответы на старые команды — ищем ответ именно на запрос параметров
                var deadline = DateTime.UtcNow + ReplyTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    var read = _stream.Read(buffer, 0, buffer.Length);

                    // buffer[0] — идентификатор отчёта, дальше ответ в том же формате, что и команда
                    if (ParseDeviceInfo(buffer[..read]) is not { } info)
                    {
                        foreign++;
                        DeviceLog.Write($"Robobloq {_devicePath}: попытка {attempt + 1}, ответ не на запрос параметров "
                            + $"(прочитано {read} байт): {DeviceLog.Hex(buffer[..read])}");
                        continue;
                    }

                    _screenSize = info.ScreenSize;
                    _ledCount = info.LedCount;
                    _serial = info.Serial;
                    _firmware = info.Firmware;

                    // Некоторые ленты отвечают с опозданием на один запрос: ответ на прошлую попытку подходит так же
                    if (buffer[4] != expectedIndex)
                        DeviceLog.Write($"Robobloq {_devicePath}: ответ на параметры пришёл на прошлый запрос (пакет {buffer[4]}, ждали {expectedIndex})");

                    DeviceLog.Write($"Robobloq {_devicePath}: параметры прочитаны с попытки {attempt + 1} — светодиодов {_ledCount}, "
                        + $"экран {_screenSize}\", серийный номер {_serial}, прошивка {_firmware}"
                        + (_ledCount == 0 ? " — лента сообщила 0 светодиодов, управлять нечем" : ""));

                    return true;
                }

                DeviceLog.Write($"Robobloq {_devicePath}: попытка {attempt + 1}, ответа на запрос параметров нет за "
                    + $"{ReplyTimeout.TotalSeconds:0} с (чужих ответов: {foreign})");
            }
            catch (TimeoutException)
            {
                // Лента иногда не отвечает с первого раза — повторяем запрос
                DeviceLog.Write($"Robobloq {_devicePath}: попытка {attempt + 1}, лента не ответила за {ReplyTimeout.TotalSeconds:0} с "
                    + $"(чужих ответов: {foreign})");
            }
            catch (IOException e)
            {
                DeviceLog.Write($"Robobloq {_devicePath}: попытка {attempt + 1}, ошибка ввода-вывода — {e.Message}");
                return false;
            }
        }

        DeviceLog.Write($"Robobloq {_devicePath}: параметры не прочитаны за {ReplyAttempts} попытки");

        return false;
    }
}

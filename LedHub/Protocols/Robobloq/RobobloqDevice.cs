using System.IO;
using HidSharp;
using LedHub.Core;

namespace LedHub.Protocols.Robobloq;

/// <summary>
/// Подсветка монитора Robobloq (приложение SyncLight), подключённая по USB HID.
/// Формат команд соответствует драйверу Robobloq Monitor Light Strip из OpenRGB.
/// </summary>
public sealed class RobobloqDevice : LedDevice, IAddressableLedDevice, IHardwareEffectsDevice
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

    private static readonly IReadOnlyList<HardwareEffect> DynamicEffects = new HardwareEffect[]
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

    public IReadOnlyList<HardwareEffect> HardwareEffects => DynamicEffects;

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
        if (isOn)
        {
            await SetColorAsync(_lastColor, _lastBrightness, ct);
            return;
        }

        // Лента не выключается полностью: останавливаем её эффекты и гасим все светодиоды
        await SendAsync(new[] { CommandTurnOff }, ct);
        await SendColorAsync(new RgbColor(0, 0, 0), ct);
    }

    public override async Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        _lastColor = color;
        _lastBrightness = brightness;

        await SendColorAsync(color, ct);
        await SendBrightnessAsync(brightness, ct);
    }

    public async Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        // Каждый диапазон — пять байт: первый светодиод (с 1), R, G, B, последний светодиод
        var payload = MergeRanges(colors, MaxColorRanges)
            .SelectMany(r => new[] { (byte)r.Start, r.Color.R, r.Color.G, r.Color.B, (byte)r.End })
            .ToArray();

        await SendMultiReportAsync(CommandSetSyncScreen, payload, ct);
        await SendBrightnessAsync(brightness, ct);
    }

    public async Task StartHardwareEffectAsync(int effectId, int speed, CancellationToken ct = default)
    {
        await SendAsync(new[] { CommandSetEffect, EffectCategoryDynamic, (byte)effectId }, ct);

        // Лента ждёт обратную шкалу: 0 — быстро, 100 — медленно
        await SendAsync(new[] { CommandSetEffectSpeed, (byte)(100 - Math.Clamp(speed, 0, 100)) }, ct);
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

    private async Task SendBrightnessAsync(int brightness, CancellationToken ct)
    {
        // Яркость ленты 1–255; ноль лента воспринимает как максимум
        var level = (byte)Math.Max(1, Math.Round(Math.Clamp(brightness, 0, 100) * 255 / 100.0));

        // Эффекты шлют кадры часто, а яркость меняется редко — не повторяем одну и ту же команду
        if (level == _sentBrightness)
            return;

        await SendAsync(new[] { CommandSetBrightness, level }, ct);
        _sentBrightness = level;
    }

    /// <summary>
    /// Отправляет длинную команду несколькими отчётами: заголовок "SC", двухбайтовая длина,
    /// номер пакета, команда, данные, контрольная сумма, дополнение нулями до кратного 64.
    /// </summary>
    private async Task SendMultiReportAsync(byte command, byte[] payload, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var length = payload.Length + 7;
            var data = new List<byte> { 0x53, 0x43, (byte)(length >> 8), (byte)length, _packetIndex++, command };
            data.AddRange(payload);
            data.Add((byte)data.Sum(b => (int)b));

            while (data.Count % 64 != 0)
                data.Add(0x00);

            await Task.Run(() =>
            {
                for (var offset = 0; offset < data.Count; offset += 64)
                {
                    var report = new byte[ReportLength];
                    data.CopyTo(offset, report, 1, 64);
                    _stream.Write(report);

                    // Пауза между отчётами, чтобы не перегружать контроллер ленты
                    Thread.Sleep(1);
                }
            }, ct);
        }
        finally
        {
            _lock.Release();
        }
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

    private async Task SendAsync(byte[] command, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(() => _stream.Write(BuildReport(command)), ct);
        }
        finally
        {
            _lock.Release();
        }
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
    /// Запрашивает число светодиодов, размер экрана, серийный номер и версию прошивки.
    /// </summary>
    private bool TryReadDeviceInfo()
    {
        var buffer = new byte[ReportLength];

        for (var attempt = 0; attempt < ReplyAttempts; attempt++)
        {
            var expectedIndex = _packetIndex;

            try
            {
                _stream.Write(BuildReport(new[] { CommandReadDeviceInfo }));

                // Во входном буфере могут лежать ответы на старые команды — ищем ответ именно на наш пакет
                var deadline = DateTime.UtcNow + ReplyTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    var read = _stream.Read(buffer, 0, buffer.Length);

                    // buffer[0] — идентификатор отчёта, дальше ответ в том же формате, что и команда
                    if (read < 25 || buffer[1] != 0x52 || buffer[2] != 0x42 || buffer[4] != expectedIndex)
                        continue;

                    _screenSize = buffer[9];
                    _ledCount = buffer[12];
                    _serial = Convert.ToHexString(buffer, 13, 8).ToLowerInvariant();
                    _firmware = $"{buffer[22]}.{buffer[23]}.{buffer[24]}";

                    return true;
                }
            }
            catch (TimeoutException)
            {
                // Лента иногда не отвечает с первого раза — повторяем запрос
            }
            catch (IOException)
            {
                return false;
            }
        }

        return false;
    }
}

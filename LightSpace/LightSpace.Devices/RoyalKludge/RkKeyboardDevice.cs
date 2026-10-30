using System.Diagnostics;
using HidSharp;
using LightSpace.Core;

namespace LightSpace.Devices.RoyalKludge;

/// <summary>
/// Клавиатура Royal Kludge S98, подключённая проводом: каждая клавиша — отдельный светодиод.
/// Формат команд соответствует веб-настройщику RK (drive.rkgaming.com).
/// </summary>
public sealed class RkKeyboardDevice : LedDevice, IKeyboardDevice
{
    public const int VendorId = 0x258A;

    /// <summary>
    /// Проводные S98 разных раскладок: международная, POR, TH, DE, ES, JP, RU, TC, KR, FR.
    /// Приёмник 2,4 ГГц общается по другому протоколу и сюда не входит.
    /// </summary>
    public static readonly IReadOnlyList<int> ProductIds = new[] { 431, 542, 547, 548, 555, 559, 560, 575, 576, 577 };

    private const byte ReportId = 9;

    // Пакет без байта идентификатора отчёта
    private const int PacketLength = 519;

    // Заголовок пакета: команда, два байта параметров, признак, номер части, длина данных; данные — с седьмого байта.
    // В отчёте перед пакетом стоит идентификатор, поэтому всё сдвинуто на один байт
    private const int DataOffset = 8;

    private const byte CommandGetFirmware = 130;
    private const byte CommandGetProfile = 132;
    private const byte CommandSetProfile = 4;
    private const byte CommandSetLedColors = 6;

    private const int FirmwareLength = 10;
    private const int ProfileLength = 128;
    private const int ProfileLedModeSelection = 9;
    private const int ProfileLedMode = 10;
    private const byte LedModeCustom = 18;

    // Настройщик RK по умолчанию работает с первым профилем клавиатуры
    private const byte ProfileIndex = 0;

    private const int KeyLedCount = 126;

    // Клавиатура по проводу принимает до 60 кадров в секунду — сколько выбрано в настройках эффектов
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 60);

    private readonly HidStream _stream;
    private readonly string _devicePath;
    private readonly string? _serial;
    private readonly int _reportLength;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stopwatch _sinceFrame = Stopwatch.StartNew();

    private string? _firmware;
    private byte[]? _profile;
    private string? _readError;
    private bool _isCustomMode;
    private byte[]? _lastFrame;

    // Отдельной команды включения у клавиатуры нет: включение — это повторная отправка последнего цвета
    private RgbColor _lastColor = RgbColor.White;
    private int _lastBrightness = 100;

    private RkKeyboardDevice(HidStream stream, string devicePath, string? serial, int reportLength)
    {
        _stream = stream;
        _devicePath = devicePath;
        _serial = serial;
        _reportLength = reportLength;
    }

    public override string Id => _serial != null ? $"rk:{_serial}" : $"rk:{_devicePath}";

    public override string Name => "RK S98";

    public override string Protocol => "USB · Royal Kludge";

    public override string Address => _firmware != null ? $"прошивка {_firmware}" : "USB HID";

    public override bool IsReady => _profile != null;

    public override string? StatusText => IsReady
        ? null
        : $"Не удалось прочитать настройки клавиатуры ({_readError}). Закройте программу RK и повторите поиск";

    public override TimeSpan MinFrameInterval => FrameInterval;

    public int LedCount => KeyLedCount;

    public IReadOnlyList<KeyboardKey> Keys => RkS98Layout.Keys;

    /// <summary>
    /// Принимает ли интерфейс команды настройщика: в его описании есть feature-отчёт 9 нужной длины.
    /// </summary>
    public static bool IsCommandInterface(HidDevice hidDevice)
    {
        try
        {
            return hidDevice.GetReportDescriptor().FeatureReports
                .Any(report => report.ReportID == ReportId && report.Length >= PacketLength + 1);
        }
        catch (Exception)
        {
            // Описание некоторых интерфейсов Windows не отдаёт — такие интерфейсы нам не подходят
            return false;
        }
    }

    /// <summary>
    /// Открывает клавиатуру и читает её настройки подсветки; null, если это не управляющий интерфейс или его не удалось открыть.
    /// </summary>
    public static RkKeyboardDevice? Connect(HidDevice hidDevice)
    {
        var reportLength = hidDevice.GetMaxFeatureReportLength();

        if (reportLength < PacketLength + 1 || !hidDevice.TryOpen(out var stream))
            return null;

        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;

        var device = new RkKeyboardDevice(stream, hidDevice.DevicePath, ReadSerial(hidDevice), reportLength);
        device.TryReadSettings();

        return device;
    }

    public override async Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        if (isOn)
        {
            await SetColorAsync(_lastColor, _lastBrightness, ct);
            return;
        }

        await SendColorsAsync(Enumerable.Repeat(new RgbColor(0, 0, 0), KeyLedCount).ToList(), 0, ct);
    }

    public override async Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        _lastColor = color;
        _lastBrightness = brightness;

        await SendColorsAsync(Enumerable.Repeat(color, KeyLedCount).ToList(), brightness, ct);
    }

    public Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        return SendColorsAsync(colors, brightness, ct);
    }

    public override void Dispose()
    {
        _stream.Dispose();
        _lock.Dispose();
    }

    private async Task SendColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct)
    {
        if (_profile == null)
            return;

        // Яркость подсветки клавиатуры живёт в её профиле, поэтому яркость приложения применяем к самим цветам
        var scale = Math.Clamp(brightness, 0, 100) / 100.0;
        var frame = BuildPacket(CommandSetLedColors, 0, KeyLedCount * 3);

        for (var i = 0; i < KeyLedCount && i < colors.Count; i++)
        {
            // Цвета идут тремя слоями: сначала красные всех клавиш, затем зелёные, затем синие
            frame[DataOffset + i] = (byte)Math.Round(colors[i].R * scale);
            frame[DataOffset + KeyLedCount + i] = (byte)Math.Round(colors[i].G * scale);
            frame[DataOffset + KeyLedCount * 2 + i] = (byte)Math.Round(colors[i].B * scale);
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Эффекты шлют кадры часто, а многие кадры повторяются — одинаковые не отправляем
            if (_isCustomMode && _lastFrame != null && frame.AsSpan().SequenceEqual(_lastFrame))
                return;

            var wait = FrameInterval - _sinceFrame.Elapsed;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            // Интервал отсчитывается от начала отправки, иначе время самой отправки сдвигало бы каждый следующий кадр
            _sinceFrame.Restart();

            await Task.Run(() =>
            {
                // Цвета отдельных клавиш клавиатура показывает только в своём режиме «свои цвета» — включаем его при первом кадре
                if (!_isCustomMode)
                {
                    _stream.SetFeature(BuildCustomModeProfile());
                    _isCustomMode = true;
                }

                _stream.SetFeature(frame);
            }, ct);

            _lastFrame = frame;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Профиль клавиатуры с режимом подсветки «свои цвета»; остальные настройки остаются прежними.
    /// </summary>
    private byte[] BuildCustomModeProfile()
    {
        var packet = BuildPacket(CommandSetProfile, 0, ProfileLength);
        _profile!.CopyTo(packet, DataOffset);

        packet[DataOffset + ProfileLedModeSelection] = 1;
        packet[DataOffset + ProfileLedMode] = LedModeCustom;

        return packet;
    }

    /// <summary>
    /// Собирает отчёт: идентификатор, команда, параметр, номер профиля, признак, длина данных; данные дописывает вызывающий.
    /// </summary>
    private byte[] BuildPacket(byte command, byte parameter, int dataLength)
    {
        var packet = new byte[_reportLength];

        packet[0] = ReportId;
        packet[1] = command;
        packet[2] = parameter;
        packet[3] = ProfileIndex;
        packet[4] = 1;
        packet[6] = (byte)dataLength;
        packet[7] = (byte)(dataLength >> 8);

        return packet;
    }

    /// <summary>
    /// Читает версию прошивки и профиль клавиатуры: запрос уходит отчётом, ответ забирается чтением того же отчёта.
    /// </summary>
    private bool TryReadSettings()
    {
        var step = "версия прошивки";

        try
        {
            var firmware = Exchange(BuildPacket(CommandGetFirmware, 1, FirmwareLength));
            _firmware = $"{firmware[16]:x2}{firmware[17]:x2}";

            // Настройщик RK длину данных в ответе не проверяет и берёт профиль как есть — делаем так же
            step = "профиль";
            var reply = Exchange(BuildPacket(CommandGetProfile, 0, ProfileLength));
            _profile = reply.AsSpan(DataOffset, ProfileLength).ToArray();

            return true;
        }
        catch (Exception e)
        {
            _readError = $"{step}, отчёт {_reportLength} байт: {e.GetType().Name}: {e.Message}";
            return false;
        }
    }

    private byte[] Exchange(byte[] request)
    {
        _stream.SetFeature(request);

        var reply = new byte[_reportLength];
        reply[0] = ReportId;
        _stream.GetFeature(reply);

        return reply;
    }

    private static string? ReadSerial(HidDevice hidDevice)
    {
        try
        {
            var serial = hidDevice.GetSerialNumber();
            return string.IsNullOrWhiteSpace(serial) ? null : serial;
        }
        catch (Exception)
        {
            // Серийный номер отдают не все клавиатуры — тогда узнаём её по пути устройства
            return null;
        }
    }
}

using System.Diagnostics;
using HidSharp;
using LightSpace.Core;

namespace LightSpace.Devices.RoyalKludge;

/// <summary>
/// Клавиатура Royal Kludge S98, подключённая проводом: каждая клавиша — отдельный светодиод.
/// Формат команд соответствует веб-настройщику RK (drive.rkgaming.com).
/// </summary>
public sealed class RkKeyboardDevice : LedDevice, IKeyboardDevice, IBuiltinEffectsDevice
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
    private const byte CommandGetEffectColors = 138;
    private const byte CommandSetEffectColors = 10;

    private const int FirmwareLength = 10;
    private const int ProfileLength = 128;
    private const int ProfileLedModeSelection = 9;
    private const int ProfileLedMode = 10;
    private const byte LedModeCustom = 18;

    // Яркость, скорость и цветность каждого режима — по два байта начиная с 56-го: яркость, затем скорость × 16 + цветность
    private const int ProfileModeParams = 56;
    private const int MaxBrightness = 19;
    private const int MinSpeed = 1;
    private const int MaxSpeed = 4;
    private const int ColorSingle = 0;
    private const int ColorMixed = 7;

    // Свой цвет каждого режима: красный, зелёный, синий в начале 21-байтного блока режима
    private const int EffectColorsLength = 420;
    private const int EffectColorsStride = 21;

    // Настройщик RK по умолчанию работает с первым профилем клавиатуры
    private const byte ProfileIndex = 0;

    private const int KeyLedCount = 126;

    // Клавиатура по проводу принимает до 60 кадров в секунду — сколько выбрано в настройках эффектов
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 60);

    /// <summary>
    /// Эффекты прошивки S98 под их номерами режимов; названия переведены с внутренних имён режимов настройщика RK.
    /// </summary>
    private static readonly IReadOnlyList<BuiltinEffect> FirmwareEffects = new BuiltinEffect[]
    {
        new(1, "Постоянный свет"),
        new(2, "Дыхание"),
        new(3, "Радуга"),
        new(4, "Вспышки"),
        new(5, "Капли дождя"),
        new(6, "Радужное колесо"),
        new(7, "Круги от нажатий"),
        new(8, "Мерцание звёзд"),
        new(9, "Тающий след нажатий"),
        new(10, "Змейка"),
        new(11, "Неоновый поток"),
        new(12, "Реакция на нажатия"),
        new(13, "Синусоида"),
        new(14, "Сканер"),
        new(15, "Вертушка"),
        new(16, "Цветной водопад"),
        new(17, "Распускание")
    };

    private readonly HidStream _stream;
    private readonly string _devicePath;
    private readonly string? _serial;
    private readonly int _reportLength;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stopwatch _sinceFrame = Stopwatch.StartNew();

    private string? _firmware;
    private byte[]? _profile;
    private byte[]? _effectColors;
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

    public IReadOnlyList<BuiltinEffect> BuiltinEffects => FirmwareEffects;

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

    /// <summary>
    /// Эффект прошивки идёт плавно с заданными яркостью и скоростью; без цвета он переливается всеми цветами.
    /// </summary>
    public async Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default)
    {
        if (_profile == null || FirmwareEffects.All(e => e.Id != effectId))
            return;

        var profile = BuildPacket(CommandSetProfile, 0, ProfileLength);
        _profile.CopyTo(profile, DataOffset);

        // Свои цвета клавиш включаются отдельным признаком — для эффекта прошивки его снимаем
        var modeParams = DataOffset + ProfileModeParams + effectId * 2;
        var level = (int)Math.Round(Math.Clamp(brightness, 0, 100) / 100.0 * MaxBrightness);
        var pace = MinSpeed + (int)Math.Round(Math.Clamp(speed, 0, 100) / 100.0 * (MaxSpeed - MinSpeed));

        profile[DataOffset + ProfileLedModeSelection] = 0;
        profile[DataOffset + ProfileLedMode] = (byte)effectId;
        profile[modeParams] = (byte)level;
        profile[modeParams + 1] = (byte)(pace << 4 | (color == null ? ColorMixed : ColorSingle));

        byte[]? colors = null;

        if (color is { } single)
        {
            var effectColors = _effectColors ?? new byte[EffectColorsLength];
            effectColors[effectId * EffectColorsStride] = single.R;
            effectColors[effectId * EffectColorsStride + 1] = single.G;
            effectColors[effectId * EffectColorsStride + 2] = single.B;

            colors = BuildPacket(CommandSetEffectColors, 0, EffectColorsLength);
            effectColors.CopyTo(colors, DataOffset);
            _effectColors = effectColors;
        }

        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                if (colors != null)
                    _stream.SetFeature(colors);

                _stream.SetFeature(profile);
            }, ct);

            // Клавиатура теперь в режиме эффекта: следующий рисунок по клавишам снова включит «свои цвета»
            _profile = profile.AsSpan(DataOffset, ProfileLength).ToArray();
            _isCustomMode = false;
            _lastFrame = null;
            _sinceFrame.Restart();
        }
        finally
        {
            _lock.Release();
        }
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

            _effectColors = TryReadEffectColors();

            return true;
        }
        catch (Exception e)
        {
            _readError = $"{step}, отчёт {_reportLength} байт: {e.GetType().Name}: {e.Message}";
            return false;
        }
    }

    /// <summary>
    /// Читает свои цвета режимов подсветки; null, если клавиатура их не отдала — тогда цвета остальных режимов задаются заново.
    /// </summary>
    private byte[]? TryReadEffectColors()
    {
        try
        {
            var reply = Exchange(BuildPacket(CommandGetEffectColors, 0, EffectColorsLength));
            return reply.AsSpan(DataOffset, EffectColorsLength).ToArray();
        }
        catch (Exception)
        {
            // Без этих цветов клавиатура всё равно управляется — эффект одним цветом просто перезапишет их
            return null;
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

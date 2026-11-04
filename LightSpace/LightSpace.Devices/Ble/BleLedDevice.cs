using LightSpace.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace LightSpace.Devices.Ble;

public enum BleVariant
{
    /// <summary>Контроллеры ELK-BLEDOM / MELK (приложения duoCo Strip, Lotus Lantern).</summary>
    ElkBledom,

    /// <summary>Контроллеры Triones / LEDBLE (приложение Happy Lighting).</summary>
    Triones
}

/// <summary>
/// Лента с дешёвым Bluetooth LE контроллером.
/// </summary>
public sealed class BleLedDevice : LedDevice, IBuiltinEffectsDevice
{
    public static readonly (BleVariant Variant, Guid Service, Guid Characteristic)[] Variants =
    {
        (BleVariant.ElkBledom, BluetoothUuidHelper.FromShortId(0xFFF0), BluetoothUuidHelper.FromShortId(0xFFF3)),
        (BleVariant.Triones, BluetoothUuidHelper.FromShortId(0xFFD5), BluetoothUuidHelper.FromShortId(0xFFD9))
    };

    // После сна компьютера Windows закрывает соединение — тогда устройство и характеристика открываются заново
    private BluetoothLEDevice _device;
    private GattCharacteristic _characteristic;
    private readonly BleVariant _variant;
    private readonly ElkModel? _model;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Адрес хранится отдельно: у закрытого Windows устройства его уже не прочитать
    private readonly ulong _address;
    private int? _sentBrightness;

    public BleLedDevice(BluetoothLEDevice device, GattCharacteristic characteristic, BleVariant variant, string name)
    {
        _device = device;
        _characteristic = characteristic;
        _address = device.BluetoothAddress;
        _variant = variant;
        _model = variant == BleVariant.ElkBledom ? ElkModel.Find(name) : null;
        Name = name;

        DeviceLog.Write($"BLE «{name}» {Address}: вариант {variant}, модель {_model?.Name ?? "не известна"}, "
            + $"встроенных эффектов {BuiltinEffects.Count}, свойства характеристики {characteristic.CharacteristicProperties}");
    }

    public override string Id => $"ble:{_address:X12}";

    public override string Name { get; }

    public override string Protocol => _model != null
        ? $"BLE · {_model.Name}"
        : _variant == BleVariant.ElkBledom ? "BLE · ELK-BLEDOM" : "BLE · Triones";

    public IReadOnlyList<BuiltinEffect> BuiltinEffects => _model?.Effects ?? Array.Empty<BuiltinEffect>();

    public bool HasBuiltinColor => false;

    /// <summary>
    /// Выключенный контроллер ELK/Triones принимает цвета и эффекты, но не показывает их, пока его не включат.
    /// </summary>
    public override bool NeedsPowerOnAtStart => true;

    public override string Address => string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((_address >> (i * 8)) & 0xFF).ToString("X2")));

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        var command = _variant switch
        {
            BleVariant.ElkBledom when isOn => new byte[] { 0x7E, 0x04, 0x04, 0xF0, 0x00, 0x01, 0xFF, 0x00, 0xEF },
            BleVariant.ElkBledom => new byte[] { 0x7E, 0x04, 0x04, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xEF },
            _ when isOn => new byte[] { 0xCC, 0x23, 0x33 },
            _ => new byte[] { 0xCC, 0x24, 0x33 }
        };

        return WriteAsync(command, ct);
    }

    public override async Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        // У известной модели есть своя яркость: без неё лента остаётся на яркости из приложения телефона
        if (_model != null)
        {
            await WriteAsync(new byte[] { 0x7E, 0x00, 0x05, 0x03, color.R, color.G, color.B, 0x00, 0xEF }, ct);

            if (brightness != _sentBrightness)
            {
                await WriteAsync(ElkModel.BrightnessCommand(brightness), ct);
                _sentBrightness = brightness;
            }

            return;
        }

        await SetScaledColorAsync(color, brightness, ct);
    }

    /// <summary>
    /// Лента играет эффект своими цветами и со своей яркостью: цвет и яркость не меняются.
    /// </summary>
    public async Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default)
    {
        if (_model == null)
            throw new NotSupportedException("У этой модели нет известных встроенных эффектов");

        await WriteAsync(ElkModel.EffectCommand(effectId), ct);

        // Некоторые контроллеры ELK теряют команду, пришедшую сразу за предыдущей
        await Task.Delay(150, ct);
        await WriteAsync(ElkModel.EffectSpeedCommand(speed), ct);
    }

    /// <summary>
    /// Встроенный эффект контроллера останавливается обычным цветом — чёрным.
    /// </summary>
    public override async Task ResetAsync(CancellationToken ct = default)
    {
        await SetScaledColorAsync(new RgbColor(0, 0, 0), 0, ct);

        // Следом обычно идёт запуск встроенного эффекта — без паузы контроллер ELK его теряет и остаётся на цвете
        await Task.Delay(150, ct);
    }

    /// <summary>
    /// Яркость передаётся через сам цвет — так одинаково работает на всех вариантах контроллеров.
    /// </summary>
    private Task SetScaledColorAsync(RgbColor color, int brightness, CancellationToken ct)
    {
        var c = color.Scale(brightness);

        var command = _variant == BleVariant.ElkBledom
            ? new byte[] { 0x7E, 0x00, 0x05, 0x03, c.R, c.G, c.B, 0x00, 0xEF }
            : new byte[] { 0x56, c.R, c.G, c.B, 0x00, 0xF0, 0xAA };

        return WriteAsync(command, ct);
    }

    public override void Dispose()
    {
        _characteristic.Service.Dispose();
        _device.Dispose();
        _lock.Dispose();
    }

    private GattWriteOption WriteOption =>
        _characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

    /// <summary>
    /// Открывает устройство и его характеристику заново, вместо закрытых Windows.
    /// </summary>
    private async Task ReopenAsync(CancellationToken ct)
    {
        var (_, serviceUuid, characteristicUuid) = Variants.First(v => v.Variant == _variant);

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(_address).AsTask(ct)
            ?? throw new IOException("Bluetooth-устройство не открылось заново");

        var services = await device.GetGattServicesForUuidAsync(serviceUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        var service = services.Status == GattCommunicationStatus.Success ? services.Services.FirstOrDefault() : null;

        if (service == null)
        {
            device.Dispose();
            throw new IOException($"Сервис ленты не найден после переподключения ({services.Status})");
        }

        var characteristics = await service.GetCharacteristicsForUuidAsync(characteristicUuid, BluetoothCacheMode.Uncached).AsTask(ct);
        var characteristic = characteristics.Status == GattCommunicationStatus.Success ? characteristics.Characteristics.FirstOrDefault() : null;

        if (characteristic == null)
        {
            service.Dispose();
            device.Dispose();
            throw new IOException($"Характеристика ленты не найдена после переподключения ({characteristics.Status})");
        }

        _device = device;
        _characteristic = characteristic;

        DeviceLog.Write($"BLE «{Name}»: подключено заново");
    }

    private async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            GattCommunicationStatus status;
            GattWriteOption option;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                option = WriteOption;
                status = await _characteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), option).AsTask(ct);
            }
            catch (ObjectDisposedException)
            {
                // Соединение закрыла Windows, например на время сна: открываем устройство заново и повторяем команду
                DeviceLog.Write($"BLE «{Name}»: соединение закрыто, подключаемся заново");

                await ReopenAsync(ct);

                option = WriteOption;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                status = await _characteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), option).AsTask(ct);
            }

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);

            DeviceLog.Write($"BLE «{Name}» → {DeviceLog.Hex(data)} ({option}): {status}, {elapsed.TotalMilliseconds:0} мс");

            if (status != GattCommunicationStatus.Success)
                throw new IOException($"Ошибка записи по Bluetooth: {status}");
        }
        catch (Exception e) when (e is not IOException and not OperationCanceledException)
        {
            DeviceLog.Write($"BLE «{Name}» → {DeviceLog.Hex(data)}: исключение {e.GetType().Name}: {e.Message}");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}

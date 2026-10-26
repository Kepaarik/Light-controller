using LedHub.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace LedHub.Protocols.Ble;

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
public sealed class BleLedDevice : LedDevice
{
    public static readonly (BleVariant Variant, Guid Service, Guid Characteristic)[] Variants =
    {
        (BleVariant.ElkBledom, BluetoothUuidHelper.FromShortId(0xFFF0), BluetoothUuidHelper.FromShortId(0xFFF3)),
        (BleVariant.Triones, BluetoothUuidHelper.FromShortId(0xFFD5), BluetoothUuidHelper.FromShortId(0xFFD9))
    };

    private readonly BluetoothLEDevice _device;
    private readonly GattCharacteristic _characteristic;
    private readonly BleVariant _variant;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BleLedDevice(BluetoothLEDevice device, GattCharacteristic characteristic, BleVariant variant, string name)
    {
        _device = device;
        _characteristic = characteristic;
        _variant = variant;
        Name = name;
    }

    public override string Id => $"ble:{_device.BluetoothAddress:X12}";

    public override string Name { get; }

    public override string Protocol => _variant == BleVariant.ElkBledom ? "BLE · ELK-BLEDOM" : "BLE · Triones";

    public override string Address => string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((_device.BluetoothAddress >> (i * 8)) & 0xFF).ToString("X2")));

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

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        // Яркость передаём через сам цвет — так одинаково работает на всех вариантах контроллеров
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

    private async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        var option = _characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        await _lock.WaitAsync(ct);
        try
        {
            var status = await _characteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), option).AsTask(ct);

            if (status != GattCommunicationStatus.Success)
                throw new IOException($"Ошибка записи по Bluetooth: {status}");
        }
        finally
        {
            _lock.Release();
        }
    }
}

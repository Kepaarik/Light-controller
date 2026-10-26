using System.IO;
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
public sealed class BleLedDevice : LedDevice, IHardwareEffectsDevice
{
    public static readonly (BleVariant Variant, Guid Service, Guid Characteristic)[] Variants =
    {
        (BleVariant.ElkBledom, BluetoothUuidHelper.FromShortId(0xFFF0), BluetoothUuidHelper.FromShortId(0xFFF3)),
        (BleVariant.Triones, BluetoothUuidHelper.FromShortId(0xFFD5), BluetoothUuidHelper.FromShortId(0xFFD9))
    };

    private readonly BluetoothLEDevice _device;
    private readonly GattCharacteristic _characteristic;
    private readonly BleVariant _variant;
    private readonly ElkModel? _model;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int? _sentBrightness;

    public BleLedDevice(BluetoothLEDevice device, GattCharacteristic characteristic, BleVariant variant, string name)
    {
        _device = device;
        _characteristic = characteristic;
        _variant = variant;
        _model = variant == BleVariant.ElkBledom ? ElkModel.Find(name) : null;
        Name = name;
    }

    public override string Id => $"ble:{_device.BluetoothAddress:X12}";

    public override string Name { get; }

    public override string Protocol => _model != null
        ? $"BLE · {_model.Name}"
        : _variant == BleVariant.ElkBledom ? "BLE · ELK-BLEDOM" : "BLE · Triones";

    public IReadOnlyList<HardwareEffect> HardwareEffects => _model?.Effects ?? Array.Empty<HardwareEffect>();

    /// <summary>
    /// Название известной модели контроллера (например, MELK-OA21); null для прочих.
    /// </summary>
    public string? ModelName => _model?.Name;

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

    public async Task StartHardwareEffectAsync(int effectId, int speed, CancellationToken ct = default)
    {
        if (_model == null)
            throw new NotSupportedException("У этой модели нет известных встроенных эффектов");

        await WriteAsync(ElkModel.EffectCommand(effectId), ct);

        // Некоторые контроллеры ELK теряют команду, пришедшую сразу за предыдущей
        await Task.Delay(150, ct);
        await WriteAsync(ElkModel.EffectSpeedCommand(speed), ct);
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

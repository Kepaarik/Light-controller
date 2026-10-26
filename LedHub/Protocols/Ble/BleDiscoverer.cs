using System.Collections.Concurrent;
using LedHub.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LedHub.Protocols.Ble;

/// <summary>
/// Ищет BLE-ленты по рекламным пакетам, затем подключается и проверяет наличие нужной GATT-характеристики.
/// </summary>
public sealed class BleDiscoverer : IDeviceDiscoverer
{
    private static readonly string[] KnownNamePrefixes = { "ELK-", "BLEDOM", "MELK", "Triones", "LEDBLE", "QHM-" };

    public string Protocol => "Bluetooth";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        var candidates = await ScanAdvertisementsAsync(timeout, ct);
        var devices = new List<LedDevice>();

        // Подключаемся по очереди: стек Bluetooth в Windows плохо переносит параллельные подключения
        foreach (var (address, advertisedName) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var device = await TryConnectAsync(address, advertisedName);
            if (device != null)
                devices.Add(device);
        }

        return devices;
    }

    private static async Task<IReadOnlyDictionary<ulong, string>> ScanAdvertisementsAsync(TimeSpan timeout, CancellationToken ct)
    {
        var candidates = new ConcurrentDictionary<ulong, string>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        watcher.Received += (_, args) =>
        {
            var name = args.Advertisement.LocalName ?? "";

            if (!IsCandidate(name, args.Advertisement.ServiceUuids))
                return;

            candidates.AddOrUpdate(args.BluetoothAddress, name, (_, old) => name.Length > 0 ? name : old);
        };

        BluetoothLEAdvertisementWatcherStatus status;

        watcher.Start();
        try
        {
            await Task.Delay(timeout, ct);
        }
        finally
        {
            status = watcher.Status;
            watcher.Stop();
        }

        if (status == BluetoothLEAdvertisementWatcherStatus.Aborted)
            throw new InvalidOperationException("Bluetooth выключен или недоступен");

        return candidates;
    }

    private static bool IsCandidate(string name, IList<Guid> serviceUuids)
    {
        if (KnownNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return true;

        return BleLedDevice.Variants.Any(v => serviceUuids.Contains(v.Service));
    }

    private static async Task<LedDevice?> TryConnectAsync(ulong address, string advertisedName)
    {
        BluetoothLEDevice? device = null;

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device == null)
                return null;

            var services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success)
            {
                device.Dispose();
                return null;
            }

            foreach (var (variant, serviceUuid, characteristicUuid) in BleLedDevice.Variants)
            {
                var service = services.Services.FirstOrDefault(s => s.Uuid == serviceUuid);
                if (service == null)
                    continue;

                var characteristics = await service.GetCharacteristicsForUuidAsync(characteristicUuid, BluetoothCacheMode.Uncached);
                var characteristic = characteristics.Status == GattCommunicationStatus.Success
                    ? characteristics.Characteristics.FirstOrDefault()
                    : null;

                if (characteristic == null)
                    continue;

                var name = string.IsNullOrWhiteSpace(device.Name) ? advertisedName : device.Name;

                return new BleLedDevice(device, characteristic, variant, string.IsNullOrWhiteSpace(name) ? "BLE лента" : name);
            }

            device.Dispose();
            return null;
        }
        catch (Exception)
        {
            // Посторонние BLE-устройства с похожими UUID могут не дать подключиться — это не ошибка поиска
            device?.Dispose();
            return null;
        }
    }
}

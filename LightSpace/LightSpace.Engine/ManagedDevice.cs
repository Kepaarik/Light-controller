using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine.Output;

namespace LightSpace.Engine;

/// <summary>
/// Состояние связи с устройством.
/// </summary>
public enum DeviceStatus
{
    /// <summary>Устройство известно по прошлым запускам, но при последнем поиске не нашлось.</summary>
    NotFound,

    /// <summary>Устройство подключено и принимает команды.</summary>
    Connected,

    /// <summary>Устройство найдено, но для управления ему чего-то не хватает, например ключа.</summary>
    NeedsSetup,

    /// <summary>Устройство подключено, но последняя команда до него не дошла.</summary>
    Failing
}

/// <summary>
/// Известное приложению устройство: его сохранённые настройки и, пока оно подключено, драйвер и вывод кадров.
/// </summary>
public sealed class ManagedDevice
{
    public ManagedDevice(DeviceConfig settings)
    {
        Settings = settings;
    }

    /// <summary>
    /// Сохраняемые настройки устройства; живут в конфиге приложения.
    /// </summary>
    public DeviceConfig Settings { get; }

    public string Id => Settings.Id;

    public string Name => Device?.Name ?? Settings.Name;

    public string Protocol => Device?.Protocol ?? Settings.Protocol;

    public string Address => Device?.Address ?? Settings.Address;

    /// <summary>
    /// Вывод кадров на устройство; null, пока устройство не подключено.
    /// </summary>
    public DeviceOutput? Output { get; private set; }

    /// <summary>
    /// Драйвер подключённого устройства; null, пока устройство не подключено.
    /// </summary>
    public LedDevice? Device => Output?.Device;

    /// <summary>
    /// Возможности подключённого устройства; null, пока устройство не подключено.
    /// </summary>
    public DeviceCapabilities? Capabilities => Device?.Capabilities;

    public DeviceStatus Status
    {
        get
        {
            if (Output == null)
                return DeviceStatus.NotFound;

            if (!Output.Device.IsReady)
                return DeviceStatus.NeedsSetup;

            return Output.Diagnostics.IsFailing ? DeviceStatus.Failing : DeviceStatus.Connected;
        }
    }

    /// <summary>
    /// Пояснение к состоянию: чего не хватает устройству или почему не дошла команда; null, если всё в порядке.
    /// </summary>
    public string? StatusText => Status switch
    {
        DeviceStatus.NeedsSetup => Output?.Device.StatusText,
        DeviceStatus.Failing => Output?.Diagnostics.LastError,
        _ => null
    };

    /// <summary>
    /// Подключает найденное устройство: запоминает, как оно называется и где находится, и открывает вывод кадров.
    /// </summary>
    public void Connect(LedDevice device, bool isLightingEnabled)
    {
        Disconnect();

        Settings.Name = device.Name;
        Settings.Protocol = device.Protocol;
        Settings.Address = device.Address;

        Output = new DeviceOutput(device, isLightingEnabled);
    }

    /// <summary>
    /// Останавливает вывод и закрывает соединение с устройством.
    /// </summary>
    public void Disconnect()
    {
        if (Output == null)
            return;

        var device = Output.Device;

        Output.Dispose();
        device.Dispose();
        Output = null;
    }
}

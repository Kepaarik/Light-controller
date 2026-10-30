using HidSharp;

namespace LightSpace.Devices.Robobloq;

/// <summary>
/// Ищет подсветку Robobloq среди подключённых USB HID-устройств.
/// </summary>
public sealed class RobobloqDiscoverer : IDeviceDiscoverer
{
    // Страница использования 0xFF00 (vendor-defined), использование 0x01
    private const uint ControlUsage = 0xFF000001;

    public string Protocol => "Robobloq (USB)";

    public Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<LedDevice>>(() => DeviceList.Local
            .GetHidDevices(RobobloqDevice.VendorId, RobobloqDevice.ProductId)
            .Where(IsControlInterface)
            .Select(RobobloqDevice.Connect)
            .OfType<LedDevice>()
            .ToList(), ct);
    }

    /// <summary>
    /// У ленты несколько HID-интерфейсов; команды принимает только интерфейс с vendor-страницей 0xFF00.
    /// </summary>
    private static bool IsControlInterface(HidDevice device)
    {
        try
        {
            return device.GetReportDescriptor().DeviceItems.Any(item => item.Usages.GetAllValues().Contains(ControlUsage));
        }
        catch (Exception)
        {
            // Дескриптор некоторых интерфейсов Windows не отдаёт — такие интерфейсы нам не подходят
            return false;
        }
    }
}

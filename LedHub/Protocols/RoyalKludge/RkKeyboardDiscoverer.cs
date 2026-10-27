using HidSharp;
using LedHub.Core;

namespace LedHub.Protocols.RoyalKludge;

/// <summary>
/// Ищет проводные клавиатуры Royal Kludge S98 среди подключённых USB HID-устройств.
/// </summary>
public sealed class RkKeyboardDiscoverer : IDeviceDiscoverer
{
    // Страница использования 0xFF00 (vendor-defined), использование 0x01
    private const uint ControlUsage = 0xFF000001;

    public string Protocol => "Royal Kludge (USB)";

    public Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<LedDevice>>(() => DeviceList.Local
            .GetHidDevices(RkKeyboardDevice.VendorId)
            .Where(d => RkKeyboardDevice.ProductIds.Contains(d.ProductID))
            .Where(IsControlInterface)
            .Select(RkKeyboardDevice.Connect)
            .OfType<LedDevice>()
            .ToList(), ct);
    }

    /// <summary>
    /// У клавиатуры много HID-интерфейсов; настройки принимает только интерфейс с vendor-страницей 0xFF00.
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

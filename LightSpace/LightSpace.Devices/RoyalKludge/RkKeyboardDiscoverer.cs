using HidSharp;

namespace LightSpace.Devices.RoyalKludge;

/// <summary>
/// Ищет проводные клавиатуры Royal Kludge S98 среди подключённых USB HID-устройств.
/// </summary>
public sealed class RkKeyboardDiscoverer : IDeviceDiscoverer
{
    public string Protocol => "Royal Kludge (USB)";

    public Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<LedDevice>>(() =>
        {
            // У клавиатуры много HID-интерфейсов; команды принимает тот, в описании которого есть отчёт настройщика.
            // Если таких несколько, оставляем тот, что ответил, а остальные закрываем
            var candidates = DeviceList.Local
                .GetHidDevices(RkKeyboardDevice.VendorId)
                .Where(d => RkKeyboardDevice.ProductIds.Contains(d.ProductID))
                .Where(RkKeyboardDevice.IsCommandInterface)
                .Select(RkKeyboardDevice.Connect)
                .OfType<RkKeyboardDevice>()
                .ToList();

            var keyboards = candidates
                .GroupBy(d => d.Id)
                .Select(g => g.FirstOrDefault(d => d.IsReady) ?? g.First())
                .ToList();

            foreach (var unused in candidates.Except(keyboards))
                unused.Dispose();

            return keyboards.Cast<LedDevice>().ToList();
        }, ct);
    }
}

using System.Net;
using System.Net.Sockets;

namespace LightSpace.Devices.OpenRgb;

/// <summary>
/// Спрашивает у OpenRGB, запущенного на этом компьютере с включённым SDK-сервером, список его контроллеров.
/// </summary>
public sealed class OpenRgbDiscoverer : IDeviceDiscoverer
{
    private static readonly IPEndPoint Server = new(IPAddress.Loopback, OpenRgbProtocol.DefaultPort);

    public string Protocol => "OpenRGB";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        IReadOnlyList<OpenRgbController> controllers;

        try
        {
            using var client = await OpenRgbClient.ConnectAsync(Server, ct);
            controllers = await client.ListControllersAsync(ct);
        }
        catch (Exception e) when (e is SocketException or TimeoutException)
        {
            // OpenRGB не запущен или его сервер выключен — это не ошибка поиска, просто таких устройств нет
            return Array.Empty<LedDevice>();
        }

        // Каждому контроллеру — своё соединение: команды разных устройств идут из разных потоков вывода
        return controllers
            .Where(c => c.LedCount > 0)
            .Select(c => (LedDevice)new OpenRgbDevice(Server, c))
            .ToList();
    }
}

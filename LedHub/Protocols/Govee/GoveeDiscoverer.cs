using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LedHub.Core;

namespace LedHub.Protocols.Govee;

/// <summary>
/// Ищет ленты Govee через LAN API: multicast-запрос на 4001, ответы приходят на 4002.
/// </summary>
public sealed class GoveeDiscoverer : IDeviceDiscoverer
{
    private const int ResponsePort = 4002;
    private static readonly IPEndPoint MulticastEndPoint = new(IPAddress.Parse("239.255.255.250"), 4001);
    private static readonly byte[] ScanMessage = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reserve\"}}}");

    public string Protocol => "Govee";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var listener = NetworkHelper.CreateSharedListener(ResponsePort);
        var devices = new Dictionary<string, LedDevice>();

        // Запрос отправляем с каждого интерфейса, иначе Windows выберет для multicast только один
        foreach (var (local, _) in NetworkHelper.GetIpv4Interfaces())
        {
            try
            {
                using var sender = new UdpClient(new IPEndPoint(local, 0));
                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                await sender.SendAsync(ScanMessage, MulticastEndPoint, ct);
            }
            catch (SocketException)
            {
                // Интерфейс без поддержки multicast — пропускаем
            }
        }

        await NetworkHelper.ReceiveAllAsync(listener, timeout, ct, packet =>
        {
            var device = TryParse(packet.Buffer);

            if (device != null && !devices.TryAdd(device.Id, device))
                device.Dispose();
        });

        return devices.Values.ToList();
    }

    private static GoveeDevice? TryParse(byte[] buffer)
    {
        try
        {
            using var json = JsonDocument.Parse(buffer);
            var data = json.RootElement.GetProperty("msg").GetProperty("data");

            var ip = IPAddress.Parse(data.GetProperty("ip").GetString()!);
            var deviceId = data.GetProperty("device").GetString()!;
            var sku = data.GetProperty("sku").GetString() ?? "";

            return new GoveeDevice(ip, deviceId, sku);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentNullException)
        {
            return null;
        }
    }
}

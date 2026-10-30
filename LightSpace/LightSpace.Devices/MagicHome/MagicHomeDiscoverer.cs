using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LightSpace.Devices.MagicHome;

/// <summary>
/// Ищет контроллеры Magic Home широковещательным запросом по UDP 48899.
/// </summary>
public sealed class MagicHomeDiscoverer : IDeviceDiscoverer
{
    private const int DiscoveryPort = 48899;
    private static readonly byte[] DiscoveryMessage = Encoding.ASCII.GetBytes("HF-A11ASSISTHREAD");

    public string Protocol => "Magic Home";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var udp = new UdpClient(0) { EnableBroadcast = true };
        var devices = new Dictionary<string, LedDevice>();

        var targets = NetworkHelper.GetIpv4Interfaces()
            .Select(i => i.Broadcast)
            .Append(IPAddress.Broadcast)
            .Distinct();

        foreach (var target in targets)
        {
            try
            {
                await udp.SendAsync(DiscoveryMessage, new IPEndPoint(target, DiscoveryPort), ct);
            }
            catch (SocketException)
            {
                // Интерфейс может быть недоступен для широковещания — пропускаем его
            }
        }

        await NetworkHelper.ReceiveAllAsync(udp, timeout, ct, packet =>
        {
            // Ответ имеет вид "192.168.1.10,ACCF23AABBCC,AK001-ZJ2101"
            var parts = Encoding.ASCII.GetString(packet.Buffer).Trim().Split(',');

            if (parts.Length < 3 || !IPAddress.TryParse(parts[0], out var ip) || devices.ContainsKey(parts[1]))
                return;

            devices[parts[1]] = new MagicHomeDevice(ip, parts[1], parts[2]);
        });

        return devices.Values.ToList();
    }
}

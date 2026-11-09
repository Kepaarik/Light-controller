using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LightSpace.Devices.Wled;

/// <summary>
/// Ищет ленты WLED: спрашивает по mDNS, кто держит службу _wled._tcp, и читает у каждого ответившего /json/info.
/// </summary>
public sealed class WledDiscoverer : IDeviceDiscoverer
{
    private static readonly IPEndPoint MdnsEndPoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public string Protocol => "WLED";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        var addresses = new HashSet<IPAddress>();
        var query = WledProtocol.BuildMdnsQuery();
        var senders = new List<UdpClient>();

        try
        {
            // Запрос — с каждого интерфейса: ответы на него приходят на тот же сокет
            foreach (var (local, _) in NetworkHelper.GetIpv4Interfaces())
            {
                try
                {
                    var sender = new UdpClient(new IPEndPoint(local, 0));
                    sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                    senders.Add(sender);

                    await sender.SendAsync(query, MdnsEndPoint, ct);
                }
                catch (SocketException)
                {
                    // Интерфейс без multicast — пропускаем
                }
            }

            // Поиск WLED не должен растягивать общий поиск — устройства отвечают за доли секунды
            var listen = TimeSpan.FromTicks(Math.Min(timeout.Ticks, TimeSpan.FromSeconds(3).Ticks));

            await Task.WhenAll(senders.Select(sender => NetworkHelper.ReceiveAllAsync(sender, listen, ct, packet =>
            {
                if (WledProtocol.IsWledAnswer(packet.Buffer))
                {
                    lock (addresses)
                        addresses.Add(packet.RemoteEndPoint.Address);
                }
            })));
        }
        finally
        {
            foreach (var sender in senders)
                sender.Dispose();
        }

        var devices = await Task.WhenAll(addresses.Select(ip => ReadInfoAsync(ip, ct)));

        return devices.OfType<LedDevice>().ToList();
    }

    /// <summary>
    /// Имя, MAC и число светодиодов из /json/info; null — это не WLED или он не ответил.
    /// </summary>
    private static async Task<WledDevice?> ReadInfoAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            await using var stream = await Http.GetStreamAsync($"http://{ip}/json/info", ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var root = json.RootElement;
            var name = root.GetProperty("name").GetString() ?? "WLED";
            var mac = root.GetProperty("mac").GetString() ?? ip.ToString();
            var leds = root.GetProperty("leds").GetProperty("count").GetInt32();

            return leds > 0 ? new WledDevice(ip, mac, name, leds) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}

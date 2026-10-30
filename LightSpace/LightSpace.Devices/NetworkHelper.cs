using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LightSpace.Devices;

public static class NetworkHelper
{
    /// <summary>
    /// Возвращает IPv4-адреса всех активных сетевых интерфейсов вместе с их широковещательными адресами.
    /// </summary>
    public static IReadOnlyList<(IPAddress Local, IPAddress Broadcast)> GetIpv4Interfaces()
    {
        var result = new List<(IPAddress, IPAddress)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];

                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ip[i] | ~mask[i]);

                result.Add((unicast.Address, new IPAddress(broadcast)));
            }
        }

        return result;
    }

    /// <summary>
    /// Принимает UDP-пакеты, пока не истечёт время ожидания.
    /// </summary>
    public static async Task ReceiveAllAsync(UdpClient udp, TimeSpan timeout, CancellationToken ct, Action<UdpReceiveResult> onPacket)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await udp.ReceiveAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            onPacket(packet);
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Создаёт UDP-сокет на указанном порту, который могут одновременно слушать и другие программы.
    /// </summary>
    public static UdpClient CreateSharedListener(int port)
    {
        var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

        return udp;
    }
}

using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LedHub.Core;

namespace LedHub.Protocols.Tuya;

/// <summary>
/// Слушает анонсы, которые Tuya-устройства сами рассылают каждые ~5 секунд на UDP 6666 и 6667.
/// </summary>
public sealed class TuyaDiscoverer : IDeviceDiscoverer
{
    private static readonly int[] BroadcastPorts = { 6666, 6667 };

    public string Protocol => "Tuya";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        var devices = new Dictionary<string, LedDevice>();
        var listeners = BroadcastPorts.Select(NetworkHelper.CreateSharedListener).ToList();

        try
        {
            await Task.WhenAll(listeners.Select(listener => NetworkHelper.ReceiveAllAsync(listener, timeout, ct, packet =>
            {
                var device = TryParse(packet.Buffer);
                if (device == null)
                    return;

                // Пакеты с двух портов могут обрабатываться одновременно
                lock (devices)
                {
                    if (!devices.TryAdd(device.Id, device))
                        device.Dispose();
                }
            })));
        }
        finally
        {
            foreach (var listener in listeners)
                listener.Dispose();
        }

        return devices.Values.ToList();
    }

    private static TuyaDevice? TryParse(byte[] packet)
    {
        if (packet.Length <= 28 ||BinaryPrimitives.ReadUInt32BigEndian(packet) != TuyaProtocol.Prefix)
            return null;

        // После 16 байт заголовка и 4 байт кода возврата идут данные, в конце — CRC и суффикс
        var data = packet.AsSpan(20, packet.Length - 28);

        try
        {
            var json = data[0] == (byte)'{'
                ? Encoding.UTF8.GetString(data)
                : Encoding.UTF8.GetString(TuyaProtocol.Decrypt(TuyaProtocol.BroadcastKey, data));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var deviceId = root.GetProperty("gwId").GetString()!;
            var ip = IPAddress.Parse(root.GetProperty("ip").GetString()!);
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            return new TuyaDevice(deviceId, ip, version);
        }
        catch (Exception e) when (e is CryptographicException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentNullException)
        {
            return null;
        }
    }
}

using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LightSpace.Devices.Tuya;

/// <summary>
/// Слушает анонсы, которые Tuya-устройства сами рассылают каждые ~5 секунд на UDP 6666 и 6667.
/// </summary>
public sealed class TuyaDiscoverer : IDeviceDiscoverer
{
    // 6666 и 6667 — анонсы 3.1–3.4, 7000 — анонсы 3.5 в своих кадрах
    private static readonly int[] BroadcastPorts = { 6666, 6667, 7000 };

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
        if (packet.Length <= 28)
            return null;

        try
        {
            string json;

            if (BinaryPrimitives.ReadUInt32BigEndian(packet) == TuyaProtocol.Prefix35)
            {
                // Анонс 3.5 зашифрован AES-GCM тем же общим ключом; перед JSON может стоять код возврата
                var (_, payload) = TuyaProtocol.ParseFrame35(packet, TuyaProtocol.BroadcastKey);
                var start = Array.IndexOf(payload, (byte)'{');

                if (start < 0)
                    return null;

                json = Encoding.UTF8.GetString(payload, start, payload.Length - start);
            }
            else if (BinaryPrimitives.ReadUInt32BigEndian(packet) == TuyaProtocol.Prefix)
            {
                // После 16 байт заголовка и 4 байт кода возврата идут данные, в конце — CRC и суффикс
                var data = packet.AsSpan(20, packet.Length - 28);

                json = data[0] == (byte)'{'
                    ? Encoding.UTF8.GetString(data)
                    : Encoding.UTF8.GetString(TuyaProtocol.Decrypt(TuyaProtocol.BroadcastKey, data));
            }
            else
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var deviceId = root.GetProperty("gwId").GetString()!;
            var ip = IPAddress.Parse(root.GetProperty("ip").GetString()!);
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            return new TuyaDevice(deviceId, ip, version);
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentNullException)
        {
            return null;
        }
    }
}

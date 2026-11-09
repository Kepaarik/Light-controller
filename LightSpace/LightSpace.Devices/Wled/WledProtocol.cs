using System.Buffers.Binary;
using System.Text;
using LightSpace.Core;

namespace LightSpace.Devices.Wled;

/// <summary>
/// Пакеты для WLED: кадры по DDP (UDP 4048) и запрос mDNS, которым ищутся устройства.
/// </summary>
public static class WledProtocol
{
    public const int DdpPort = 4048;
    public const int DdpHeaderSize = 10;

    /// <summary>
    /// Сколько светодиодов помещается в один пакет DDP: 480 × 3 байта укладываются в обычный размер пакета Ethernet.
    /// </summary>
    public const int LedsPerPacket = 480;

    private const byte DdpVersion1 = 0x40;
    private const byte DdpPush = 0x01;
    private const byte DdpTypeRgb8 = 0x0B;
    private const byte DdpDisplayId = 0x01;

    /// <summary>
    /// Пакеты DDP с цветами всех светодиодов: в последнем стоит флаг «показать», и лента меняется целиком сразу.
    /// </summary>
    public static IReadOnlyList<byte[]> BuildDdpPackets(IReadOnlyList<RgbColor> colors, byte sequence)
    {
        var packets = new List<byte[]>();

        for (var start = 0; start < colors.Count; start += LedsPerPacket)
        {
            var count = Math.Min(LedsPerPacket, colors.Count - start);
            var isLast = start + count >= colors.Count;
            var packet = new byte[DdpHeaderSize + count * 3];

            packet[0] = (byte)(DdpVersion1 | (isLast ? DdpPush : 0));
            packet[1] = (byte)(sequence & 0x0F);
            packet[2] = DdpTypeRgb8;
            packet[3] = DdpDisplayId;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), (uint)(start * 3));
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8), (ushort)(count * 3));

            for (var i = 0; i < count; i++)
            {
                var color = colors[start + i];

                packet[DdpHeaderSize + i * 3] = color.R;
                packet[DdpHeaderSize + i * 3 + 1] = color.G;
                packet[DdpHeaderSize + i * 3 + 2] = color.B;
            }

            packets.Add(packet);
        }

        return packets;
    }

    /// <summary>
    /// Запрос mDNS: «кто здесь _wled._tcp.local?» — отвечают все устройства WLED в сети.
    /// </summary>
    public static byte[] BuildMdnsQuery()
    {
        var packet = new List<byte>
        {
            0, 0, // id
            0, 0, // флаги: обычный запрос
            0, 1, // один вопрос
            0, 0, 0, 0, 0, 0 // ответов и прочих записей нет
        };

        foreach (var label in new[] { "_wled", "_tcp", "local" })
        {
            packet.Add((byte)label.Length);
            packet.AddRange(Encoding.ASCII.GetBytes(label));
        }

        packet.Add(0);
        packet.AddRange(new byte[] { 0, 12 }); // PTR
        packet.AddRange(new byte[] { 0, 1 }); // IN

        return packet.ToArray();
    }

    /// <summary>
    /// Ответ mDNS говорит о службе WLED: в нём встречается её имя.
    /// </summary>
    public static bool IsWledAnswer(ReadOnlySpan<byte> packet)
    {
        // Флаг «ответ» в заголовке и метка «_wled» где-то в записях
        return packet.Length > 12 && (packet[2] & 0x80) != 0 && packet.IndexOf("_wled"u8) >= 0;
    }
}

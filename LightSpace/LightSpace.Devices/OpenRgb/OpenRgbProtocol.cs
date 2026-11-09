using System.Buffers.Binary;
using System.Text;
using LightSpace.Core;

namespace LightSpace.Devices.OpenRgb;

/// <summary>
/// Контроллер из ответа сервера OpenRGB: название, где он стоит, и сколько у него светодиодов.
/// </summary>
public sealed record OpenRgbController(int Index, string Name, string Vendor, string Serial, string Location, int LedCount);

/// <summary>
/// Пакеты SDK-сервера OpenRGB: заголовок «ORGB», номер устройства, тип и длина данных; все числа — little-endian.
/// </summary>
public static class OpenRgbProtocol
{
    public const int DefaultPort = 6742;

    /// <summary>
    /// Версия протокола, в которой клиент разбирает описание контроллеров; сервер отвечает не новее неё.
    /// </summary>
    public const uint ClientVersion = 3;

    public const int HeaderSize = 16;

    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint UpdateLeds = 1050;
    public const uint SetCustomMode = 1100;

    private static readonly byte[] Magic = "ORGB"u8.ToArray();

    public static byte[] BuildPacket(uint deviceIndex, uint packetId, ReadOnlySpan<byte> data)
    {
        var packet = new byte[HeaderSize + data.Length];
        var span = packet.AsSpan();

        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], deviceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)data.Length);
        data.CopyTo(span[HeaderSize..]);

        return packet;
    }

    /// <summary>
    /// Разбирает заголовок пакета: номер устройства, тип и длину данных.
    /// </summary>
    /// <exception cref="InvalidDataException">Это не пакет OpenRGB.</exception>
    public static (uint DeviceIndex, uint PacketId, int Length) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || !header[..4].SequenceEqual(Magic))
            throw new InvalidDataException("Ответ сервера — не пакет OpenRGB");

        return (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]));
    }

    public static byte[] UInt32(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);

        return data;
    }

    /// <summary>
    /// Название клиента, которое сервер показывает в своём списке подключений.
    /// </summary>
    public static byte[] ClientName(string name)
    {
        return Encoding.ASCII.GetBytes(name + "\0");
    }

    /// <summary>
    /// Данные команды «задать цвета всех светодиодов»: общий размер, число цветов и цвета по 4 байта (R, G, B, 0).
    /// </summary>
    public static byte[] UpdateLedsData(IReadOnlyList<RgbColor> colors)
    {
        var size = 4 + 2 + colors.Count * 4;
        var data = new byte[size];
        var span = data.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)colors.Count);

        for (var i = 0; i < colors.Count; i++)
        {
            data[6 + i * 4] = colors[i].R;
            data[7 + i * 4] = colors[i].G;
            data[8 + i * 4] = colors[i].B;
        }

        return data;
    }

    /// <summary>
    /// Разбирает описание контроллера в формате версии 3 протокола.
    /// </summary>
    /// <exception cref="InvalidDataException">Описание обрезано или испорчено.</exception>
    public static OpenRgbController ParseController(int index, byte[] data)
    {
        var reader = new Reader(data);

        reader.UInt32(); // размер описания
        reader.Int32(); // тип устройства

        var name = reader.String();
        var vendor = reader.String();

        reader.String(); // описание
        reader.String(); // версия

        var serial = reader.String();
        var location = reader.String();

        var modes = reader.UInt16();
        reader.Int32(); // активный режим

        for (var i = 0; i < modes; i++)
        {
            reader.String(); // название режима

            // value, flags, speed min/max, brightness min/max, colors min/max, speed, brightness, direction, color mode
            reader.Skip(12 * 4);
            reader.Skip(reader.UInt16() * 4);
        }

        var zones = reader.UInt16();

        for (var i = 0; i < zones; i++)
        {
            reader.String(); // название зоны
            reader.Skip(4 * 4); // тип, leds min/max/count
            reader.Skip(reader.UInt16()); // матрица клавиш
        }

        var leds = reader.UInt16();

        return new OpenRgbController(index, name, vendor, serial, location, leds);
    }

    /// <summary>
    /// Последовательное чтение описания с проверкой границ.
    /// </summary>
    private sealed class Reader
    {
        private readonly byte[] _data;
        private int _position;

        public Reader(byte[] data)
        {
            _data = data;
        }

        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        /// <summary>
        /// Строка: длина с завершающим нулём, затем байты.
        /// </summary>
        public string String()
        {
            var length = UInt16();

            return Encoding.UTF8.GetString(Take(length)).TrimEnd('\0');
        }

        public void Skip(int count) => Take(count);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _position + count > _data.Length)
                throw new InvalidDataException("Описание контроллера OpenRGB обрезано");

            var span = _data.AsSpan(_position, count);
            _position += count;

            return span;
        }
    }
}

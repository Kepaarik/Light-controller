using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LightSpace.Core;
using LightSpace.Devices.OpenRgb;
using LightSpace.Devices.Tuya;
using LightSpace.Devices.Wled;

namespace LightSpace.Tests;

/// <summary>
/// Байты WLED (DDP), OpenRGB SDK и Tuya 3.4 / 3.5.
/// </summary>
public sealed class NewDevicePacketTests
{
    private static readonly byte[] Key = Encoding.ASCII.GetBytes("0123456789abcdef");

    [Fact]
    public void DdpPacketCarriesColorsAndPushFlag()
    {
        var colors = new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) };

        var packet = Assert.Single(WledProtocol.BuildDdpPackets(colors, 7));

        Assert.Equal(new byte[] { 0x41, 0x07, 0x0B, 0x01, 0, 0, 0, 0, 0, 6, 1, 2, 3, 4, 5, 6 }, packet);
    }

    [Fact]
    public void LongStripIsSplitAndOnlyLastPacketPushes()
    {
        var colors = Enumerable.Repeat(new RgbColor(9, 9, 9), WledProtocol.LedsPerPacket + 10).ToArray();

        var packets = WledProtocol.BuildDdpPackets(colors, 1);

        Assert.Equal(2, packets.Count);
        Assert.Equal(0x40, packets[0][0]);
        Assert.Equal(0x41, packets[1][0]);
        Assert.Equal((uint)(WledProtocol.LedsPerPacket * 3), BinaryPrimitives.ReadUInt32BigEndian(packets[1].AsSpan(4)));
        Assert.Equal(30, BinaryPrimitives.ReadUInt16BigEndian(packets[1].AsSpan(8)));
    }

    [Fact]
    public void MdnsQueryAsksForWledService()
    {
        var query = WledProtocol.BuildMdnsQuery();

        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)));
        Assert.True(query.AsSpan().IndexOf("\u0005_wled\u0004_tcp\u0005local\0"u8) > 0);

        // Свой же запрос — не ответ
        Assert.False(WledProtocol.IsWledAnswer(query));
    }

    [Fact]
    public void OpenRgbPacketHasLittleEndianHeader()
    {
        var packet = OpenRgbProtocol.BuildPacket(2, OpenRgbProtocol.UpdateLeds, new byte[] { 0xAA });

        Assert.Equal("ORGB"u8.ToArray(), packet[..4]);
        Assert.Equal((2u, OpenRgbProtocol.UpdateLeds, 1), OpenRgbProtocol.ParseHeader(packet));
        Assert.Equal(0xAA, packet[16]);
    }

    [Fact]
    public void OpenRgbUpdateLedsHasSizeCountAndPaddedColors()
    {
        var data = OpenRgbProtocol.UpdateLedsData(new[] { new RgbColor(10, 20, 30) });

        Assert.Equal(new byte[] { 10, 0, 0, 0, 1, 0, 10, 20, 30, 0 }, data);
    }

    [Fact]
    public void OpenRgbControllerIsParsedFromVersion3Description()
    {
        var data = new List<byte>();

        void UInt32(uint v) => data.AddRange(BitConverter.GetBytes(v));
        void UInt16(ushort v) => data.AddRange(BitConverter.GetBytes(v));
        void Str(string s)
        {
            UInt16((ushort)(s.Length + 1));
            data.AddRange(Encoding.ASCII.GetBytes(s));
            data.Add(0);
        }

        UInt32(0); // размер
        UInt32(0); // тип
        Str("ASUS Aura");
        Str("ASUS");
        Str("Описание");
        Str("1.0");
        Str("SN1");
        Str("HID: /dev/1");

        UInt16(1); // режимы
        UInt32(0); // активный
        Str("Direct");
        for (var i = 0; i < 12; i++)
            UInt32(0);
        UInt16(1);
        UInt32(0xFF0000);

        UInt16(1); // зоны
        Str("Zone");
        for (var i = 0; i < 4; i++)
            UInt32(5);
        UInt16(16);
        data.AddRange(new byte[16]);

        UInt16(5); // светодиоды

        var controller = OpenRgbProtocol.ParseController(3, data.ToArray());

        Assert.Equal(new OpenRgbController(3, "ASUS Aura", "ASUS", "SN1", "HID: /dev/1", 5), controller);
    }

    [Fact]
    public void TruncatedOpenRgbDescriptionIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => OpenRgbProtocol.ParseController(0, new byte[6]));
    }

    [Fact]
    public void Tuya34FrameIsSignedAndParsedBack()
    {
        var payload = TuyaProtocol.Encrypt(Key, "hello"u8.ToArray());
        var frame = TuyaProtocol.BuildFrame34(5, TuyaProtocol.CommandSessionStart, payload, Key);

        Assert.Equal(TuyaProtocol.Prefix, BinaryPrimitives.ReadUInt32BigEndian(frame));
        Assert.Equal(HMACSHA256.HashData(Key, frame[..(16 + payload.Length)]), frame[(16 + payload.Length)..^4]);
        Assert.Equal(TuyaProtocol.Suffix, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(frame.Length - 4)));

        var (command, parsed) = TuyaProtocol.ParseFrame34(frame);

        Assert.Equal(TuyaProtocol.CommandSessionStart, command);
        Assert.Equal("hello"u8.ToArray(), TuyaProtocol.Decrypt(Key, parsed));
    }

    [Fact]
    public void Tuya34ReplyReturnCodeIsStripped()
    {
        var encrypted = TuyaProtocol.Encrypt(Key, "ok"u8.ToArray());
        var frame = TuyaProtocol.BuildFrame34(1, TuyaProtocol.CommandSessionResponse, new byte[4].Concat(encrypted).ToArray(), Key);

        var (_, parsed) = TuyaProtocol.ParseFrame34(frame);

        Assert.Equal("ok"u8.ToArray(), TuyaProtocol.Decrypt(Key, parsed));
    }

    [Fact]
    public void Tuya35FrameRoundTripsAndRejectsWrongKey()
    {
        var nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var frame = TuyaProtocol.BuildFrame35(9, TuyaProtocol.CommandControlNew, "{\"a\":1}"u8.ToArray(), Key, nonce);

        Assert.Equal(TuyaProtocol.Prefix35, BinaryPrimitives.ReadUInt32BigEndian(frame));
        Assert.Equal(TuyaProtocol.Suffix35, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(frame.Length - 4)));
        Assert.Equal(nonce, frame[TuyaProtocol.Header35Size..(TuyaProtocol.Header35Size + 12)]);

        var (command, payload) = TuyaProtocol.ParseFrame35(frame, Key);

        Assert.Equal(TuyaProtocol.CommandControlNew, command);
        Assert.Equal("{\"a\":1}"u8.ToArray(), payload);

        var wrongKey = Encoding.ASCII.GetBytes("fedcba9876543210");
        Assert.ThrowsAny<CryptographicException>(() => TuyaProtocol.ParseFrame35(frame, wrongKey));
    }

    [Fact]
    public void Tuya34SessionKeyIsEncryptedNonceXor()
    {
        var local = Enumerable.Repeat((byte)0x0F, 16).ToArray();
        var remote = Enumerable.Repeat((byte)0xF0, 16).ToArray();

        var key = TuyaProtocol.SessionKey("3.4", Key, local, remote);

        Assert.Equal(TuyaProtocol.EncryptBlock(Key, Enumerable.Repeat((byte)0xFF, 16).ToArray()), key);
    }

    [Fact]
    public void Tuya35SessionKeyDiffersFrom34AndIs16Bytes()
    {
        var local = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var remote = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();

        var key35 = TuyaProtocol.SessionKey("3.5", Key, local, remote);

        Assert.Equal(16, key35.Length);
        Assert.NotEqual(TuyaProtocol.SessionKey("3.4", Key, local, remote), key35);
    }
}

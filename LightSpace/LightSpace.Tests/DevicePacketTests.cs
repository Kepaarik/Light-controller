using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using LightSpace.Devices.Ble;
using LightSpace.Devices.Tuya;

namespace LightSpace.Tests;

/// <summary>
/// Байты команд, которые уходят на контроллеры ELK/MELK и Tuya.
/// </summary>
public sealed class DevicePacketTests
{
    [Fact]
    public void ElkEffectCommandCarriesEffectNumber()
    {
        Assert.Equal(new byte[] { 0x7E, 0x05, 0x03, 55, 0x06, 0xFF, 0xFF, 0x00, 0xEF }, ElkModel.EffectCommand(55));
    }

    [Theory]
    [InlineData(0, 0x00)]
    [InlineData(50, 0x3A)]
    [InlineData(100, 0x74)]
    [InlineData(150, 0x74)]
    [InlineData(-10, 0x00)]
    public void ElkSpeedNeverExceedsTheFastestSpeedAllEffectsSurvive(int speed, byte expected)
    {
        var command = ElkModel.EffectSpeedCommand(speed);

        Assert.Equal(new byte[] { 0x7E, 0x04, 0x02, expected, 0xFF, 0xFF, 0xFF, 0x00, 0xEF }, command);
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    public void ElkBrightnessIsClampedToPercent(int brightness, byte expected)
    {
        Assert.Equal(new byte[] { 0x7E, 0x04, 0x01, expected, 0x01, 0xFF, 0xFF, 0x00, 0xEF }, ElkModel.BrightnessCommand(brightness));
    }

    [Theory]
    [InlineData("MELK-OA21", "MELK-OA21")]
    [InlineData("melk-oa2116", "MELK-OA21")]
    [InlineData("MELK-OA21 16", "MELK-OA21")]
    [InlineData("MELK-OC10", "MELK-OC10")]
    public void ElkModelIsFoundByAdvertisedName(string name, string model)
    {
        Assert.Equal(model, ElkModel.Find(name)?.Name);
    }

    [Fact]
    public void UnknownElkModelIsNotFound()
    {
        Assert.Null(ElkModel.Find("ELK-BLEDOM"));
    }

    [Fact]
    public void TuyaFrameHasHeaderLengthCrcAndSuffix()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var frame = TuyaProtocol.BuildFrame(7, TuyaProtocol.CommandControl, payload);
        var span = frame.AsSpan();

        Assert.Equal(16 + payload.Length + 8, frame.Length);
        Assert.Equal(TuyaProtocol.Prefix, BinaryPrimitives.ReadUInt32BigEndian(span[0..]));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32BigEndian(span[4..]));
        Assert.Equal(TuyaProtocol.CommandControl, BinaryPrimitives.ReadUInt32BigEndian(span[8..]));
        Assert.Equal((uint)(payload.Length + 8), BinaryPrimitives.ReadUInt32BigEndian(span[12..]));
        Assert.Equal(payload, span.Slice(16, payload.Length).ToArray());
        Assert.Equal(Crc32.HashToUInt32(span[..(16 + payload.Length)]), BinaryPrimitives.ReadUInt32BigEndian(span[(16 + payload.Length)..]));
        Assert.Equal(TuyaProtocol.Suffix, BinaryPrimitives.ReadUInt32BigEndian(span[^4..]));
    }

    [Fact]
    public void TuyaEncryptionRoundTrips()
    {
        var key = Encoding.ASCII.GetBytes("0123456789abcdef");
        var data = Encoding.UTF8.GetBytes("""{"dps":{"20":true}}""");

        var encrypted = TuyaProtocol.Encrypt(key, data);

        Assert.Equal(0, encrypted.Length % 16);
        Assert.Equal(data, TuyaProtocol.Decrypt(key, encrypted));
    }
}

using LightSpace.Devices.Robobloq;

namespace LightSpace.Tests;

/// <summary>
/// Разбор ответа ленты Robobloq на запрос параметров.
/// </summary>
public sealed class RobobloqTests
{
    // Настоящий ответ ленты на 24" с прошивкой 1.7.3 из журнала: пришёл с номером пакета 02, когда ждали 03
    private const string LateReply =
        "00 52 42 19 02 82 00 06 03 18 01 00 36 CD AB 48 71 19 BC 2E D9 E5 01 07 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 "
        + "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    [Fact]
    public void LateReplyToEarlierRequestIsAccepted()
    {
        var info = RobobloqDevice.ParseDeviceInfo(Bytes(LateReply));

        Assert.NotNull(info);
        Assert.Equal(24, info.ScreenSize);
        Assert.Equal(54, info.LedCount);
        Assert.Equal("cdab487119bc2ed9", info.Serial);
        Assert.Equal("1.7.3", info.Firmware);
    }

    [Fact]
    public void ReplyToOtherCommandIsIgnored()
    {
        var report = Bytes(LateReply);

        // Тот же ответ, но на команду цвета
        report[5] = 0x86;

        Assert.Null(RobobloqDevice.ParseDeviceInfo(report));
    }

    [Fact]
    public void ForeignOrShortReportIsIgnored()
    {
        var foreign = Bytes(LateReply);

        foreign[1] = 0x53;

        Assert.Null(RobobloqDevice.ParseDeviceInfo(foreign));
        Assert.Null(RobobloqDevice.ParseDeviceInfo(Bytes(LateReply)[..24]));
        Assert.Null(RobobloqDevice.ParseDeviceInfo(Array.Empty<byte>()));
    }

    private static byte[] Bytes(string hex)
    {
        return hex.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
    }
}

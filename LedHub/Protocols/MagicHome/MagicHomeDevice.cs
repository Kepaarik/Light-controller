using System.Net;
using LedHub.Core;

namespace LedHub.Protocols.MagicHome;

/// <summary>
/// Wi-Fi контроллер Magic Home (LEDENET), управляемый по TCP.
/// </summary>
public sealed class MagicHomeDevice : LedDevice
{
    private const int ControlPort = 5577;

    private readonly IPAddress _ip;
    private readonly string _mac;
    private readonly TcpConnection _connection;

    public MagicHomeDevice(IPAddress ip, string mac, string model)
    {
        _ip = ip;
        _mac = mac;
        _connection = new TcpConnection(new IPEndPoint(ip, ControlPort));
        Name = $"{model} ({mac[^4..]})";
    }

    public override string Id => $"magichome:{_mac}";

    public override string Name { get; }

    public override string Protocol => "Wi-Fi · Magic Home";

    public override string Address => _ip.ToString();

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return SendAsync(new byte[] { 0x71, isOn ? (byte)0x23 : (byte)0x24, 0x0F }, ct);
    }

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        var c = color.Scale(brightness);

        return SendAsync(new byte[] { 0x31, c.R, c.G, c.B, 0x00, 0xF0, 0x0F }, ct);
    }

    public override void Dispose()
    {
        _connection.Dispose();
    }

    private Task SendAsync(byte[] command, CancellationToken ct)
    {
        // Каждая команда завершается байтом контрольной суммы
        var checksum = (byte)command.Sum(b => (int)b);

        return _connection.SendAsync(command.Append(checksum).ToArray(), ct);
    }
}

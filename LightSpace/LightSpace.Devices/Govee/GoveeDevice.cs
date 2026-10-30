using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LightSpace.Core;

namespace LightSpace.Devices.Govee;

/// <summary>
/// Лента Govee с включённым LAN-управлением.
/// </summary>
public sealed class GoveeDevice : LedDevice
{
    private const int ControlPort = 4003;

    private readonly IPEndPoint _endPoint;
    private readonly string _deviceId;
    private readonly UdpClient _udp = new();

    public GoveeDevice(IPAddress ip, string deviceId, string sku)
    {
        _endPoint = new IPEndPoint(ip, ControlPort);
        _deviceId = deviceId;
        Name = $"Govee {sku}";
    }

    public override string Id => $"govee:{_deviceId}";

    public override string Name { get; }

    public override string Protocol => "Wi-Fi · Govee";

    public override string Address => _endPoint.Address.ToString();

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return SendAsync("turn", new { value = isOn ? 1 : 0 }, ct);
    }

    public override async Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        await SendAsync("colorwc", new { color = new { r = color.R, g = color.G, b = color.B }, colorTemInKelvin = 0 }, ct);
        await SendAsync("brightness", new { value = Math.Clamp(brightness, 1, 100) }, ct);
    }

    public override void Dispose()
    {
        _udp.Dispose();
    }

    private async Task SendAsync(string cmd, object data, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { msg = new { cmd, data } });

        await _udp.SendAsync(json, _endPoint, ct);
    }
}

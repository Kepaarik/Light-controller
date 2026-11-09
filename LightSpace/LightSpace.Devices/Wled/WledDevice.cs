using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using LightSpace.Core;

namespace LightSpace.Devices.Wled;

/// <summary>
/// Лента на ESP с прошивкой WLED: кадры идут по DDP, включение и выключение — через JSON API.
/// Без кадров дольше пары секунд WLED сам возвращается к своему эффекту.
/// </summary>
public sealed class WledDevice : LedDevice, IAddressableLedDevice
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly IPAddress _ip;
    private readonly string _mac;
    private readonly UdpClient _udp = new();
    private byte _sequence;

    public WledDevice(IPAddress ip, string mac, string name, int ledCount)
    {
        _ip = ip;
        _mac = mac;
        Name = name;
        LedCount = ledCount;
    }

    public override string Id => $"wled:{_mac}";

    public override string Name { get; }

    public override string Protocol => "Wi-Fi · WLED";

    public override string Address => _ip.ToString();

    public int LedCount { get; }

    /// <summary>
    /// WLED и ESP спокойно принимают 40 кадров в секунду.
    /// </summary>
    public override TimeSpan MinFrameInterval => TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Выключенная с телефона лента WLED кадры DDP не показывает.
    /// </summary>
    public override bool NeedsPowerOnAtStart => true;

    public override async Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync($"http://{_ip}/json/state", new { on = isOn }, ct);

        response.EnsureSuccessStatusCode();
    }

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        return SetLedColorsAsync(Enumerable.Repeat(color, LedCount).ToArray(), brightness, ct);
    }

    public async Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        var scaled = new RgbColor[LedCount];

        for (var i = 0; i < scaled.Length; i++)
            scaled[i] = (i < colors.Count ? colors[i] : new RgbColor(0, 0, 0)).Scale(brightness);

        var endPoint = new IPEndPoint(_ip, WledProtocol.DdpPort);

        // Номер кадра 1–15 помогает WLED отбросить пакеты, пришедшие не по порядку
        _sequence = (byte)(_sequence % 15 + 1);

        foreach (var packet in WledProtocol.BuildDdpPackets(scaled, _sequence))
            await _udp.SendAsync(packet, endPoint, ct);
    }

    public override void Dispose()
    {
        _udp.Dispose();
    }
}

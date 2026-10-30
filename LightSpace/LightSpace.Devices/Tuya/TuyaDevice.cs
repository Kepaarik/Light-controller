using System.Net;
using System.Text;
using System.Text.Json;
using LightSpace.Core;

namespace LightSpace.Devices.Tuya;

/// <summary>
/// Wi-Fi лента Tuya / Smart Life, управляемая по локальной сети.
/// </summary>
public sealed class TuyaDevice : LedDevice
{
    private const int ControlPort = 6668;
    private const string SupportedVersion = "3.3";

    private readonly IPAddress _ip;
    private readonly TcpConnection _connection;
    private uint _sequence;

    public TuyaDevice(string deviceId, IPAddress ip, string version)
    {
        DeviceId = deviceId;
        Version = version;
        _ip = ip;
        _connection = new TcpConnection(new IPEndPoint(ip, ControlPort));
    }

    public string DeviceId { get; }

    public string Version { get; }

    /// <summary>
    /// Локальный ключ устройства (16 символов); без него Tuya не принимает команды.
    /// </summary>
    public string? LocalKey { get; set; }

    public TuyaDpScheme DpScheme { get; set; } = TuyaDpScheme.V2;

    public override string Id => $"tuya:{DeviceId}";

    public override string Name => $"Tuya ({DeviceId[^4..]})";

    public override string Protocol => $"Wi-Fi · Tuya {Version}";

    public override string Address => _ip.ToString();

    public override bool IsReady => StatusText == null;

    public override string? StatusText
    {
        get
        {
            if (Version != SupportedVersion)
                return $"Протокол Tuya {Version} пока не поддерживается";

            if (LocalKey is not { Length: 16 })
                return "Нужен Local Key (16 символов)";

            return null;
        }
    }

    private string SwitchDp => DpScheme == TuyaDpScheme.V2 ? "20" : "1";

    private string ModeDp => DpScheme == TuyaDpScheme.V2 ? "21" : "2";

    private string ColourDp => DpScheme == TuyaDpScheme.V2 ? "24" : "5";

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return SendDpsAsync(new Dictionary<string, object> { [SwitchDp] = isOn }, ct);
    }

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        var dps = new Dictionary<string, object>
        {
            [ModeDp] = "colour",
            [ColourDp] = FormatColour(color, brightness)
        };

        return SendDpsAsync(dps, ct);
    }

    public override void Dispose()
    {
        _connection.Dispose();
    }

    private string FormatColour(RgbColor color, int brightness)
    {
        var (hue, saturation, value) = color.ToHsv();
        var level = value * Math.Clamp(brightness, 1, 100) / 100.0;
        var h = (int)Math.Round(hue);

        // V2: "HHHHSSSSVVVV", тон 0–360, насыщенность и яркость 0–1000
        if (DpScheme == TuyaDpScheme.V2)
            return $"{h:x4}{(int)Math.Round(saturation * 1000):x4}{Math.Max(10, (int)Math.Round(level * 1000)):x4}";

        // V1: "RRGGBB" + "0HHH" + "SS" + "VV", насыщенность и яркость 0–255
        var c = color.Scale(brightness);
        return $"{c.R:x2}{c.G:x2}{c.B:x2}{h:x4}{(int)Math.Round(saturation * 255):x2}{(int)Math.Round(level * 255):x2}";
    }

    private async Task SendDpsAsync(Dictionary<string, object> dps, CancellationToken ct)
    {
        if (!IsReady)
            throw new InvalidOperationException(StatusText);

        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            devId = DeviceId,
            uid = DeviceId,
            t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            dps
        });

        var encrypted = TuyaProtocol.Encrypt(Encoding.ASCII.GetBytes(LocalKey!), json);

        // В 3.3 зашифрованной команде предшествует заголовок: версия и 12 нулевых байт
        var payload = Encoding.ASCII.GetBytes(SupportedVersion)
            .Concat(new byte[12])
            .Concat(encrypted)
            .ToArray();

        var frame = TuyaProtocol.BuildFrame(Interlocked.Increment(ref _sequence), TuyaProtocol.CommandControl, payload);

        await _connection.SendAsync(frame, ct);
    }
}

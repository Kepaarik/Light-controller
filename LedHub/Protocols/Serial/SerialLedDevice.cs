using System.IO.Ports;
using System.Text;
using LedHub.Core;

namespace LedHub.Protocols.Serial;

/// <summary>
/// Лента на Arduino с прошивкой LedHub, подключённая по USB.
/// </summary>
public sealed class SerialLedDevice : LedDevice, IAddressableLedDevice
{
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SerialLedDevice(SerialPort port, string name, int ledCount)
    {
        _port = port;
        Name = name;
        LedCount = ledCount;
    }

    /// <summary>
    /// Число светодиодов из прошивки; 0 у старой прошивки без команды COUNT.
    /// </summary>
    public int LedCount { get; }

    /// <summary>
    /// Идентификатор строится по имени из прошивки, а не по COM-порту, который может меняться.
    /// </summary>
    public override string Id => $"serial:{Name}";

    public override string Name { get; }

    public override string Protocol => "USB · Arduino";

    public override string Address => _port.PortName;

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return WriteLineAsync(isOn ? "ON" : "OFF", ct);
    }

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        var c = color.Scale(brightness);

        return WriteLineAsync($"C {c.R} {c.G} {c.B}", ct);
    }

    public Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        // Кадр: "F", затем RRGGBB каждого светодиода в шестнадцатеричном виде
        var frame = new StringBuilder("F ", 2 + colors.Count * 6);

        foreach (var color in colors)
        {
            var c = color.Scale(brightness);
            frame.Append($"{c.R:X2}{c.G:X2}{c.B:X2}");
        }

        return WriteLineAsync(frame.ToString(), ct);
    }

    public override void Dispose()
    {
        _port.Dispose();
        _lock.Dispose();
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(() => _port.WriteLine(line), ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}

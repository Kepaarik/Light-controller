using System.IO;
using System.IO.Ports;
using LedHub.Core;

namespace LedHub.Protocols.Serial;

/// <summary>
/// Опрашивает все COM-порты командой PING и ждёт ответа "LEDHUB &lt;имя&gt;" от прошивки.
/// </summary>
public sealed class SerialDiscoverer : IDeviceDiscoverer
{
    private const int BaudRate = 115200;
    private const string HandshakePrefix = "LEDHUB ";
    private const int PingAttempts = 3;

    // Большинство Arduino перезагружаются при открытии порта — ждём, пока стартует прошивка
    private static readonly TimeSpan BootDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromMilliseconds(700);

    public string Protocol => "USB / COM";

    public async Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct)
    {
        var probes = SerialPort.GetPortNames()
            .Distinct()
            .Select(portName => Task.Run(() => TryProbe(portName, ct), ct));

        var devices = await Task.WhenAll(probes);

        return devices.OfType<LedDevice>().ToList();
    }

    private static SerialLedDevice? TryProbe(string portName, CancellationToken ct)
    {
        var port = new SerialPort(portName, BaudRate)
        {
            NewLine = "\n",
            ReadTimeout = 200,
            WriteTimeout = 500,
            DtrEnable = true
        };

        try
        {
            port.Open();
            ct.WaitHandle.WaitOne(BootDelay);

            for (var attempt = 0; attempt < PingAttempts && !ct.IsCancellationRequested; attempt++)
            {
                port.DiscardInBuffer();
                port.WriteLine("PING");

                var name = TryReadHandshake(port);
                if (name != null)
                    return new SerialLedDevice(port, name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            // Порт занят другой программой или это не наше устройство
        }

        port.Dispose();
        return null;
    }

    private static string? TryReadHandshake(SerialPort port)
    {
        var deadline = DateTime.UtcNow + HandshakeTimeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var line = port.ReadLine().Trim();

                if (line.StartsWith(HandshakePrefix, StringComparison.Ordinal))
                    return line[HandshakePrefix.Length..].Trim();
            }
            catch (TimeoutException)
            {
            }
        }

        return null;
    }
}

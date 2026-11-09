using System.Net;
using LightSpace.Core;

namespace LightSpace.Devices.OpenRgb;

/// <summary>
/// Контроллер подсветки, которым управляет запущенный OpenRGB: материнская плата, память, видеокарта, мышь и другие.
/// У каждого контроллера своё соединение с сервером; оборванное открывается заново при следующей команде.
/// </summary>
public sealed class OpenRgbDevice : LedDevice, IAddressableLedDevice
{
    private readonly IPEndPoint _server;
    private readonly OpenRgbController _controller;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private OpenRgbClient? _client;

    public OpenRgbDevice(IPEndPoint server, OpenRgbController controller)
    {
        _server = server;
        _controller = controller;
    }

    /// <summary>
    /// Номер контроллера на сервере меняется при перезапуске OpenRGB, поэтому устройство узнаётся по названию и месту.
    /// </summary>
    public override string Id => $"openrgb:{_controller.Name}:{(_controller.Serial.Length > 0 ? _controller.Serial : _controller.Location)}";

    public override string Name => _controller.Name;

    public override string Protocol => _controller.Vendor.Length > 0 ? $"OpenRGB · {_controller.Vendor}" : "OpenRGB";

    public override string Address => $"{_server} #{_controller.Index}";

    public int LedCount => _controller.LedCount;

    /// <summary>
    /// Подсветку на платах и памяти OpenRGB выключает только чёрным цветом.
    /// </summary>
    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return isOn ? Task.CompletedTask : SetColorAsync(new RgbColor(0, 0, 0), 0, ct);
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

        await _lock.WaitAsync(ct);
        try
        {
            try
            {
                await SendLedsAsync(scaled, ct);
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or ObjectDisposedException)
            {
                // OpenRGB могли перезапустить — подключаемся заново один раз
                CloseClient();
                await SendLedsAsync(scaled, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public override void Dispose()
    {
        CloseClient();
        _lock.Dispose();
    }

    private async Task SendLedsAsync(RgbColor[] colors, CancellationToken ct)
    {
        if (_client == null)
        {
            _client = await OpenRgbClient.ConnectAsync(_server, ct);

            // Свой режим нужен, чтобы контроллер показывал присланные цвета, а не свой эффект
            await _client.SendAsync((uint)_controller.Index, OpenRgbProtocol.SetCustomMode, ReadOnlyMemory<byte>.Empty, ct);
        }

        await _client.SendAsync((uint)_controller.Index, OpenRgbProtocol.UpdateLeds, OpenRgbProtocol.UpdateLedsData(colors), ct);
    }

    private void CloseClient()
    {
        _client?.Dispose();
        _client = null;
    }
}

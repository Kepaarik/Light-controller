using LedHub.Core;
using LedHub.Protocols.Ble;
using LedHub.Protocols.Govee;
using LedHub.Protocols.MagicHome;
using LedHub.Protocols.Robobloq;
using LedHub.Protocols.Serial;
using LedHub.Protocols.Tuya;

namespace LedHub.Services;

public sealed record DiscoveryResult(IReadOnlyList<LedDevice> Devices, IReadOnlyList<string> Errors);

/// <summary>
/// Одновременно ищет ленты всеми поддерживаемыми способами подключения.
/// </summary>
public sealed class DiscoveryService
{
    // Tuya рассылает анонсы раз в ~5 секунд, поэтому слушаем с запасом
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(8);

    private readonly IDeviceDiscoverer[] _discoverers =
    {
        new BleDiscoverer(),
        new MagicHomeDiscoverer(),
        new GoveeDiscoverer(),
        new TuyaDiscoverer(),
        new SerialDiscoverer(),
        new RobobloqDiscoverer()
    };

    public async Task<DiscoveryResult> DiscoverAllAsync(CancellationToken ct)
    {
        var errors = new List<string>();

        var results = await Task.WhenAll(_discoverers.Select(d => DiscoverSafeAsync(d, errors, ct)));

        return new DiscoveryResult(results.SelectMany(r => r).ToList(), errors);
    }

    /// <summary>
    /// Ошибка одного способа подключения (например, выключенный Bluetooth) не должна мешать остальным.
    /// </summary>
    private static async Task<IReadOnlyList<LedDevice>> DiscoverSafeAsync(IDeviceDiscoverer discoverer, List<string> errors, CancellationToken ct)
    {
        try
        {
            return await discoverer.DiscoverAsync(ScanDuration, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            lock (errors)
                errors.Add($"{discoverer.Protocol}: {e.Message}");

            return Array.Empty<LedDevice>();
        }
    }
}

namespace LightSpace.Devices;

/// <summary>
/// Поиск лент одного типа подключения.
/// </summary>
public interface IDeviceDiscoverer
{
    string Protocol { get; }

    Task<IReadOnlyList<LedDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken ct);
}

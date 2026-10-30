namespace LedHub.Core;

/// <summary>
/// Светодиодная лента, найденная при поиске, независимо от способа подключения.
/// </summary>
public abstract class LedDevice : IDisposable
{
    /// <summary>
    /// Постоянный идентификатор устройства; используется для хранения состава групп между запусками.
    /// </summary>
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Protocol { get; }

    public abstract string Address { get; }

    /// <summary>
    /// Готово ли устройство принимать команды.
    /// </summary>
    public virtual bool IsReady => true;

    /// <summary>
    /// Пояснение, почему устройством пока нельзя управлять; null, если всё в порядке.
    /// </summary>
    public virtual string? StatusText => null;

    /// <summary>
    /// Как часто устройство может принимать кадры эффекта; ноль — так часто, как их шлёт приложение.
    /// </summary>
    public virtual TimeSpan MinFrameInterval => TimeSpan.Zero;

    public abstract Task SetPowerAsync(bool isOn, CancellationToken ct = default);

    /// <summary>
    /// Устанавливает цвет ленты с заданной яркостью (0–100 %).
    /// </summary>
    public abstract Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default);

    public virtual void Dispose()
    {
    }
}

namespace LedHub.Core;

/// <summary>
/// Эффект, встроенный в контроллер ленты.
/// </summary>
public sealed record HardwareEffect(int Id, string Title);

/// <summary>
/// Лента с собственными эффектами: они выполняются контроллером и работают даже при закрытом приложении.
/// </summary>
public interface IHardwareEffectsDevice
{
    IReadOnlyList<HardwareEffect> HardwareEffects { get; }

    /// <summary>
    /// Запускает встроенный эффект со скоростью 0–100 %.
    /// </summary>
    Task StartHardwareEffectAsync(int effectId, int speed, CancellationToken ct = default);
}

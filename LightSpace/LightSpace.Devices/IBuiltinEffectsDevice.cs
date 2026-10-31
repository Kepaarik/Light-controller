using LightSpace.Core;

namespace LightSpace.Devices;

/// <summary>
/// Эффект, встроенный в устройство: его играет сам контроллер или прошивка устройства.
/// </summary>
/// <param name="Id">Номер эффекта у устройства.</param>
public sealed record BuiltinEffect(int Id, string Title);

/// <summary>
/// Устройство со встроенными эффектами: они идут сами, без кадров от приложения, и работают даже при закрытом приложении,
/// пока устройству не пришлют обычный цвет или кадр.
/// </summary>
public interface IBuiltinEffectsDevice
{
    /// <summary>
    /// Встроенные эффекты устройства; пусто, если эффекты этой модели неизвестны.
    /// </summary>
    IReadOnlyList<BuiltinEffect> BuiltinEffects { get; }

    /// <summary>
    /// Запускает встроенный эффект со скоростью и яркостью 0–100 % и цветом; без цвета эффект играет своими цветами.
    /// Яркость и цвет меняют только устройства, которые это умеют; остальные их пропускают.
    /// </summary>
    Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default);
}

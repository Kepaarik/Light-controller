namespace LightSpace.Engine.Frames;

/// <summary>
/// Сколько занимает расчёт кадра для всех устройств.
/// </summary>
/// <param name="Last">Расчёт последнего кадра.</param>
/// <param name="Average">Скользящее среднее по последним кадрам.</param>
/// <param name="Frames">Сколько кадров посчитано с запуска.</param>
public sealed record FrameTiming(TimeSpan Last, TimeSpan Average, long Frames)
{
    /// <summary>
    /// Ни одного кадра ещё не посчитано.
    /// </summary>
    public static readonly FrameTiming None = new(TimeSpan.Zero, TimeSpan.Zero, 0);
}

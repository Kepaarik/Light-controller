namespace LightSpace.Engine.Sources;

/// <summary>
/// Неизменяемый снимок звука: громкость, полосы спектра и счётчик ударов. Громкость и полосы уже приведены
/// автоусилением к 0–1, поэтому тихая и громкая музыка выглядят одинаково живо.
/// </summary>
/// <param name="Level">Громкость от 0 до 1.</param>
/// <param name="Bands">Полосы спектра от басов к высоким частотам, каждая от 0 до 1.</param>
/// <param name="Beats">Сколько ударов услышано с начала захвата; новый удар — когда число выросло.</param>
public sealed record AudioLevels(double Level, IReadOnlyList<double> Bands, long Beats)
{
    /// <summary>
    /// Сколько полос в спектре.
    /// </summary>
    public const int BandCount = 16;

    /// <summary>
    /// Тишина: звук не слушается, его нет или его не удалось получить.
    /// </summary>
    public static readonly AudioLevels Silent = new(0, new double[BandCount], 0);
}

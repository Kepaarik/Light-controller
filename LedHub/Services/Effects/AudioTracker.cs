namespace LedHub.Services.Effects;

/// <summary>
/// Слушает звук компьютера и на каждом кадре готовит для музыкальных эффектов громкость, спектр и удары.
/// </summary>
public sealed class AudioTracker : IDisposable
{
    // Как быстро гаснет свет после громкого места (доля, остающаяся за кадр)
    private const double Release = 0.85;

    // Как медленно забывается пик громкости — от этого зависит автоусиление
    private const double PeakDecay = 0.995;

    private const double MinimumPeak = 0.02;
    private const double MinimumBandPeak = 0.001;

    // Удар — когда басы громче своего недавнего среднего в столько раз
    private const double BeatThreshold = 1.5;
    private const int FramesBetweenBeats = 5;
    private const int MaxBeats = 12;

    private readonly AudioAnalyzer _analyzer = new();
    private readonly double[] _bandPeaks = new double[AudioAnalyzer.BandCount];
    private double _levelPeak = MinimumPeak;
    private double _bassAverage;
    private int _framesSinceBeat;

    public AudioTracker()
    {
        Array.Fill(_bandPeaks, MinimumBandPeak);
    }

    public AudioFrame Frame { get; } = new();

    /// <summary>
    /// Обновляет звук для кадра; time — время эффекта, к нему привязываются удары.
    /// </summary>
    public void Update(double time, double gain)
    {
        var (level, bands) = _analyzer.Snapshot();

        // Автоусиление: делим на недавний пик, который медленно спадает, — тихая и громкая музыка выглядят одинаково живо
        _levelPeak = Math.Max(level, Math.Max(_levelPeak * PeakDecay, MinimumPeak));
        Frame.Level = Follow(Frame.Level, Math.Clamp(level / _levelPeak * gain, 0, 1));

        for (var i = 0; i < bands.Length; i++)
        {
            _bandPeaks[i] = Math.Max(bands[i], Math.Max(_bandPeaks[i] * PeakDecay, MinimumBandPeak));
            Frame.Bands[i] = Follow(Frame.Bands[i], Math.Clamp(bands[i] / _bandPeaks[i] * gain, 0, 1));
        }

        var bass = (bands[0] + bands[1] + bands[2]) / 3;
        var isBeat = bass > _bassAverage * BeatThreshold && _framesSinceBeat >= FramesBetweenBeats && bass > MinimumBandPeak;
        _bassAverage = _bassAverage * 0.9 + bass * 0.1;
        _framesSinceBeat = isBeat ? 0 : _framesSinceBeat + 1;
        Frame.Bass = (Frame.Bands[0] + Frame.Bands[1] + Frame.Bands[2]) / 3;

        if (isBeat)
        {
            Frame.Hue = (Frame.Hue + 47) % 360;
            Frame.Beats.Add(new AudioBeat(time, Frame.Hue));

            if (Frame.Beats.Count > MaxBeats)
                Frame.Beats.RemoveAt(0);
        }

        // Между ударами цвет тоже медленно плывёт, чтобы не застывать на тихих местах
        Frame.Hue = (Frame.Hue + 0.4) % 360;
    }

    public void Dispose()
    {
        _analyzer.Dispose();
    }

    /// <summary>
    /// Быстрая атака и плавный спад, чтобы свет не дрожал.
    /// </summary>
    private static double Follow(double current, double target)
    {
        return target > current ? target : current * Release + target * (1 - Release);
    }
}

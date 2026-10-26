using LedHub.Core;

namespace LedHub.Services;

public enum MusicMode
{
    /// <summary>Яркость цвета кисти следует за громкостью; на адресной ленте — шкала от центра.</summary>
    Pulse,

    /// <summary>Басы, средние и высокие частоты — красный, зелёный и синий; на адресной ленте — полосы спектра.</summary>
    Spectrum,

    /// <summary>Цвет перескакивает по радуге на каждом ударе.</summary>
    RainbowBeat
}

/// <summary>
/// Музыка с компьютера: ленты реагируют на звук, который сейчас играет.
/// </summary>
public sealed class MusicSource : ILightSource, IDisposable
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

    private readonly AudioAnalyzer _analyzer = new();
    private readonly MusicMode _mode;
    private readonly double[] _bands = new double[AudioAnalyzer.BandCount];
    private readonly double[] _bandPeaks = new double[AudioAnalyzer.BandCount];
    private double _level;
    private double _levelPeak = MinimumPeak;
    private double _bassAverage;
    private int _framesSinceBeat;
    private double _hue;
    private RgbColor _color;

    public MusicSource(MusicMode mode)
    {
        _mode = mode;
        Array.Fill(_bandPeaks, MinimumBandPeak);
    }

    /// <summary>
    /// Чувствительность: больше 1 — ярче реагирует на тихую музыку.
    /// </summary>
    public double Gain { get; set; } = 1;

    public void BeginFrame(double phase, EffectSettings settings)
    {
        _color = settings.Color;
        var (level, bands) = _analyzer.Snapshot();

        // Автоусиление: делим на недавний пик, который медленно спадает, — тихая и громкая музыка выглядят одинаково живо
        _levelPeak = Math.Max(level, Math.Max(_levelPeak * PeakDecay, MinimumPeak));
        _level = Follow(_level, Math.Clamp(level / _levelPeak * Gain, 0, 1));

        for (var i = 0; i < bands.Length; i++)
        {
            _bandPeaks[i] = Math.Max(bands[i], Math.Max(_bandPeaks[i] * PeakDecay, MinimumBandPeak));
            _bands[i] = Follow(_bands[i], Math.Clamp(bands[i] / _bandPeaks[i] * Gain, 0, 1));
        }

        var bass = (bands[0] + bands[1] + bands[2]) / 3;
        var isBeat = bass > _bassAverage * BeatThreshold && _framesSinceBeat >= FramesBetweenBeats && bass > MinimumBandPeak;
        _bassAverage = _bassAverage * 0.9 + bass * 0.1;
        _framesSinceBeat = isBeat ? 0 : _framesSinceBeat + 1;

        if (isBeat)
            _hue = (_hue + 47) % 360;

        // Между ударами цвет тоже медленно плывёт, чтобы не застывать на тихих местах
        _hue = (_hue + 0.4) % 360;
    }

    public RgbColor RenderSolid()
    {
        switch (_mode)
        {
            case MusicMode.Spectrum:
                return new RgbColor(ToByte(BandAverage(0, 4)), ToByte(BandAverage(5, 10)), ToByte(BandAverage(11, 15)));

            case MusicMode.RainbowBeat:
                return RgbColor.FromHsv(_hue, 1, 0.1 + 0.9 * _level);

            default:
                return _color.Scale((int)Math.Round(8 + 92 * _level));
        }
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        var colors = new RgbColor[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            var position = (i + 0.5) / ledCount;
            colors[i] = _mode switch
            {
                MusicMode.Spectrum => SpectrumPixel(position),
                MusicMode.RainbowBeat => RgbColor.FromHsv(_hue + position * 90, 1, 0.1 + 0.9 * _level),
                _ => MeterPixel(position)
            };
        }

        return colors;
    }

    public void Dispose()
    {
        _analyzer.Dispose();
    }

    /// <summary>
    /// Шкала громкости от центра ленты к краям: зелёная в середине, красная у краёв.
    /// </summary>
    private RgbColor MeterPixel(double position)
    {
        var distance = Math.Abs(position * 2 - 1);

        return distance <= _level
            ? RgbColor.FromHsv(120 - 120 * distance, 1, 1)
            : new RgbColor(0, 0, 0);
    }

    /// <summary>
    /// Полосы спектра вдоль ленты: басы в начале, высокие частоты в конце.
    /// </summary>
    private RgbColor SpectrumPixel(double position)
    {
        var band = Math.Min(AudioAnalyzer.BandCount - 1, (int)(position * AudioAnalyzer.BandCount));

        return RgbColor.FromHsv(position * 270, 1, _bands[band]);
    }

    private double BandAverage(int from, int to)
    {
        double sum = 0;
        for (var i = from; i <= to; i++)
            sum += _bands[i];

        return sum / (to - from + 1);
    }

    /// <summary>
    /// Быстрая атака и плавный спад, чтобы свет не дрожал.
    /// </summary>
    private static double Follow(double current, double target)
    {
        return target > current ? target : current * Release + target * (1 - Release);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}

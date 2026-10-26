using NAudio.Dsp;
using NAudio.Wave;

namespace LedHub.Services;

/// <summary>
/// Слушает звук, который играет на компьютере (колонки, наушники), и считает громкость и спектр.
/// </summary>
public sealed class AudioAnalyzer : IDisposable
{
    public const int BandCount = 16;

    private const int FftPower = 10;
    private const int FftSize = 1 << FftPower;
    private const double LowestFrequency = 40;
    private const double HighestFrequency = 12000;

    // Без звука WASAPI не присылает данные; если их нет дольше этого, считаем, что тишина
    private const long SilenceAfterMs = 250;

    private readonly WasapiLoopbackCapture _capture;
    private readonly object _lock = new();
    private readonly float[] _samples = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _bands = new double[BandCount];
    private readonly int[] _bandEdges = new int[BandCount + 1];
    private int _writePosition;
    private double _level;
    private long _lastDataAt;

    public AudioAnalyzer()
    {
        _capture = new WasapiLoopbackCapture();

        // Границы полос в логарифмической шкале — так басы и высокие частоты получают равное внимание
        var binWidth = (double)_capture.WaveFormat.SampleRate / FftSize;
        for (var i = 0; i <= BandCount; i++)
        {
            var frequency = LowestFrequency * Math.Pow(HighestFrequency / LowestFrequency, i / (double)BandCount);
            _bandEdges[i] = Math.Clamp((int)(frequency / binWidth), 1, FftSize / 2 - 1);
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    /// <summary>
    /// Текущие громкость (RMS) и амплитуды полос спектра, без нормализации.
    /// </summary>
    public (double Level, double[] Bands) Snapshot()
    {
        lock (_lock)
        {
            if (Environment.TickCount64 - _lastDataAt > SilenceAfterMs)
                return (0, new double[BandCount]);

            return (_level, (double[])_bands.Clone());
        }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.StopRecording();
        _capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = _capture.WaveFormat;

        // Общий формат WASAPI — 32-битные числа с плавающей точкой; другие форматы не разбираем
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
            return;

        var channels = format.Channels;
        var frames = e.BytesRecorded / 4 / channels;

        lock (_lock)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                // Сводим каналы в моно
                float sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += BitConverter.ToSingle(e.Buffer, (frame * channels + channel) * 4);

                _samples[_writePosition] = sum / channels;
                _writePosition = (_writePosition + 1) % FftSize;
            }

            Analyze();
            _lastDataAt = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Считает громкость и спектр по последним FftSize сэмплам из кольцевого буфера.
    /// </summary>
    private void Analyze()
    {
        double energy = 0;

        for (var i = 0; i < FftSize; i++)
        {
            var sample = _samples[(_writePosition + i) % FftSize];
            energy += sample * sample;

            // Окно Ханна убирает «звон» на краях отрезка
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
            _fft[i].X = (float)(sample * window);
            _fft[i].Y = 0;
        }

        _level = Math.Sqrt(energy / FftSize);

        FastFourierTransform.FFT(true, FftPower, _fft);

        for (var band = 0; band < BandCount; band++)
        {
            var from = _bandEdges[band];
            var to = Math.Max(from + 1, _bandEdges[band + 1]);
            double magnitude = 0;

            for (var bin = from; bin < to; bin++)
                magnitude += Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y);

            _bands[band] = magnitude / (to - from);
        }
    }
}

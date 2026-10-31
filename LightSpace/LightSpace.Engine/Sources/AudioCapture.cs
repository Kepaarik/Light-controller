using System.Diagnostics;
using LightSpace.Core.Config;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Захват звука с одного входа в своём потоке: системный звук или микрофон. 50 раз в секунду считает громкость,
/// спектр и удары и публикует последний снимок. Рендер только читает снимок; пока звука нет или его не удалось
/// получить, снимок — тишина.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    private const int FftPower = 10;
    private const int FftSize = 1 << FftPower;
    private const double LowestFrequency = 40;
    private const double HighestFrequency = 12000;

    // Без звука WASAPI не присылает данные; если их нет дольше этого, считаем, что тишина
    private const long SilenceAfterMs = 250;

    private const double MinimumPeak = 0.02;
    private const double MinimumBandPeak = 0.001;

    // Удар — когда басы громче своего недавнего среднего в столько раз, но не чаще, чем раз в MinBeatInterval
    private const double BeatThreshold = 1.5;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MinBeatInterval = TimeSpan.FromMilliseconds(250);

    // После ошибки захват пробует снова не сразу, чтобы не крутиться впустую
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    // Как медленно забывается пик громкости — от этого зависит автоусиление: за секунду остаётся примерно 90 %
    private static readonly double PeakDecay = Math.Pow(0.9, Tick.TotalSeconds);

    // Вес нового значения в среднем громкости басов: среднее примерно за полсекунды
    private static readonly double BassAverageWeight = 1 - Math.Pow(0.12, Tick.TotalSeconds);

    private readonly ManualResetEventSlim _stop = new();
    private readonly AudioInput _input;

    // Кольцевой буфер последних сэмплов; его пишет поток NAudio, читает поток захвата
    private readonly object _lock = new();
    private readonly float[] _samples = new float[FftSize];
    private int _writePosition;
    private long _lastDataAt;

    // Снимок заменяется целиком атомарной записью ссылки
    private AudioLevels _levels = AudioLevels.Silent;

    public AudioCapture(AudioInput input)
    {
        _input = input;

        var thread = new Thread(Run) { IsBackground = true, Name = "Light Space: захват звука" };
        thread.Start();
    }

    /// <summary>
    /// Последний снимок звука; тишина — звука нет, захват ещё не начался или не удался.
    /// </summary>
    public AudioLevels Levels => Volatile.Read(ref _levels);

    /// <summary>
    /// Останавливает захват; поток сам освобождает устройство и не задерживает вызывающего.
    /// </summary>
    public void Dispose()
    {
        _stop.Set();
    }

    private void Run()
    {
        var isFailing = false;

        try
        {
            while (!_stop.IsSet)
            {
                try
                {
                    Listen();
                    isFailing = false;
                }
                catch (Exception e)
                {
                    // Ошибку пишем один раз за полосу неудач: отключённое устройство не находится при каждой попытке
                    if (!isFailing)
                        Trace.TraceWarning($"Light Space: не удалось слушать звук ({_input.Kind}): {e.Message}");

                    isFailing = true;
                    Volatile.Write(ref _levels, AudioLevels.Silent);
                    _stop.Wait(RetryDelay);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _levels, AudioLevels.Silent);
            _stop.Dispose();
        }
    }

    /// <summary>
    /// Слушает устройство, пока захват не остановят или устройство не перестанет отвечать.
    /// </summary>
    private void Listen()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = AudioDevices.Open(enumerator, _input);
        using WasapiCapture capture = _input.Kind == AudioInputKind.Microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);

        Exception? stoppedBy = null;
        var isStopped = false;

        capture.DataAvailable += (_, e) => OnDataAvailable(capture.WaveFormat, e);
        capture.RecordingStopped += (_, e) =>
        {
            stoppedBy = e.Exception;
            Volatile.Write(ref isStopped, true);
        };

        var analysis = new Analysis(capture.WaveFormat.SampleRate);

        capture.StartRecording();

        try
        {
            while (!_stop.Wait(Tick))
            {
                // Устройство отключили или сменили его формат — захват начнётся заново
                if (Volatile.Read(ref isStopped))
                    throw stoppedBy ?? new InvalidOperationException("Запись звука остановилась");

                Volatile.Write(ref _levels, Analyze(analysis));
            }
        }
        finally
        {
            if (!Volatile.Read(ref isStopped))
                capture.StopRecording();
        }
    }

    private void OnDataAvailable(WaveFormat format, WaveInEventArgs e)
    {
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

            _lastDataAt = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Считает громкость и спектр по последним FftSize сэмплам, приводит их автоусилением к 0–1 и ищет удары.
    /// </summary>
    private AudioLevels Analyze(Analysis analysis)
    {
        var fft = analysis.Fft;
        var energy = 0.0;

        lock (_lock)
        {
            if (Environment.TickCount64 - _lastDataAt > SilenceAfterMs)
                return new AudioLevels(0, new double[AudioLevels.BandCount], analysis.Beats);

            for (var i = 0; i < FftSize; i++)
            {
                var sample = _samples[(_writePosition + i) % FftSize];
                energy += sample * sample;

                // Окно Ханна убирает «звон» на краях отрезка
                var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
                fft[i].X = (float)(sample * window);
                fft[i].Y = 0;
            }
        }

        var level = Math.Sqrt(energy / FftSize);

        FastFourierTransform.FFT(true, FftPower, fft);

        var bands = new double[AudioLevels.BandCount];

        for (var band = 0; band < bands.Length; band++)
        {
            var from = analysis.BandEdges[band];
            var to = Math.Max(from + 1, analysis.BandEdges[band + 1]);
            var magnitude = 0.0;

            for (var bin = from; bin < to; bin++)
                magnitude += Math.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y);

            bands[band] = magnitude / (to - from);
        }

        // Удар ищем по сырым басам, до автоусиления
        var bass = (bands[0] + bands[1] + bands[2]) / 3;
        var isBeat = bass > analysis.BassAverage * BeatThreshold && bass > MinimumBandPeak &&
                     analysis.SinceBeat.Elapsed >= MinBeatInterval;

        analysis.BassAverage += (bass - analysis.BassAverage) * BassAverageWeight;

        if (isBeat)
        {
            analysis.Beats++;
            analysis.SinceBeat.Restart();
        }

        // Автоусиление: делим на недавний пик, который медленно спадает
        analysis.LevelPeak = Math.Max(level, Math.Max(analysis.LevelPeak * PeakDecay, MinimumPeak));

        for (var i = 0; i < bands.Length; i++)
        {
            analysis.BandPeaks[i] = Math.Max(bands[i], Math.Max(analysis.BandPeaks[i] * PeakDecay, MinimumBandPeak));
            bands[i] = Math.Clamp(bands[i] / analysis.BandPeaks[i], 0, 1);
        }

        return new AudioLevels(Math.Clamp(level / analysis.LevelPeak, 0, 1), bands, analysis.Beats);
    }

    /// <summary>
    /// Что анализ звука помнит между тиками одного захвата: пики автоусиления, среднее басов и удары.
    /// </summary>
    private sealed class Analysis
    {
        public Analysis(int sampleRate)
        {
            Array.Fill(BandPeaks, MinimumBandPeak);

            // Границы полос в логарифмической шкале — так басы и высокие частоты получают равное внимание
            var binWidth = (double)sampleRate / FftSize;

            for (var i = 0; i <= AudioLevels.BandCount; i++)
            {
                var frequency = LowestFrequency * Math.Pow(HighestFrequency / LowestFrequency, i / (double)AudioLevels.BandCount);
                BandEdges[i] = Math.Clamp((int)(frequency / binWidth), 1, FftSize / 2 - 1);
            }
        }

        public Complex[] Fft { get; } = new Complex[FftSize];

        public int[] BandEdges { get; } = new int[AudioLevels.BandCount + 1];

        public double[] BandPeaks { get; } = new double[AudioLevels.BandCount];

        public double LevelPeak { get; set; } = MinimumPeak;

        public double BassAverage { get; set; }

        public long Beats { get; set; }

        public Stopwatch SinceBeat { get; } = Stopwatch.StartNew();
    }
}

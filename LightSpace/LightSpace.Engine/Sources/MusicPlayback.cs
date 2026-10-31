using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Неизменяемая копия настроек содержимого-музыки.
/// </summary>
/// <param name="Color">Цвет, когда музыка светится одним цветом.</param>
/// <param name="Sensitivity">Множитель громкости: 1 — как есть, больше — ярче на тихой музыке.</param>
/// <param name="Smoothing">Доля света, которая остаётся за 1/20 секунды после громкого места, от 0 до 0,95.</param>
public sealed record MusicSettings(AudioInput Input, MusicMode Mode, MusicColors Colors, RgbColor Color, double Sensitivity, double Smoothing)
{
    public static MusicSettings From(MusicConfig music)
    {
        var color = RgbColor.TryParse(music.Color, out var parsed) ? parsed : RgbColor.Parse(MusicConfig.DefaultColor);

        return new MusicSettings(new AudioInput(music.Input, music.DeviceId), music.Mode, music.Colors, color,
            music.Sensitivity / 100.0, music.Smoothing / 100.0);
    }
}

/// <summary>
/// Как играет содержимое-музыка: громкость, полосы спектра и удары с быстрой атакой и плавным спадом, и их цвета
/// на устройстве. Музыка играет в пространстве самого устройства: на ленте спектр идёт вдоль неё, на клавиатуре —
/// столбиками по колонкам. Помнит сглаженный звук, поэтому у каждого играющего содержимого свой экземпляр;
/// живёт в одном потоке.
/// </summary>
public sealed class MusicPlayback
{
    // Сглаживание задано на 1/20 секунды: так же, как в старом LedHub при 20 кадрах в секунду
    private const double SmoothingRate = 20;

    // Насколько тон перескакивает на каждом ударе и как медленно плывёт между ударами
    private const double HueStepPerBeat = 47;
    private const double HueDriftPerSecond = 8;

    // Самая тусклая яркость в ритме и пульсе: свет не гаснет совсем между ударами
    private const double MinimumLight = 0.08;

    private readonly double[] _bands = new double[AudioLevels.BandCount];
    private double _level;
    private double _flash;
    private double _hue;

    // Сколько ударов было в прошлом кадре; -1 — кадров ещё не было
    private long _beats = -1;

    /// <summary>
    /// Двигает сглаженный звук на seconds секунд к снимку levels.
    /// </summary>
    public void Advance(MusicSettings settings, AudioLevels levels, double seconds)
    {
        var keep = Math.Pow(settings.Smoothing, seconds * SmoothingRate);

        _level = Follow(_level, Math.Clamp(levels.Level * settings.Sensitivity, 0, 1), keep);

        for (var i = 0; i < _bands.Length && i < levels.Bands.Count; i++)
            _bands[i] = Follow(_bands[i], Math.Clamp(levels.Bands[i] * settings.Sensitivity, 0, 1), keep);

        // Захват начался заново — счётчик ударов сбросился, это не удар
        var isBeat = _beats >= 0 && levels.Beats > _beats;
        _beats = levels.Beats;

        _flash *= keep;

        if (isBeat)
        {
            _flash = 1;
            _hue = (_hue + HueStepPerBeat) % 360;
        }

        // Между ударами цвет тоже медленно плывёт, чтобы не застывать на тихих местах
        _hue = (_hue + HueDriftPerSecond * seconds) % 360;
    }

    /// <summary>
    /// Цвета светодиодов устройства; у устройства одного цвета — один цвет.
    /// </summary>
    public RgbColor[] Render(MusicSettings settings, DeviceGeometry geometry)
    {
        if (!geometry.IsAddressable)
            return new[] { Solid(settings) };

        var colors = new RgbColor[geometry.Points.Count];

        for (var i = 0; i < colors.Length; i++)
        {
            if (geometry.Points[i] is not { } point)
                continue;

            // Место вдоль устройства от 0 до 1 и высота над нижним краем от 0 до 1
            var position = geometry.IsLinear ? point.X : point.X / Math.Max(geometry.Width, 0.01);
            var rise = geometry.IsLinear ? 0 : 1 - point.Y / Math.Max(geometry.Height, 0.01);

            colors[i] = settings.Mode switch
            {
                MusicMode.Rhythm => Tint(settings, _hue + position * 90, MinimumLight + (1 - MinimumLight) * _flash),
                MusicMode.Pulse => Meter(settings, position),
                _ => Spectrum(settings, position, rise, geometry.IsLinear)
            };
        }

        return colors;
    }

    /// <summary>
    /// Цвет устройства без отдельных светодиодов.
    /// </summary>
    private RgbColor Solid(MusicSettings settings)
    {
        switch (settings.Mode)
        {
            case MusicMode.Rhythm:
                return Tint(settings, _hue, MinimumLight + (1 - MinimumLight) * _flash);

            case MusicMode.Pulse:
                return Tint(settings, _hue, MinimumLight + (1 - MinimumLight) * _level);

            default:
                // Басы, средние и высокие частоты — красный, зелёный и синий
                return settings.Colors == MusicColors.Rainbow
                    ? new RgbColor(ToByte(BandAverage(0, 4)), ToByte(BandAverage(5, 10)), ToByte(BandAverage(11, 15)))
                    : EffectMath.Dim(settings.Color, _level);
        }
    }

    /// <summary>
    /// Полосы спектра: басы в начале, высокие частоты в конце. На ленте полоса светится ярче на громкой частоте,
    /// на плоском устройстве — столбик поднимается от нижнего края.
    /// </summary>
    private RgbColor Spectrum(MusicSettings settings, double position, double rise, bool isLinear)
    {
        var band = _bands[Math.Clamp((int)(position * AudioLevels.BandCount), 0, AudioLevels.BandCount - 1)];
        var light = isLinear ? band : rise <= band ? 1 : 0;

        return Tint(settings, position * 270, light);
    }

    /// <summary>
    /// Шкала громкости от середины к краям: в радуге зелёная в середине и красная у краёв.
    /// </summary>
    private RgbColor Meter(MusicSettings settings, double position)
    {
        var distance = Math.Abs(position * 2 - 1);

        return distance <= _level ? Tint(settings, 120 - 120 * distance, 1) : EffectMath.Black;
    }

    /// <summary>
    /// Цвет с яркостью light от 0 до 1: тон hue в радуге или цвет из настроек.
    /// </summary>
    private static RgbColor Tint(MusicSettings settings, double hue, double light)
    {
        return settings.Colors == MusicColors.Rainbow
            ? RgbColor.FromHsv(hue, 1, Math.Clamp(light, 0, 1))
            : EffectMath.Dim(settings.Color, light);
    }

    private double BandAverage(int from, int to)
    {
        var sum = 0.0;

        for (var i = from; i <= to; i++)
            sum += _bands[i];

        return sum / (to - from + 1);
    }

    /// <summary>
    /// Быстрая атака и плавный спад, чтобы свет не дрожал; keep — доля прежнего значения при спаде.
    /// </summary>
    private static double Follow(double current, double target, double keep)
    {
        return target > current ? target : current * keep + target * (1 - keep);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}

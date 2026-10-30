using LedHub.Core;

namespace LedHub.Services.Effects;

public enum EffectCategory
{
    Colors,
    Motion,
    Nature,
    Music,
    Screen,
    Keyboard
}

/// <summary>
/// Точка, которую красит эффект: X и Y — место на ленте или на сцене, Seed — постоянное число светодиода
/// для случайных эффектов, чтобы каждый светодиод мерцал по-своему, но без скачков от кадра к кадру.
/// </summary>
public readonly record struct EffectPixel(double X, double Y, int Seed);

/// <summary>
/// Эффект из библиотеки: название, настройки и правило, по которому считается цвет точки.
/// </summary>
public sealed class EffectDefinition
{
    private readonly Dictionary<string, EffectParameter> _parameters;
    private readonly Func<EffectFrame, EffectPixel, RgbColor> _render;

    public EffectDefinition(string id, string title, EffectCategory category, string description,
        IReadOnlyList<EffectParameter> parameters, Func<EffectFrame, EffectPixel, RgbColor> render)
    {
        Id = id;
        Title = title;
        Category = category;
        Description = description;
        Parameters = parameters;
        _parameters = parameters.ToDictionary(p => p.Key);
        _render = render;
    }

    /// <summary>
    /// Постоянный идентификатор; по нему сохраняются настройки эффекта.
    /// </summary>
    public string Id { get; }

    public string Title { get; }

    public EffectCategory Category { get; }

    public string Description { get; }

    public IReadOnlyList<EffectParameter> Parameters { get; }

    /// <summary>
    /// Эффект слушает звук компьютера.
    /// </summary>
    public bool UsesAudio { get; init; }

    /// <summary>
    /// Эффект берёт цвета с картинки главного монитора.
    /// </summary>
    public bool UsesScreen { get; init; }

    /// <summary>
    /// Эффект отвечает на нажатия клавиш и играет только на клавиатуре.
    /// </summary>
    public bool UsesKeys { get; init; }

    public EffectParameter? Find(string key)
    {
        return _parameters.GetValueOrDefault(key);
    }

    public RgbColor Render(EffectFrame frame, EffectPixel pixel)
    {
        return _render(frame, pixel);
    }
}

/// <summary>
/// Удар в музыке: когда он случился (во времени эффекта) и каким цветом его показать.
/// </summary>
public readonly record struct AudioBeat(double Time, double Hue);

/// <summary>
/// Нажатие клавиши: где она на клавиатуре, когда её нажали (во времени эффекта) и тон для разноцветных вспышек.
/// </summary>
public readonly record struct KeyHit(double X, double Y, double Time, double Hue);

/// <summary>
/// Звук на текущем кадре, уже приведённый к 0–1 с автоусилением.
/// </summary>
public sealed class AudioFrame
{
    public const int BandCount = AudioAnalyzer.BandCount;

    public double Level { get; set; }

    public double Bass { get; set; }

    /// <summary>
    /// Тон, который перескакивает на каждом ударе и медленно плывёт между ними.
    /// </summary>
    public double Hue { get; set; }

    public double[] Bands { get; } = new double[BandCount];

    /// <summary>
    /// Недавние удары, самый свежий последним.
    /// </summary>
    public List<AudioBeat> Beats { get; } = new();

    /// <summary>
    /// Уровень полосы спектра в точке от 0 до 1: басы в начале, высокие частоты в конце.
    /// </summary>
    public double BandAt(double position)
    {
        var band = Math.Clamp((int)(position * BandCount), 0, BandCount - 1);

        return Bands[band];
    }
}

/// <summary>
/// Всё, что эффекту нужно знать о текущем кадре: время, настройки, звук и форму пространства.
/// </summary>
public sealed class EffectFrame
{
    private static readonly ScreenFrame NoScreen = new(1, 1);

    private readonly EffectDefinition _effect;
    private readonly EffectValues _values;

    public EffectFrame(EffectDefinition effect, EffectValues values, bool isLinear, double originX, double originY,
        double width, double height, AudioFrame audio)
    {
        _effect = effect;
        _values = values;
        IsLinear = isLinear;
        OriginX = originX;
        OriginY = originY;

        // Сцена, вытянутая в одну линию, не должна давать деление на ноль по второй стороне
        Width = Math.Max(width, 0.05);
        Height = Math.Max(height, 0.05);
        Audio = audio;

        MaxDistance = isLinear
            ? 1
            : new[] { (0.0, 0.0), (Width, 0.0), (0.0, Height), (Width, Height) }
                .Max(c => Math.Sqrt((c.Item1 - originX) * (c.Item1 - originX) + (c.Item2 - originY) * (c.Item2 - originY)));
    }

    /// <summary>
    /// Время эффекта в секундах, уже умноженное на скорость.
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// Эффект играет вдоль одной ленты: X и Y точки равны её месту на ленте от 0 до 1.
    /// Иначе — на сцене, где X и Y — координаты на стене.
    /// </summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Центр, от которого расходятся круги, — обычно основной монитор на сцене.
    /// </summary>
    public double OriginX { get; }

    public double OriginY { get; }

    /// <summary>
    /// Размер пространства: X идёт от 0 до Width, Y — от 0 до Height; большая сторона равна 1.
    /// </summary>
    public double Width { get; }

    public double Height { get; }

    public AudioFrame Audio { get; }

    /// <summary>
    /// Картинка главного монитора; у эффектов не «по экрану» — пустая и чёрная.
    /// </summary>
    public ScreenFrame Screen { get; init; } = NoScreen;

    /// <summary>
    /// Недавние нажатия клавиш, самое свежее последним; у эффектов не от нажатий — пусто.
    /// </summary>
    public IReadOnlyList<KeyHit> Keys { get; init; } = Array.Empty<KeyHit>();

    /// <summary>
    /// Расстояние от центра до самого дальнего угла пространства.
    /// </summary>
    public double MaxDistance { get; }

    public double Number(string key)
    {
        if (_values.Numbers.TryGetValue(key, out var value))
            return value;

        // Скорость и чувствительность без своей настройки — обычные
        return _effect.Find(key)?.Default ?? (key is "speed" or "gain" ? 1 : 0);
    }

    public RgbColor Color(string key)
    {
        if (_values.Colors.TryGetValue(key, out var color))
            return color;

        return _effect.Find(key)?.DefaultColor ?? RgbColor.White;
    }

    public bool Flag(string key)
    {
        return Number(key) >= 0.5;
    }

    /// <summary>
    /// Расстояние точки от центра; на ленте — от её середины, от 0 до 1.
    /// </summary>
    public double Distance(EffectPixel pixel)
    {
        if (IsLinear)
            return Math.Abs(pixel.X - 0.5) * 2;

        var dx = pixel.X - OriginX;
        var dy = pixel.Y - OriginY;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Расстояние от центра в долях: 0 в центре, 1 в самом дальнем углу.
    /// </summary>
    public double Radial(EffectPixel pixel)
    {
        return Distance(pixel) / MaxDistance;
    }

    /// <summary>
    /// Положение точки вдоль выбранного в настройке «Направление» пути: от 0 в начале до 1 в конце.
    /// </summary>
    public double Along(EffectPixel pixel)
    {
        return (int)Number("direction") switch
        {
            1 => 1 - pixel.X / Width,
            2 => pixel.Y / Height,
            3 => 1 - pixel.Y / Height,
            4 => Radial(pixel),
            5 => 1 - Radial(pixel),
            _ => pixel.X / Width
        };
    }
}

using LightSpace.Core;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Effects;

/// <summary>
/// Раздел галереи эффектов.
/// </summary>
public enum EffectCategory
{
    Colors,
    Motion,
    Nature
}

/// <summary>
/// Точка, которую красит эффект: X и Y — её место в пространстве устройства, Seed — постоянное число светодиода
/// для случайных эффектов, чтобы каждый светодиод мерцал по-своему, но без скачков от кадра к кадру.
/// </summary>
public readonly record struct EffectPixel(double X, double Y, int Seed);

/// <summary>
/// Эффект из галереи: название, настройки и правило, по которому считается цвет точки в момент времени.
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
    /// Постоянный идентификатор; по нему сохраняются выбор эффекта и его настройки.
    /// </summary>
    public string Id { get; }

    public string Title { get; }

    public EffectCategory Category { get; }

    public string Description { get; }

    public IReadOnlyList<EffectParameter> Parameters { get; }

    public EffectParameter? Find(string key)
    {
        return _parameters.GetValueOrDefault(key);
    }

    /// <summary>
    /// Значение числовой настройки: своё, если его выставили, иначе по умолчанию.
    /// </summary>
    public double Number(EffectValues values, string key)
    {
        if (values.Numbers.TryGetValue(key, out var value))
            return value;

        // Скорость и чувствительность без своей настройки — обычные
        return Find(key)?.Default ?? (key is "speed" or "gain" ? 1 : 0);
    }

    /// <summary>
    /// Значение настройки-цвета: своё, если его выставили, иначе по умолчанию.
    /// </summary>
    public RgbColor Color(EffectValues values, string key)
    {
        if (values.Colors.TryGetValue(key, out var color))
            return color;

        return Find(key)?.DefaultColor ?? RgbColor.White;
    }

    public RgbColor Render(EffectFrame frame, EffectPixel pixel)
    {
        return _render(frame, pixel);
    }
}

/// <summary>
/// Всё, что эффекту нужно знать о текущем кадре: время, настройки и форму пространства устройства.
/// </summary>
public sealed class EffectFrame
{
    private readonly EffectDefinition _effect;
    private readonly EffectValues _values;

    public EffectFrame(EffectDefinition effect, EffectValues values, DeviceGeometry space, double time)
    {
        _effect = effect;
        _values = values;
        Time = time;
        IsLinear = space.IsLinear;
        OriginX = space.OriginX;
        OriginY = space.OriginY;

        // Пространство, вытянутое в одну линию, не должно давать деление на ноль по второй стороне
        Width = Math.Max(space.Width, 0.05);
        Height = Math.Max(space.Height, 0.05);

        MaxDistance = IsLinear
            ? 1
            : new[] { (0.0, 0.0), (Width, 0.0), (0.0, Height), (Width, Height) }
                .Max(c => Math.Sqrt((c.Item1 - OriginX) * (c.Item1 - OriginX) + (c.Item2 - OriginY) * (c.Item2 - OriginY)));
    }

    /// <summary>
    /// Время эффекта в секундах, уже умноженное на скорость.
    /// </summary>
    public double Time { get; }

    /// <summary>
    /// Эффект играет вдоль одной линии: X и Y точки равны её месту на линии от 0 до 1.
    /// Иначе — на плоскости, где X и Y — координаты точки.
    /// </summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Центр, от которого расходятся круги.
    /// </summary>
    public double OriginX { get; }

    public double OriginY { get; }

    /// <summary>
    /// Размер пространства: X идёт от 0 до Width, Y — от 0 до Height; большая сторона равна 1.
    /// </summary>
    public double Width { get; }

    public double Height { get; }

    /// <summary>
    /// Расстояние от центра до самого дальнего угла пространства.
    /// </summary>
    public double MaxDistance { get; }

    public double Number(string key)
    {
        return _effect.Number(_values, key);
    }

    public RgbColor Color(string key)
    {
        return _effect.Color(_values, key);
    }

    public bool Flag(string key)
    {
        return Number(key) >= 0.5;
    }

    /// <summary>
    /// Расстояние точки от центра; на линии — от её середины, от 0 до 1.
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

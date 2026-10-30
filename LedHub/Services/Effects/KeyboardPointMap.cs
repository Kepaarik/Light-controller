using LedHub.Core;

namespace LedHub.Services.Effects;

/// <summary>
/// Эффект по клавиатуре: каждая клавиша — точка в середине своего места, поэтому волны и круги идут
/// по рядам и столбцам клавиш, а не по номерам светодиодов. Большая сторона клавиатуры равна 1.
/// </summary>
public sealed class KeyboardPointMap : IEffectPointMap
{
    private readonly IKeyboardDevice _keyboard;
    private readonly double _scale;

    public KeyboardPointMap(IKeyboardDevice keyboard)
    {
        _keyboard = keyboard;

        var width = keyboard.Keys.Count > 0 ? keyboard.Keys.Max(k => k.X + k.Width) : 1;
        var height = keyboard.Keys.Count > 0 ? keyboard.Keys.Max(k => k.Y + k.Height) : 1;

        _scale = 1 / Math.Max(width, height);
        Width = width * _scale;
        Height = height * _scale;
    }

    public bool IsLinear => false;

    public double OriginX => Width / 2;

    public double OriginY => Height / 2;

    public double Width { get; }

    public double Height { get; }

    public ScreenRect? Screen => null;

    public IReadOnlyList<EffectPixel?> GetPoints(LedDevice device, int ledCount)
    {
        var points = new EffectPixel?[ledCount];

        foreach (var key in _keyboard.Keys.Where(k => k.Led < ledCount))
            points[key.Led] = PointOf(key);

        return points;
    }

    public EffectPixel? GetSolidPoint(LedDevice device)
    {
        return new EffectPixel(OriginX, OriginY, device.Id.GetHashCode());
    }

    /// <summary>
    /// Середина клавиши с этим кодом; null — на клавиатуре такой клавиши нет.
    /// </summary>
    public EffectPixel? FindKey(int code)
    {
        var key = _keyboard.Keys.FirstOrDefault(k => k.Code == code);

        return key != null ? PointOf(key) : null;
    }

    private EffectPixel PointOf(KeyboardKey key)
    {
        return new EffectPixel((key.X + key.Width / 2) * _scale, (key.Y + key.Height / 2) * _scale, key.Led);
    }
}

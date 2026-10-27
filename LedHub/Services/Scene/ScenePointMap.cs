using LedHub.Core;
using LedHub.Services.Effects;

namespace LedHub.Services.Scene;

/// <summary>
/// Места светодиодов на сцене для эффектов: координаты сцены приводятся к долям, где большая сторона сцены — 1.
/// Обновляется при каждой правке сцены, поэтому запущенный эффект сразу видит новую расстановку.
/// </summary>
public sealed class ScenePointMap : IEffectPointMap
{
    private Dictionary<string, EffectPixel?[]> _leds = new();
    private Dictionary<string, EffectPixel> _centers = new();

    public bool IsLinear => false;

    public double OriginX { get; private set; } = 0.5;

    public double OriginY { get; private set; } = 0.5;

    public double Width { get; private set; } = 1;

    public double Height { get; private set; } = 1;

    /// <summary>
    /// Пересчитывает места по формам элементов; origin — точка, от которой расходятся круги (обычно основной монитор).
    /// </summary>
    public void Update(IReadOnlyList<SceneShape> shapes, ScenePoint? origin)
    {
        var leds = new Dictionary<string, EffectPixel?[]>();
        var centers = new Dictionary<string, EffectPixel>();

        if (shapes.Count == 0)
        {
            _leds = leds;
            _centers = centers;
            return;
        }

        var minX = shapes.Min(s => s.Bounds.Min.X);
        var minY = shapes.Min(s => s.Bounds.Min.Y);
        var maxX = shapes.Max(s => s.Bounds.Max.X);
        var maxY = shapes.Max(s => s.Bounds.Max.Y);
        var scale = Math.Max(1, Math.Max(maxX - minX, maxY - minY));

        EffectPixel ToPixel(ScenePoint point, int seed) => new((point.X - minX) / scale, (point.Y - minY) / scale, seed);

        foreach (var shape in shapes.Where(s => s.DeviceId != null))
        {
            var id = shape.DeviceId!;
            var points = new EffectPixel?[shape.Leds.Count];

            for (var i = 0; i < points.Length; i++)
            {
                if (shape.Leds[i] is { } point)
                    points[i] = ToPixel(point, HashCode.Combine(id, i));
            }

            leds[id] = points;
            centers[id] = ToPixel(shape.Center, id.GetHashCode());
        }

        var center = origin ?? new ScenePoint((minX + maxX) / 2, (minY + maxY) / 2);

        Width = (maxX - minX) / scale;
        Height = (maxY - minY) / scale;
        OriginX = (center.X - minX) / scale;
        OriginY = (center.Y - minY) / scale;

        _leds = leds;
        _centers = centers;
    }

    public IReadOnlyList<EffectPixel?> GetPoints(LedDevice device, int ledCount)
    {
        if (!_leds.TryGetValue(device.Id, out var points) || points.Length == 0)
        {
            // Адресная лента, у которой на сцене нет мест для светодиодов, светится целиком цветом своей середины
            var center = GetSolidPoint(device);
            return Enumerable.Repeat(center, ledCount).ToArray();
        }

        return points;
    }

    public EffectPixel? GetSolidPoint(LedDevice device)
    {
        return CenterOf(device.Id);
    }

    /// <summary>
    /// Место светодиода устройства для предпросмотра на схеме — даже если самого устройства сейчас нет в сети.
    /// </summary>
    public EffectPixel? LedOf(string deviceId, int index)
    {
        return _leds.TryGetValue(deviceId, out var points) && index < points.Length ? points[index] : null;
    }

    public EffectPixel? CenterOf(string deviceId)
    {
        return _centers.TryGetValue(deviceId, out var center) ? center : null;
    }
}

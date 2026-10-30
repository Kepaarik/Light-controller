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

    // Те же места в координатах самой сцены — по ним слои эффектов узнают, что попало в их границы
    private Dictionary<string, ScenePoint?[]> _sceneLeds = new();
    private Dictionary<string, ScenePoint> _sceneCenters = new();

    public bool IsLinear => false;

    public double OriginX { get; private set; } = 0.5;

    public double OriginY { get; private set; } = 0.5;

    public double Width { get; private set; } = 1;

    public double Height { get; private set; } = 1;

    public ScreenRect? Screen { get; private set; }

    /// <summary>
    /// Экран главного монитора в координатах сцены; null — монитора на сцене нет.
    /// </summary>
    public ScreenRect? SceneScreen { get; private set; }

    /// <summary>
    /// Пересчитывает места по формам элементов; origin — точка, от которой расходятся круги (обычно основной монитор),
    /// screen — экран главного монитора, с которого берут цвета эффекты «по экрану».
    /// </summary>
    public void Update(IReadOnlyList<SceneShape> shapes, ScenePoint? origin, SceneShape? screen)
    {
        var leds = new Dictionary<string, EffectPixel?[]>();
        var centers = new Dictionary<string, EffectPixel>();
        var sceneLeds = new Dictionary<string, ScenePoint?[]>();
        var sceneCenters = new Dictionary<string, ScenePoint>();

        if (shapes.Count == 0)
        {
            _leds = leds;
            _centers = centers;
            _sceneLeds = sceneLeds;
            _sceneCenters = sceneCenters;
            Screen = null;
            SceneScreen = null;
            return;
        }

        // Поле эффекта задают элементы «в связке»; если таких нет, то все
        var together = shapes.Where(s => s.Config.Projection == SceneProjection.Together).ToList();
        var field = together.Count > 0 ? together : shapes;

        var minX = field.Min(s => s.Bounds.Min.X);
        var minY = field.Min(s => s.Bounds.Min.Y);
        var maxX = field.Max(s => s.Bounds.Max.X);
        var maxY = field.Max(s => s.Bounds.Max.Y);
        var scale = Math.Max(1, Math.Max(maxX - minX, maxY - minY));

        EffectPixel ToPixel(ScenePoint point, int seed) => new((point.X - minX) / scale, (point.Y - minY) / scale, seed);

        foreach (var shape in shapes.Where(s => s.DeviceId != null && s.Config.Projection != SceneProjection.Off))
        {
            var id = shape.DeviceId!;
            var points = new EffectPixel?[shape.Leds.Count];
            Func<ScenePoint, ScenePoint> place = shape.Config.Projection == SceneProjection.Separate
                ? FitInto(shape, minX, minY, maxX, maxY)
                : point => point;

            for (var i = 0; i < points.Length; i++)
            {
                if (shape.Leds[i] is { } point)
                    points[i] = ToPixel(place(point), HashCode.Combine(id, i));
            }

            leds[id] = points;
            centers[id] = ToPixel(place(shape.Center), id.GetHashCode());

            sceneLeds[id] = shape.Leds.ToArray();
            sceneCenters[id] = shape.Center;
        }

        var center = origin ?? new ScenePoint((minX + maxX) / 2, (minY + maxY) / 2);

        Width = (maxX - minX) / scale;
        Height = (maxY - minY) / scale;
        OriginX = (center.X - minX) / scale;
        OriginY = (center.Y - minY) / scale;

        Screen = screen?.Screen is { } rect
            ? new ScreenRect((rect.TopLeft.X - minX) / scale, (rect.TopLeft.Y - minY) / scale, rect.Width / scale, rect.Height / scale)
            : null;

        SceneScreen = screen?.Screen is { } sceneRect
            ? new ScreenRect(sceneRect.TopLeft.X, sceneRect.TopLeft.Y, sceneRect.Width, sceneRect.Height)
            : null;

        _leds = leds;
        _centers = centers;
        _sceneLeds = sceneLeds;
        _sceneCenters = sceneCenters;
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

    /// <summary>
    /// Места светодиодов устройства в координатах сцены; null — устройство не играет эффекты сцены.
    /// </summary>
    public IReadOnlyList<ScenePoint?>? SceneLedsOf(string deviceId)
    {
        return _sceneLeds.TryGetValue(deviceId, out var points) ? points : null;
    }

    public ScenePoint? SceneCenterOf(string deviceId)
    {
        return _sceneCenters.TryGetValue(deviceId, out var center) ? center : null;
    }

    /// <summary>
    /// Растягивает элемент с сохранением пропорций на всё поле эффекта, чтобы на нём шёл весь эффект целиком.
    /// </summary>
    private static Func<ScenePoint, ScenePoint> FitInto(SceneShape shape, double minX, double minY, double maxX, double maxY)
    {
        var (min, max) = shape.Bounds;
        var width = max.X - min.X;
        var height = max.Y - min.Y;

        // Прямая лента не имеет толщины — её вписываем только по длине
        var scaleX = width > 0 ? (maxX - minX) / width : double.PositiveInfinity;
        var scaleY = height > 0 ? (maxY - minY) / height : double.PositiveInfinity;
        var scale = Math.Min(scaleX, scaleY);

        if (double.IsInfinity(scale))
            scale = 1;

        var fromX = (min.X + max.X) / 2;
        var fromY = (min.Y + max.Y) / 2;
        var toX = (minX + maxX) / 2;
        var toY = (minY + maxY) / 2;

        return point => new ScenePoint(toX + (point.X - fromX) * scale, toY + (point.Y - fromY) * scale);
    }
}

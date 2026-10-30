using LedHub.Core;
using LedHub.Services.Effects;

namespace LedHub.Services.Scene;

/// <summary>
/// Слой эффектов сцены: свой эффект в своей прямоугольной области поверх эффекта всей сцены.
/// </summary>
public sealed class SceneLayerConfig
{
    /// <summary>
    /// Эффект из библиотеки; его настройки общие с галереей эффектов.
    /// </summary>
    public string EffectId { get; set; } = "";

    /// <summary>
    /// Левый верхний угол области в единицах сцены.
    /// </summary>
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 120;

    public double Height { get; set; } = 80;

    public bool Contains(ScenePoint point)
    {
        return point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
    }
}

/// <summary>
/// Места светодиодов внутри области слоя: эффект слоя целиком умещается в его границы,
/// а светодиоды за границами в слой не попадают.
/// </summary>
public sealed class SceneLayerMap : IEffectPointMap
{
    private readonly ScenePointMap _scene;
    private readonly SceneLayerConfig _layer;

    /// <summary>
    /// Границы слоя читаются на каждом кадре, поэтому их можно менять на ходу.
    /// </summary>
    public SceneLayerMap(ScenePointMap scene, SceneLayerConfig layer)
    {
        _scene = scene;
        _layer = layer;
    }

    public bool IsLinear => false;

    public double OriginX => Width / 2;

    public double OriginY => Height / 2;

    public double Width => _layer.Width / Scale;

    public double Height => _layer.Height / Scale;

    public ScreenRect? Screen => _scene.SceneScreen is { } screen
        ? new ScreenRect((screen.X - _layer.X) / Scale, (screen.Y - _layer.Y) / Scale, screen.Width / Scale, screen.Height / Scale)
        : null;

    private double Scale => Math.Max(1, Math.Max(_layer.Width, _layer.Height));

    public IReadOnlyList<EffectPixel?> GetPoints(LedDevice device, int ledCount)
    {
        var points = new EffectPixel?[ledCount];
        var leds = _scene.SceneLedsOf(device.Id);

        if (leds == null || leds.Count == 0)
        {
            // Как и на всей сцене: лента без мест для светодиодов светится целиком цветом своей середины
            Array.Fill(points, GetSolidPoint(device));
            return points;
        }

        for (var i = 0; i < ledCount && i < leds.Count; i++)
        {
            if (leds[i] is { } led)
                points[i] = ToPixel(led, HashCode.Combine(device.Id, i));
        }

        return points;
    }

    public EffectPixel? GetSolidPoint(LedDevice device)
    {
        return CenterOf(device.Id);
    }

    /// <summary>
    /// Место светодиода в слое для предпросмотра на схеме; null — светодиод за границами слоя.
    /// </summary>
    public EffectPixel? LedOf(string deviceId, int index)
    {
        var leds = _scene.SceneLedsOf(deviceId);
        var point = leds != null && index < leds.Count ? leds[index] : null;

        return point is { } led ? ToPixel(led, HashCode.Combine(deviceId, index)) : CenterOf(deviceId);
    }

    public EffectPixel? CenterOf(string deviceId)
    {
        return _scene.SceneCenterOf(deviceId) is { } center ? ToPixel(center, deviceId.GetHashCode()) : null;
    }

    private EffectPixel? ToPixel(ScenePoint point, int seed)
    {
        if (!_layer.Contains(point))
            return null;

        return new EffectPixel((point.X - _layer.X) / Scale, (point.Y - _layer.Y) / Scale, seed);
    }
}

/// <summary>
/// Эффект всей сцены вместе со слоями поверх него: светодиод берёт цвет у верхнего слоя, в границы которого попал,
/// а если ни в один не попал — у эффекта сцены. Каждый эффект идёт со своей скоростью.
/// </summary>
public sealed class SceneLayerSource : ILightSource, IDisposable
{
    private readonly ScenePointMap _scene;
    private readonly Func<EffectDefinition, double> _speedOf;
    private readonly Part _background;
    private readonly List<(Part Part, SceneLayerMap Map)> _layers = new();
    private double _lastTime;

    /// <summary>
    /// layers идут снизу вверх. Для музыкальных эффектов может выбросить исключение, если звука в системе нет.
    /// </summary>
    public SceneLayerSource(EffectDefinition background, ScenePointMap scene,
        IReadOnlyList<(EffectDefinition Effect, SceneLayerConfig Bounds)> layers,
        Func<EffectDefinition, EffectValues> valuesOf, Func<EffectDefinition, double> speedOf)
    {
        _scene = scene;
        _speedOf = speedOf;
        _background = new Part(background, new PatternSource(background, valuesOf(background), scene));

        try
        {
            foreach (var (effect, bounds) in layers)
            {
                var map = new SceneLayerMap(scene, bounds);
                _layers.Add((new Part(effect, new PatternSource(effect, valuesOf(effect), map)), map));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// phase — время в секундах; скорость каждого эффекта берётся из его настроек.
    /// </summary>
    public void BeginFrame(double phase, EffectSettings settings)
    {
        var elapsed = Math.Max(0, phase - _lastTime);
        _lastTime = phase;

        Advance(_background, elapsed, settings);

        foreach (var (part, _) in _layers)
            Advance(part, elapsed, settings);
    }

    public RgbColor RenderSolid(LedDevice device)
    {
        var color = _background.Source.RenderSolid(device);

        foreach (var (part, map) in _layers)
        {
            if (map.GetSolidPoint(device) is { } point)
                color = part.Source.Render(point);
        }

        return color;
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        var colors = _background.Source.RenderStrip(device, ledCount).ToArray();

        // Слои идут снизу вверх, поэтому верхний перекрашивает нижние
        foreach (var (part, map) in _layers)
        {
            var points = map.GetPoints(device, ledCount);

            for (var i = 0; i < colors.Length && i < points.Count; i++)
            {
                if (points[i] is { } point)
                    colors[i] = part.Source.Render(point);
            }
        }

        return colors;
    }

    /// <summary>
    /// Цвет светодиода для предпросмотра на схеме — даже если самого устройства сейчас нет в сети.
    /// </summary>
    public RgbColor RenderLed(string deviceId, int index)
    {
        var pixel = _scene.LedOf(deviceId, index) ?? _scene.CenterOf(deviceId);
        var color = pixel is { } point ? _background.Source.Render(point) : EffectMath.Black;

        foreach (var (part, map) in _layers)
        {
            if (map.LedOf(deviceId, index) is { } layerPoint)
                color = part.Source.Render(layerPoint);
        }

        return color;
    }

    /// <summary>
    /// Цвет обычной ленты для предпросмотра на схеме.
    /// </summary>
    public RgbColor RenderCenter(string deviceId)
    {
        var color = _scene.CenterOf(deviceId) is { } point ? _background.Source.Render(point) : EffectMath.Black;

        foreach (var (part, map) in _layers)
        {
            if (map.CenterOf(deviceId) is { } layerPoint)
                color = part.Source.Render(layerPoint);
        }

        return color;
    }

    public void Dispose()
    {
        _background.Source.Dispose();

        foreach (var (part, _) in _layers)
            part.Source.Dispose();
    }

    private void Advance(Part part, double elapsed, EffectSettings settings)
    {
        part.Phase += elapsed * _speedOf(part.Effect);
        part.Source.BeginFrame(part.Phase, settings);
    }

    private sealed class Part
    {
        public Part(EffectDefinition effect, PatternSource source)
        {
            Effect = effect;
            Source = source;
        }

        public EffectDefinition Effect { get; }

        public PatternSource Source { get; }

        public double Phase { get; set; }
    }
}

using LedHub.Core;

namespace LedHub.Services.Effects;

/// <summary>
/// Где находятся светодиоды для эффекта: вдоль одной ленты или на сцене.
/// </summary>
public interface IEffectPointMap
{
    /// <summary>
    /// Точки идут вдоль ленты, а не по сцене.
    /// </summary>
    bool IsLinear { get; }

    double OriginX { get; }

    double OriginY { get; }

    double Width { get; }

    double Height { get; }

    /// <summary>
    /// Места светодиодов адресной ленты по порядку; null — светодиод никуда не попал и гаснет.
    /// </summary>
    IReadOnlyList<EffectPixel?> GetPoints(LedDevice device, int ledCount);

    /// <summary>
    /// Одна точка для обычной ленты, которой можно задать только один цвет.
    /// </summary>
    EffectPixel? GetSolidPoint(LedDevice device);
}

/// <summary>
/// Эффект вдоль каждой ленты отдельно: начало ленты — 0, конец — 1.
/// </summary>
public sealed class StripPointMap : IEffectPointMap
{
    private readonly Dictionary<(string Id, int Count), EffectPixel?[]> _cache = new();

    public bool IsLinear => true;

    public double OriginX => 0.5;

    public double OriginY => 0.5;

    public double Width => 1;

    public double Height => 1;

    public IReadOnlyList<EffectPixel?> GetPoints(LedDevice device, int ledCount)
    {
        if (_cache.TryGetValue((device.Id, ledCount), out var points))
            return points;

        points = new EffectPixel?[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            var position = (i + 0.5) / ledCount;
            points[i] = new EffectPixel(position, position, HashCode.Combine(device.Id, i));
        }

        _cache[(device.Id, ledCount)] = points;
        return points;
    }

    public EffectPixel? GetSolidPoint(LedDevice device)
    {
        return new EffectPixel(0.5, 0.5, device.Id.GetHashCode());
    }
}

/// <summary>
/// Источник кадров для эффекта из библиотеки: на каждом кадре красит все точки лент по правилу эффекта.
/// </summary>
public sealed class PatternSource : ILightSource, IDisposable
{
    // Немузыкальным эффектам звук не нужен — им достаётся вечная тишина
    private static readonly AudioFrame Silence = new();

    private readonly EffectDefinition _effect;
    private readonly EffectValues _values;
    private readonly IEffectPointMap _map;
    private readonly AudioTracker? _audio;
    private EffectFrame _frame;

    /// <summary>
    /// values читаются на каждом кадре, поэтому настройки эффекта можно менять на ходу.
    /// Для музыкальных эффектов сразу начинает слушать звук и может выбросить исключение, если звука в системе нет.
    /// </summary>
    public PatternSource(EffectDefinition effect, EffectValues values, IEffectPointMap map)
    {
        _effect = effect;
        _values = values;
        _map = map;
        _audio = effect.UsesAudio ? new AudioTracker() : null;
        _frame = CreateFrame();
    }

    public void BeginFrame(double phase, EffectSettings settings)
    {
        // Раскладку сцены можно менять на ходу, поэтому пространство берётся заново на каждом кадре
        _frame = CreateFrame();
        _frame.Time = phase;

        _audio?.Update(phase, _frame.Number("gain"));
    }

    public RgbColor RenderSolid(LedDevice device)
    {
        return _map.GetSolidPoint(device) is { } point ? _effect.Render(_frame, point) : EffectMath.Black;
    }

    public IReadOnlyList<RgbColor> RenderStrip(LedDevice device, int ledCount)
    {
        var points = _map.GetPoints(device, ledCount);
        var colors = new RgbColor[ledCount];

        for (var i = 0; i < ledCount && i < points.Count; i++)
        {
            if (points[i] is { } point)
                colors[i] = _effect.Render(_frame, point);
        }

        return colors;
    }

    /// <summary>
    /// Цвет одной точки на текущем кадре — для предпросмотра на схеме.
    /// </summary>
    public RgbColor Render(EffectPixel pixel)
    {
        return _effect.Render(_frame, pixel);
    }

    public void Dispose()
    {
        _audio?.Dispose();
    }

    private EffectFrame CreateFrame()
    {
        return new EffectFrame(_effect, _values, _map.IsLinear, _map.OriginX, _map.OriginY, _map.Width, _map.Height,
            _audio?.Frame ?? Silence);
    }
}

namespace LightSpace.Engine.Space;

/// <summary>
/// Время эффектов слоёв одной сцены: у каждого слоя своё, со своей скоростью. Новый слой начинается с нуля.
/// Живёт в одном потоке: в потоке кадров или в потоке окна для превью.
/// </summary>
public sealed class SceneClock
{
    private readonly Dictionary<string, double> _times = new();

    /// <summary>
    /// Двигает время каждого слоя на seconds секунд с учётом его скорости; время слоёв, которых больше нет, забывается.
    /// </summary>
    public void Advance(IReadOnlyList<SceneLayer> layers, double seconds)
    {
        foreach (var layer in layers)
            _times[layer.ClockKey] = TimeOf(layer) + seconds * layer.Speed;

        if (_times.Count > layers.Count)
        {
            foreach (var key in _times.Keys.Except(layers.Select(l => l.ClockKey)).ToList())
                _times.Remove(key);
        }
    }

    /// <summary>
    /// Сколько секунд эффекта слоя прошло с его начала с учётом скорости.
    /// </summary>
    public double TimeOf(SceneLayer layer)
    {
        return _times.GetValueOrDefault(layer.ClockKey);
    }
}

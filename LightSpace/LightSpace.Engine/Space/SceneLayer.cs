using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Space;

/// <summary>
/// Неизменяемая копия слоя сцены: эффект с настройками в своих границах на сцене, непрозрачность и смешивание.
/// </summary>
/// <param name="Id">Постоянный идентификатор слоя.</param>
/// <param name="Bounds">Границы слоя на сцене; светодиоды за ними слой не меняет.</param>
/// <param name="Opacity">Непрозрачность слоя от 0 до 1.</param>
public sealed record SceneLayer(string Id, EffectDefinition Effect, EffectValues Values, SceneRect Bounds, double Opacity, LayerBlend Blend)
{
    /// <summary>
    /// Скорость эффекта слоя: во сколько раз быстрее обычного идёт его время.
    /// </summary>
    public double Speed => Effect.Number(Values, "speed");

    /// <summary>
    /// По чему часы сцены узнают время слоя: когда у слоя меняют эффект, его время начинается заново.
    /// </summary>
    public string ClockKey => $"{Id}/{Effect.Id}";

    /// <summary>
    /// Видимые слои содержимого-сцены снизу вверх: в таком порядке их и накладывают.
    /// </summary>
    public static IReadOnlyList<SceneLayer> ListOf(ContentConfig content)
    {
        return content.Layers
            .Where(l => l.IsVisible)
            .Reverse()
            .Select(From)
            .ToList();
    }

    public static SceneLayer From(SceneLayerConfig layer)
    {
        var effect = EffectLibrary.FindOrSolid(layer.Effect.EffectId);
        var values = EffectValues.From(layer.Effect.Effects.GetValueOrDefault(effect.Id));

        return new SceneLayer(layer.Id, effect, values, layer.Bounds, layer.Opacity / 100.0, layer.Blend);
    }
}

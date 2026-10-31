using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Space;

/// <summary>
/// Кадр содержимого-сцены: слои накладываются снизу вверх, каждый красит только точки внутри своих границ.
/// Эффект слоя целиком умещается в его границы: для эффекта границы — всё его пространство, большая сторона которого равна 1.
/// Одним и тем же рендером считаются кадры устройств и превью сцены в окне.
/// </summary>
public static class SceneRenderer
{
    /// <summary>
    /// Цвета точек сцены; points — места точек в единицах сцены, null — точка нигде не стоит и остаётся чёрной.
    /// Под всеми слоями — чёрный. Возвращает новый массив, который потом никто не меняет.
    /// </summary>
    public static RgbColor[] Render(IReadOnlyList<SceneLayer> layers, SceneClock clock, IReadOnlyList<EffectPixel?> points)
    {
        var colors = new RgbColor[points.Count];

        foreach (var layer in layers)
        {
            var bounds = layer.Bounds;
            var scale = Math.Max(Math.Max(bounds.Width, bounds.Height), 0.01);
            var space = DeviceGeometry.Plane(bounds.Width / scale, bounds.Height / scale);
            var frame = new EffectFrame(layer.Effect, layer.Values, space, clock.TimeOf(layer));

            for (var i = 0; i < colors.Length; i++)
            {
                if (points[i] is not { } point || !bounds.Contains(point.X, point.Y))
                    continue;

                var pixel = new EffectPixel((point.X - bounds.X) / scale, (point.Y - bounds.Y) / scale, point.Seed);

                colors[i] = Blend(colors[i], layer.Effect.Render(frame, pixel), layer);
            }
        }

        return colors;
    }

    /// <summary>
    /// Цвет слоя поверх цвета под ним с учётом смешивания и непрозрачности слоя.
    /// </summary>
    private static RgbColor Blend(RgbColor below, RgbColor color, SceneLayer layer)
    {
        return layer.Blend switch
        {
            LayerBlend.Add => new RgbColor(
                (byte)Math.Min(255, below.R + Math.Round(color.R * layer.Opacity)),
                (byte)Math.Min(255, below.G + Math.Round(color.G * layer.Opacity)),
                (byte)Math.Min(255, below.B + Math.Round(color.B * layer.Opacity))),
            LayerBlend.Lighten => EffectMath.Mix(below, EffectMath.Max(below, color), layer.Opacity),
            _ => EffectMath.Mix(below, color, layer.Opacity)
        };
    }
}

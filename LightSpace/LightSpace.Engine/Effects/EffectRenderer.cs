using LightSpace.Core;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Effects;

/// <summary>
/// Кадр эффекта для одного пространства: цвета всех его светодиодов в момент времени.
/// Одним и тем же рендером считаются кадры устройств и картинки-превью на плитках галереи.
/// </summary>
public static class EffectRenderer
{
    /// <summary>
    /// Цвета светодиодов по их номерам; у пространства без отдельных светодиодов — один цвет его середины.
    /// Возвращает новый массив, который потом никто не меняет.
    /// </summary>
    public static RgbColor[] Render(EffectDefinition effect, EffectValues values, DeviceGeometry space, double time)
    {
        var frame = new EffectFrame(effect, values, space, time);

        if (!space.IsAddressable)
            return new[] { effect.Render(frame, space.Center) };

        var colors = new RgbColor[space.Points.Count];

        for (var i = 0; i < colors.Length; i++)
        {
            // Светодиод без места остаётся чёрным
            if (space.Points[i] is { } point)
                colors[i] = effect.Render(frame, point);
        }

        return colors;
    }
}

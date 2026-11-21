using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Scene;

/// <summary>
/// Пространство устройства внутри сцены. По пространству — места его светодиодов на общем холсте, поэтому один эффект
/// переходит с устройства на устройство; синхронно — его собственное пространство. В обоих случаях пространство
/// повёрнуто так, чтобы эффект шёл в направлении сцены: эффект всегда идёт слева направо по X.
/// </summary>
public static class SceneSpace
{
    /// <summary>
    /// Пространство устройства own в сцене: светодиоды с теми же номерами, что у own, стоят так, как устройство
    /// выглядит в своём виде. monitor — монитор, за которым висит лента: вид «Монитор» берёт её раскладку.
    /// </summary>
    public static DeviceGeometry Of(DeviceGeometry own, SceneDeviceConfig placement, SceneSpread spread, SceneDirection direction,
        MonitorConfig? monitor = null)
    {
        var local = SceneShapes.Of(own, placement, monitor).Local;

        if (spread == SceneSpread.Synced)
            return Directed(local, direction);

        var (points, center) = Place(local, placement);

        return DirectedPlane(points, center, 1, SceneConfig.CanvasHeight, direction);
    }

    /// <summary>
    /// Места светодиодов пространства local на холсте: X от 0 до 1, Y от 0 до высоты холста. У устройства одного цвета
    /// светодиодов нет — только середина.
    /// </summary>
    public static (IReadOnlyList<EffectPixel?> Points, EffectPixel Center) Place(DeviceGeometry local, SceneDeviceConfig placement)
    {
        var points = local.Points.Select(p => p is { } pixel ? ToCanvas(pixel, local, placement) : (EffectPixel?)null).ToArray();

        return (points, ToCanvas(local.Center, local, placement));
    }

    /// <summary>
    /// Устройство в своём виде на холсте: светодиоды, углы корпуса и шестигранников в точках холста.
    /// </summary>
    public static SceneShape Place(SceneShape shape, SceneDeviceConfig placement)
    {
        var local = shape.Local;
        var (points, center) = Place(local, placement);

        var body = shape.Body.Select(p => ToCanvas(p, local, placement)).ToList();
        var panels = shape.Panels
            .Select(p => p with { Corners = p.Corners.Select(c => ToCanvas(c, local, placement)).ToList() })
            .ToList();

        var placed = DeviceGeometry.FromPoints(false, 1, SceneConfig.CanvasHeight, points, center);

        return shape with { Local = placed, Body = body, Panels = panels };
    }

    /// <summary>
    /// Точка пространства local — в точку холста: лента лежит вдоль линии через свою середину, плоскость — вокруг
    /// своей середины; размер и поворот — как у устройства.
    /// </summary>
    private static EffectPixel ToCanvas(EffectPixel pixel, DeviceGeometry local, SceneDeviceConfig placement)
    {
        var radians = placement.Angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var localX = (local.IsLinear ? pixel.X - 0.5 : pixel.X - local.Width / 2) * placement.Size;
        var localY = local.IsLinear ? 0 : (pixel.Y - local.Height / 2) * placement.Size;

        // Поворот по часовой стрелке: Y на холсте идёт вниз
        return new EffectPixel(
            placement.X + localX * cos - localY * sin,
            placement.Y * SceneConfig.CanvasHeight + localX * sin + localY * cos,
            pixel.Seed);
    }

    /// <summary>
    /// Собственное пространство устройства, повёрнутое по направлению сцены.
    /// </summary>
    private static DeviceGeometry Directed(DeviceGeometry own, SceneDirection direction)
    {
        // Одному цвету направление ни к чему
        if (!own.IsAddressable)
            return own;

        if (!own.IsLinear)
            return DirectedPlane(own.Points, own.Center, own.Width, own.Height, direction);

        // На ленте X и Y точки — её место на линии от 0 до 1
        double Along(double position) => direction switch
        {
            SceneDirection.RightToLeft or SceneDirection.BottomToTop => 1 - position,
            SceneDirection.FromCenter => Math.Abs(position - 0.5) * 2,
            _ => position
        };

        var points = own.Points
            .Select(p => p is { } pixel ? new EffectPixel(Along(pixel.X), Along(pixel.X), pixel.Seed) : (EffectPixel?)null)
            .ToArray();

        return DeviceGeometry.FromPoints(true, 1, 1, points, own.Center);
    }

    /// <summary>
    /// Плоскость шириной width и высотой height, повёрнутая так, чтобы направление сцены шло по X. «Из центра» —
    /// линия от середины плоскости к её углам.
    /// </summary>
    private static DeviceGeometry DirectedPlane(IReadOnlyList<EffectPixel?> points, EffectPixel center, double width, double height,
        SceneDirection direction)
    {
        var maxDistance = Math.Sqrt(width * width + height * height) / 2;

        EffectPixel Turn(EffectPixel p) => direction switch
        {
            SceneDirection.RightToLeft => p with { X = width - p.X },
            SceneDirection.TopToBottom => p with { X = p.Y, Y = p.X },
            SceneDirection.BottomToTop => p with { X = height - p.Y, Y = p.X },
            SceneDirection.FromCenter => FromCenter(p),
            _ => p
        };

        EffectPixel FromCenter(EffectPixel p)
        {
            var dx = p.X - width / 2;
            var dy = p.Y - height / 2;
            var distance = Math.Min(Math.Sqrt(dx * dx + dy * dy) / maxDistance, 1);

            return p with { X = distance, Y = distance };
        }

        var turned = points.Select(p => p is { } pixel ? Turn(pixel) : (EffectPixel?)null).ToArray();

        return direction switch
        {
            SceneDirection.FromCenter => DeviceGeometry.FromPoints(true, 1, 1, turned, Turn(center)),
            SceneDirection.TopToBottom or SceneDirection.BottomToTop => DeviceGeometry.FromPoints(false, height, width, turned, Turn(center)),
            _ => DeviceGeometry.FromPoints(false, width, height, turned, Turn(center))
        };
    }
}

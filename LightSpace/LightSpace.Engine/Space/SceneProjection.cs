using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Space;

/// <summary>
/// Проекция устройства на сцену: какой оно формы и где на сцене стоит каждый его светодиод.
/// Лента — полоса вдоль своей длины, клавиатура — прямоугольник своей раскладки, устройство одного цвета — квадрат.
/// </summary>
public static class SceneProjection
{
    /// <summary>
    /// Толщина ленты на сцене.
    /// </summary>
    public const double StripThickness = 3;

    // Размеры по умолчанию: длина ленты, ширина клавиатуры, поперечник устройства одного цвета
    private const double DefaultStripLength = 60;
    private const double DefaultKeyboardWidth = 44;
    private const double DefaultSingleSize = 10;

    /// <summary>
    /// Размер устройства на сцене: свой, если его задали, иначе по умолчанию для такого устройства.
    /// </summary>
    public static double SizeOf(DeviceGeometry geometry, DevicePlacement placement)
    {
        return placement.Size > 0 ? placement.Size : DefaultSizeOf(geometry);
    }

    /// <summary>
    /// Размер по умолчанию: лента, клавиатура или устройство одного цвета.
    /// </summary>
    public static double DefaultSizeOf(DeviceGeometry geometry)
    {
        if (!geometry.IsAddressable)
            return DefaultSingleSize;

        return geometry.IsLinear ? DefaultStripLength : DefaultKeyboardWidth;
    }

    /// <summary>
    /// Ширина и высота устройства на сцене без поворота.
    /// </summary>
    public static (double Width, double Height) ExtentOf(DeviceGeometry geometry, DevicePlacement placement)
    {
        var size = SizeOf(geometry, placement);

        if (!geometry.IsAddressable)
            return (size, size);

        // Клавиатура сохраняет пропорции раскладки: большая сторона пространства равна 1, ширина на сцене — size
        return geometry.IsLinear
            ? (size, StripThickness)
            : (size, size * geometry.Height / Math.Max(geometry.Width, 0.01));
    }

    /// <summary>
    /// Прямоугольник, в который целиком входит повёрнутое устройство.
    /// </summary>
    public static SceneRect BoundsOf(DeviceGeometry geometry, DevicePlacement placement)
    {
        var (width, height) = ExtentOf(geometry, placement);
        var angle = placement.Rotation * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(angle));
        var sin = Math.Abs(Math.Sin(angle));
        var boundsWidth = width * cos + height * sin;
        var boundsHeight = width * sin + height * cos;

        return new SceneRect(placement.X - boundsWidth / 2, placement.Y - boundsHeight / 2, boundsWidth, boundsHeight);
    }

    /// <summary>
    /// Места точек устройства на сцене в единицах сцены, с теми же номерами и числами Seed, что в его пространстве:
    /// у адресного устройства — по светодиоду, у устройства одного цвета — одна точка в его середине.
    /// </summary>
    public static IReadOnlyList<EffectPixel?> Project(DeviceGeometry geometry, DevicePlacement placement)
    {
        if (!geometry.IsAddressable)
            return new EffectPixel?[] { new EffectPixel(placement.X, placement.Y, geometry.Center.Seed) };

        var (width, _) = ExtentOf(geometry, placement);
        var angle = placement.Rotation * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var points = new EffectPixel?[geometry.Points.Count];

        // Сколько единиц сцены в единице пространства устройства: его ширина на сцене — width
        var scale = width / Math.Max(geometry.Width, 0.01);

        for (var i = 0; i < points.Length; i++)
        {
            if (geometry.Points[i] is not { } point)
                continue;

            // У ленты место светодиода на линии — X от 0 до 1, поперёк ленты все светодиоды на её середине
            var x = (point.X - geometry.Width / 2) * scale;
            var y = geometry.IsLinear ? 0 : (point.Y - geometry.Height / 2) * scale;

            points[i] = new EffectPixel(placement.X + x * cos - y * sin, placement.Y + x * sin + y * cos, point.Seed);
        }

        return points;
    }
}

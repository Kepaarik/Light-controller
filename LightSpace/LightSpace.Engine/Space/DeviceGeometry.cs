using LightSpace.Devices;
using LightSpace.Engine.Effects;

namespace LightSpace.Engine.Space;

/// <summary>
/// Пространство одного устройства: где находится каждый его светодиод. Большая сторона пространства равна 1.
/// Лента — светодиоды вдоль линии по порядку, клавиатура — середины клавиш по раскладке, устройство одного цвета — одна точка.
/// </summary>
public sealed class DeviceGeometry
{
    private DeviceGeometry(bool isLinear, double width, double height, IReadOnlyList<EffectPixel?> points, EffectPixel center)
    {
        IsLinear = isLinear;
        Width = width;
        Height = height;
        Points = points;
        Center = center;
    }

    /// <summary>
    /// Точки идут вдоль одной линии: X и Y точки равны её месту на линии от 0 до 1.
    /// Иначе X и Y — координаты на плоскости.
    /// </summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Ширина пространства: X идёт от 0 до Width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Высота пространства: Y идёт от 0 до Height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Центр пространства по горизонтали: от него расходятся круги.
    /// </summary>
    public double OriginX => Width / 2;

    /// <summary>
    /// Центр пространства по вертикали.
    /// </summary>
    public double OriginY => Height / 2;

    /// <summary>
    /// Места светодиодов по их номерам; null — у светодиода нет места (например, под ним нет клавиши), он не горит.
    /// Пусто — у устройства нет отдельных светодиодов.
    /// </summary>
    public IReadOnlyList<EffectPixel?> Points { get; }

    /// <summary>
    /// Середина устройства: её цвет показывает устройство без отдельных светодиодов.
    /// </summary>
    public EffectPixel Center { get; }

    /// <summary>
    /// Каждому светодиоду устройства достаётся свой цвет.
    /// </summary>
    public bool IsAddressable => Points.Count > 0;

    /// <summary>
    /// Пространство устройства по его возможностям: клавиатура, адресная лента или одна точка.
    /// </summary>
    public static DeviceGeometry For(string deviceId, DeviceCapabilities capabilities)
    {
        if (capabilities.IsKeyboard)
            return Keyboard(capabilities.Keys, capabilities.LedCount);

        if (capabilities.IsAddressable)
            return Strip(deviceId, capabilities.LedCount);

        return Single(deviceId);
    }

    /// <summary>
    /// Устройство, которое светится одним цветом: одна точка в середине линии.
    /// </summary>
    public static DeviceGeometry Single(string deviceId)
    {
        return new DeviceGeometry(true, 1, 1, Array.Empty<EffectPixel?>(), new EffectPixel(0.5, 0.5, deviceId.GetHashCode()));
    }

    /// <summary>
    /// Адресная лента: начало ленты — 0, конец — 1, светодиоды стоят посередине своих отрезков.
    /// </summary>
    public static DeviceGeometry Strip(string deviceId, int ledCount)
    {
        var points = new EffectPixel?[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            var position = (i + 0.5) / ledCount;
            points[i] = new EffectPixel(position, position, HashCode.Combine(deviceId, i));
        }

        return new DeviceGeometry(true, 1, 1, points, new EffectPixel(0.5, 0.5, deviceId.GetHashCode()));
    }

    /// <summary>
    /// Клавиатура: каждая клавиша — точка в середине своего места, поэтому волны и круги идут по рядам и столбцам клавиш,
    /// а не по номерам светодиодов.
    /// </summary>
    public static DeviceGeometry Keyboard(IReadOnlyList<KeyboardKey> keys, int ledCount)
    {
        var width = keys.Count > 0 ? keys.Max(k => k.X + k.Width) : 1;
        var height = keys.Count > 0 ? keys.Max(k => k.Y + k.Height) : 1;
        var scale = 1 / Math.Max(width, height);
        var points = new EffectPixel?[ledCount];

        foreach (var key in keys.Where(k => k.Led >= 0 && k.Led < ledCount))
            points[key.Led] = new EffectPixel((key.X + key.Width / 2) * scale, (key.Y + key.Height / 2) * scale, key.Led);

        var center = new EffectPixel(width * scale / 2, height * scale / 2, 0);

        return new DeviceGeometry(false, width * scale, height * scale, points, center);
    }

    /// <summary>
    /// Прямоугольная сетка точек по строкам слева направо — например, картинка-превью эффекта.
    /// </summary>
    public static DeviceGeometry Grid(int columns, int rows)
    {
        var height = (double)rows / columns;
        var points = new EffectPixel?[columns * rows];

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                points[index] = new EffectPixel((column + 0.5) / columns, (row + 0.5) / rows * height, index);
            }
        }

        return new DeviceGeometry(false, 1, height, points, new EffectPixel(0.5, height / 2, 0));
    }
}

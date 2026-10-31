using LightSpace.Core;
using LightSpace.Core.Config;

namespace LightSpace.Engine.Space;

/// <summary>
/// Автокомпоновка сцены: расставляет устройства так, как они обычно стоят на столе. Клавиатуры — внизу по центру,
/// первая лента — горизонтально вдоль верхнего края, как за монитором, следующие две — вертикально слева и справа,
/// остальные — горизонтальными рядами под первой; устройства одного цвета — в ряд посередине сцены.
/// </summary>
public static class SceneLayout
{
    // Поля от краёв сцены и промежуток между устройствами
    private const double Margin = 8;
    private const double Gap = 6;

    // Шаг, с которым ищется свободное место для нового устройства
    private const double SearchStep = 4;

    /// <summary>
    /// Места всех устройств по их идентификаторам. Размер устройства, который задали раньше (size больше 0), сохраняется.
    /// </summary>
    public static IReadOnlyDictionary<string, DevicePlacement> Arrange(IReadOnlyList<(string Id, DeviceGeometry Geometry, double Size)> devices)
    {
        var placements = new Dictionary<string, DevicePlacement>();
        var keyboards = devices.Where(d => d.Geometry.IsAddressable && !d.Geometry.IsLinear).ToList();
        var strips = devices.Where(d => d.Geometry.IsAddressable && d.Geometry.IsLinear).ToList();
        var singles = devices.Where(d => !d.Geometry.IsAddressable).ToList();

        // Клавиатуры — внизу по центру, следующая выше предыдущей
        var bottom = SceneArea.Height - Margin;

        foreach (var (id, geometry, size) in keyboards)
        {
            var placement = new DevicePlacement { X = SceneArea.Width / 2, Size = size };
            var (_, height) = SceneProjection.ExtentOf(geometry, placement);

            placement.Y = bottom - height / 2;
            bottom -= height + Gap;
            placements[id] = placement;
        }

        // Ленты: вверху, слева, справа, затем ряды под верхней
        var top = Margin + SceneProjection.StripThickness;

        for (var i = 0; i < strips.Count; i++)
        {
            var (id, _, size) = strips[i];

            placements[id] = i switch
            {
                0 => new DevicePlacement { X = SceneArea.Width / 2, Y = top, Size = size },
                1 => new DevicePlacement { X = Margin + SceneProjection.StripThickness, Size = size, Rotation = 90 },
                2 => new DevicePlacement { X = SceneArea.Width - Margin - SceneProjection.StripThickness, Size = size, Rotation = 90 },
                _ => new DevicePlacement { X = SceneArea.Width / 2, Y = Math.Min(top + (i - 2) * Gap * 2, SceneArea.Height - Margin), Size = size }
            };
        }

        // Устройства одного цвета — в ряд посередине, от центра попеременно вправо и влево
        for (var i = 0; i < singles.Count; i++)
        {
            var (id, geometry, size) = singles[i];
            var placement = new DevicePlacement { Size = size };
            var step = (SceneProjection.SizeOf(geometry, placement) + Gap) * ((i + 1) / 2);

            placement.X = Math.Clamp(SceneArea.Width / 2 + (i % 2 == 0 ? step : -step), Margin, SceneArea.Width - Margin);
            placements[id] = placement;
        }

        foreach (var placement in placements.Values)
            placement.Normalize();

        return placements;
    }

    /// <summary>
    /// Место для нового устройства: первое свободное сверху вниз и слева направо, где оно целиком на сцене и не задевает
    /// уже стоящие устройства taken. Если свободного места нет — середина сцены.
    /// </summary>
    public static DevicePlacement FindFreeSpot(DeviceGeometry geometry, IReadOnlyList<SceneRect> taken)
    {
        for (var y = Margin; y <= SceneArea.Height - Margin; y += SearchStep)
        {
            for (var x = Margin; x <= SceneArea.Width - Margin; x += SearchStep)
            {
                var placement = new DevicePlacement { X = x, Y = y };
                var bounds = SceneProjection.BoundsOf(geometry, placement);

                if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > SceneArea.Width || bounds.Bottom > SceneArea.Height)
                    continue;

                if (taken.All(t => !t.Inflate(Gap / 2).IntersectsWith(bounds)))
                    return placement;
            }
        }

        return new DevicePlacement();
    }
}

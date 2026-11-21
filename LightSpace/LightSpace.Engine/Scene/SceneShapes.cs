using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Sources;
using LightSpace.Engine.Space;

namespace LightSpace.Engine.Scene;

/// <summary>
/// Шестигранник устройства: его углы и светодиод, чей цвет он показывает; -1 — светодиода у него нет.
/// </summary>
public sealed record ScenePanel(IReadOnlyList<EffectPixel> Corners, int Led);

/// <summary>
/// Устройство в своём виде: где стоят его светодиоды, контур корпуса (клавиатуры, экрана) и шестигранники.
/// Светодиодов столько же, сколько у самого устройства; у устройства одного цвета их нет — только середина.
/// </summary>
/// <param name="Local">Пространство устройства в этом виде: по нему эффект ложится на светодиоды.</param>
/// <param name="Body">Углы корпуса по порядку; пусто — корпуса нет (лента, шестигранники).</param>
/// <param name="Panels">Шестигранники; пусто — у вида их нет.</param>
public sealed record SceneShape(SceneDeviceShape Kind, DeviceGeometry Local, IReadOnlyList<EffectPixel> Body, IReadOnlyList<ScenePanel> Panels);

/// <summary>
/// Виды устройств сцены: лента, клавиатура, монитор и шестигранники.
/// </summary>
public static class SceneShapes
{
    // Монитор, у которого нет своих размеров, — обычный 16:9
    private const double MonitorAspect = 16.0 / 9;

    // Клавиатура без своей раскладки: втрое шире, чем выше, клавиши в шесть рядов
    private const double KeyboardAspect = 3;
    private const int KeyboardRows = 6;

    // Между шестигранниками — небольшой зазор, как между настоящими панелями
    private const double PanelScale = 0.92;

    /// <summary>
    /// Вид устройства: выбранный в сцене или по самому устройству — клавиатура клавиатурой, остальное лентой.
    /// </summary>
    public static SceneDeviceShape KindOf(DeviceGeometry own, SceneDeviceConfig placement)
    {
        if (placement.Shape != SceneDeviceShape.Auto)
            return placement.Shape;

        return own.IsLinear ? SceneDeviceShape.Strip : SceneDeviceShape.Keyboard;
    }

    /// <summary>
    /// Устройство own в его виде. monitor — монитор, за которым висит лента: монитор берёт её раскладку по краям.
    /// </summary>
    public static SceneShape Of(DeviceGeometry own, SceneDeviceConfig placement, MonitorConfig? monitor = null)
    {
        return KindOf(own, placement) switch
        {
            SceneDeviceShape.Keyboard => Keyboard(own, placement),
            SceneDeviceShape.Monitor => Monitor(own, placement, monitor),
            SceneDeviceShape.Hexagons => Hexagons(own, placement),
            _ => Strip(own, placement)
        };
    }

    private static SceneShape Strip(DeviceGeometry own, SceneDeviceConfig placement)
    {
        var local = own.IsLinear ? own : DeviceGeometry.Strip(placement.DeviceId, own.Points.Count);

        return new SceneShape(SceneDeviceShape.Strip, local, Array.Empty<EffectPixel>(), Array.Empty<ScenePanel>());
    }

    /// <summary>
    /// Настоящая клавиатура — со своей раскладкой; другое устройство — клавишами рядами по порядку светодиодов.
    /// </summary>
    private static SceneShape Keyboard(DeviceGeometry own, SceneDeviceConfig placement)
    {
        if (!own.IsLinear)
        {
            // Корпус чуть больше клавиш: крайние клавиши не лежат на его краю
            var margin = Math.Max(own.Width, own.Height) * 0.03;

            return new SceneShape(SceneDeviceShape.Keyboard, own,
                Rectangle(-margin, -margin, own.Width + margin, own.Height + margin), Array.Empty<ScenePanel>());
        }

        var count = own.Points.Count;
        var height = 1 / KeyboardAspect;
        var rows = Math.Clamp(count, 1, KeyboardRows);
        var columns = Math.Max(1, (int)Math.Ceiling(count / (double)rows));
        var points = new EffectPixel?[count];

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            points[i] = new EffectPixel((column + 0.5) / columns, (row + 0.5) / rows * height, HashCode.Combine(placement.DeviceId, i));
        }

        var local = DeviceGeometry.FromPoints(false, 1, height, points, new EffectPixel(0.5, height / 2, placement.DeviceId.GetHashCode()));

        return new SceneShape(SceneDeviceShape.Keyboard, local, Rectangle(0, 0, 1, height), Array.Empty<ScenePanel>());
    }

    /// <summary>
    /// Экран с лентой по краям. У ленты за монитором — её раскладка и поворот монитора, иначе раскладка по умолчанию на 16:9.
    /// Светодиоды стоят так, как их видно спереди.
    /// </summary>
    private static SceneShape Monitor(DeviceGeometry own, SceneDeviceConfig placement, MonitorConfig? monitor)
    {
        var aspect = monitor != null ? ScreenEdges.PhysicalAspect(16, 9, monitor.Rotation) : MonitorAspect;
        var layout = monitor?.Layout ?? new ScreenLayoutConfig();
        var height = 1 / aspect;
        var count = own.Points.Count;
        var points = new EffectPixel?[count];

        if (count > 0)
        {
            var edges = ScreenEdges.Build(layout, count, aspect);

            for (var i = 0; i < count; i++)
            {
                if (edges[i] is not { } edge)
                    continue;

                // Раскладку, заданную видом сзади, на холсте показываем спереди
                var x = layout.IsViewedFromBack ? 1 - edge.X : edge.X;

                points[i] = new EffectPixel(x, edge.Y * height, HashCode.Combine(placement.DeviceId, i));
            }
        }

        var local = DeviceGeometry.FromPoints(false, 1, height, points, new EffectPixel(0.5, height / 2, placement.DeviceId.GetHashCode()));

        return new SceneShape(SceneDeviceShape.Monitor, local, Rectangle(0, 0, 1, height), Array.Empty<ScenePanel>());
    }

    /// <summary>
    /// Шестигранники сотами: ряды поровну, каждый второй ряд сдвинут на полшага. Светодиоды по порядку поровну делятся
    /// между шестигранниками; если светодиодов меньше, чем шестигранников, у последних светодиода нет.
    /// </summary>
    private static SceneShape Hexagons(DeviceGeometry own, SceneDeviceConfig placement)
    {
        var count = Math.Clamp(placement.Panels, SceneDeviceConfig.MinPanels, SceneDeviceConfig.MaxPanels);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);

        // Шестигранник стоит вершиной вверх: шаг по ряду — √3·r, между рядами — 1,5·r; все соты по ширине — 1
        var step = Math.Sqrt(3);
        var radius = 1 / (step * columns + (rows > 1 ? step / 2 : 0));
        var height = radius * (2 + 1.5 * (rows - 1));
        var centers = new List<EffectPixel>();

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var x = step * radius * (column + 0.5) + (row % 2 == 1 ? step * radius / 2 : 0);
            var y = radius + 1.5 * radius * row;

            centers.Add(new EffectPixel(x, y, HashCode.Combine(placement.DeviceId, i)));
        }

        var ledCount = own.Points.Count;
        var points = new EffectPixel?[ledCount];
        var firstLeds = Enumerable.Repeat(-1, count).ToArray();

        for (var led = 0; led < ledCount; led++)
        {
            // Светодиодов меньше шестигранников — по одному на первые; иначе поровну подряд
            var panel = ledCount >= count ? led * count / ledCount : led;

            points[led] = centers[panel] with { Seed = HashCode.Combine(placement.DeviceId, led) };

            if (firstLeds[panel] < 0)
                firstLeds[panel] = led;
        }

        var panels = centers
            .Select((center, i) => new ScenePanel(Hexagon(center, radius * PanelScale), firstLeds[i]))
            .ToList();

        var local = DeviceGeometry.FromPoints(false, 1, height, points, new EffectPixel(0.5, height / 2, placement.DeviceId.GetHashCode()));

        return new SceneShape(SceneDeviceShape.Hexagons, local, Array.Empty<EffectPixel>(), panels);
    }

    private static IReadOnlyList<EffectPixel> Rectangle(double left, double top, double right, double bottom)
    {
        return new[]
        {
            new EffectPixel(left, top, 0),
            new EffectPixel(right, top, 0),
            new EffectPixel(right, bottom, 0),
            new EffectPixel(left, bottom, 0)
        };
    }

    /// <summary>
    /// Углы шестигранника вершиной вверх с серединой center и радиусом radius.
    /// </summary>
    private static IReadOnlyList<EffectPixel> Hexagon(EffectPixel center, double radius)
    {
        return Enumerable.Range(0, 6)
            .Select(i =>
            {
                var angle = (60 * i - 90) * Math.PI / 180;

                return new EffectPixel(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle), 0);
            })
            .ToList();
    }
}

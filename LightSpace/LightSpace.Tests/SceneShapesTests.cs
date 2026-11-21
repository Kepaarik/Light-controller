using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Space;

namespace LightSpace.Tests;

/// <summary>
/// Виды устройств в сцене: светодиоды стоят так, как устройство выглядит в жизни, и их столько же, сколько у устройства.
/// </summary>
public sealed class SceneShapesTests
{
    private const double Precision = 9;

    [Fact]
    public void AutoShapeFollowsDevice()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a" };

        Assert.Equal(SceneDeviceShape.Strip, SceneShapes.KindOf(DeviceGeometry.Strip("a", 10), placement));
        Assert.Equal(SceneDeviceShape.Strip, SceneShapes.KindOf(DeviceGeometry.Single("a"), placement));
        Assert.Equal(SceneDeviceShape.Keyboard, SceneShapes.KindOf(DeviceGeometry.Grid(4, 2), placement));

        placement.Shape = SceneDeviceShape.Monitor;

        Assert.Equal(SceneDeviceShape.Monitor, SceneShapes.KindOf(DeviceGeometry.Strip("a", 10), placement));
    }

    [Theory]
    [InlineData(SceneDeviceShape.Strip)]
    [InlineData(SceneDeviceShape.Keyboard)]
    [InlineData(SceneDeviceShape.Monitor)]
    [InlineData(SceneDeviceShape.Hexagons)]
    public void EveryShapeKeepsDeviceLeds(SceneDeviceShape kind)
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = kind };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 30), placement);
        var space = SceneSpace.Of(DeviceGeometry.Strip("a", 30), placement, SceneSpread.Space, SceneDirection.LeftToRight);

        Assert.Equal(kind, shape.Kind);
        Assert.Equal(30, shape.Local.Points.Count);
        Assert.Equal(30, space.Points.Count);
    }

    [Fact]
    public void HexagonsShareLedsEvenly()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Hexagons, Panels = 7 };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 14), placement);

        Assert.Equal(7, shape.Panels.Count);
        Assert.All(shape.Panels, p => Assert.Equal(6, p.Corners.Count));
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12 }, shape.Panels.Select(p => p.Led));

        // Светодиоды одного шестигранника стоят в его середине — и цвет у них один
        var points = shape.Local.Points;

        Assert.Equal(points[0]!.Value.X, points[1]!.Value.X, Precision);
        Assert.Equal(points[0]!.Value.Y, points[1]!.Value.Y, Precision);
        Assert.True(Math.Abs(points[0]!.Value.X - points[2]!.Value.X) > 1e-6);
    }

    [Fact]
    public void HexagonsWithoutTheirOwnLedStayDark()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Hexagons, Panels = 5 };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 3), placement);

        Assert.Equal(new[] { 0, 1, 2, -1, -1 }, shape.Panels.Select(p => p.Led));
    }

    [Fact]
    public void SingleColorHexagonsHaveNoLeds()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Hexagons, Panels = 4 };

        var shape = SceneShapes.Of(DeviceGeometry.Single("a"), placement);

        Assert.Empty(shape.Local.Points);
        Assert.Equal(4, shape.Panels.Count);
        Assert.All(shape.Panels, p => Assert.Equal(-1, p.Led));
    }

    [Fact]
    public void MonitorPutsLedsOnScreenEdges()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Monitor };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 40), placement);
        var width = shape.Local.Width;
        var height = shape.Local.Height;

        Assert.Equal(16.0 / 9, width / height, Precision);
        Assert.Equal(4, shape.Body.Count);

        foreach (var pixel in shape.Local.Points.OfType<EffectPixel>())
        {
            var isOnEdge = Math.Abs(pixel.X) < 1e-9 || Math.Abs(pixel.X - width) < 1e-9 ||
                           Math.Abs(pixel.Y) < 1e-9 || Math.Abs(pixel.Y - height) < 1e-9;

            Assert.True(isOnEdge, $"Светодиод ({pixel.X}; {pixel.Y}) не на краю экрана");
        }
    }

    [Fact]
    public void MonitorTakesLayoutOfStripBehindIt()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Monitor };
        var monitor = new MonitorConfig
        {
            DeviceId = "a",
            Rotation = MonitorRotation.Clockwise90,
            Layout = new ScreenLayoutConfig { UseLeft = true, UseTop = false, UseRight = false, UseBottom = false }
        };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 10), placement, monitor);

        // Повёрнутый монитор — книжный; лента только по левому краю
        Assert.True(shape.Local.Height > shape.Local.Width);
        Assert.All(shape.Local.Points.OfType<EffectPixel>(), p => Assert.Equal(0, p.X, Precision));
    }

    [Fact]
    public void StripLaidOutAsKeyboardGetsRowsOfKeys()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Keyboard };

        var shape = SceneShapes.Of(DeviceGeometry.Strip("a", 12), placement);
        var rows = shape.Local.Points.OfType<EffectPixel>().Select(p => Math.Round(p.Y, 6)).Distinct().Count();

        Assert.False(shape.Local.IsLinear);
        Assert.Equal(6, rows);
        Assert.Equal(4, shape.Body.Count);
    }

    [Fact]
    public void PlacedShapeFollowsPlacement()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", Shape = SceneDeviceShape.Monitor, X = 0.25, Y = 0.5, Size = 0.4 };

        var placed = SceneSpace.Place(SceneShapes.Of(DeviceGeometry.Strip("a", 20), placement), placement);
        var left = placed.Body.Min(c => c.X);
        var right = placed.Body.Max(c => c.X);

        Assert.Equal(0.05, left, Precision);
        Assert.Equal(0.45, right, Precision);
        Assert.Equal(0.25, placed.Local.Center.X, Precision);
        Assert.Equal(0.5 * SceneConfig.CanvasHeight, placed.Local.Center.Y, Precision);
    }
}

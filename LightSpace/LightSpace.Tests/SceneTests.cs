using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Space;

namespace LightSpace.Tests;

/// <summary>
/// Сцена профиля: места устройств на холсте, направление эффекта, хранение в настройках и группа сцены в снимке кадров.
/// </summary>
public sealed class SceneTests
{
    private const double Precision = 9;

    [Fact]
    public void StripLiesAlongLineThroughItsMiddle()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", X = 0.5, Y = 0.5, Size = 0.4 };

        var (points, center) = SceneSpace.Place(DeviceGeometry.Strip("a", 2), placement);

        // Светодиоды стоят посередине своих половин: 0.25 и 0.75 длины ленты
        Assert.Equal(0.4, points[0]!.Value.X, Precision);
        Assert.Equal(0.6, points[1]!.Value.X, Precision);
        Assert.All(points, p => Assert.Equal(0.5 * SceneConfig.CanvasHeight, p!.Value.Y, Precision));
        Assert.Equal(0.5, center.X, Precision);
    }

    [Fact]
    public void QuarterTurnStandsStripUpright()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", X = 0.5, Y = 0.5, Size = 0.4, Angle = 90 };

        var (points, _) = SceneSpace.Place(DeviceGeometry.Strip("a", 2), placement);

        // По часовой стрелке: начало ленты сверху, конец снизу
        Assert.All(points, p => Assert.Equal(0.5, p!.Value.X, Precision));
        Assert.Equal(0.3 - 0.1, points[0]!.Value.Y, Precision);
        Assert.Equal(0.3 + 0.1, points[1]!.Value.Y, Precision);
    }

    [Fact]
    public void SingleColorDeviceIsOnePointAtItsPlace()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", X = 0.2, Y = 1 };

        var (points, center) = SceneSpace.Place(DeviceGeometry.Single("a"), placement);

        Assert.Empty(points);
        Assert.Equal(0.2, center.X, Precision);
        Assert.Equal(SceneConfig.CanvasHeight, center.Y, Precision);
    }

    [Fact]
    public void DevicesSideBySideShareOneCanvas()
    {
        var left = new SceneDeviceConfig { DeviceId = "a", X = 0.25, Size = 0.4 };
        var right = new SceneDeviceConfig { DeviceId = "b", X = 0.75, Size = 0.4 };

        var a = SceneSpace.Of(DeviceGeometry.Strip("a", 4), left, SceneSpread.Space, SceneDirection.LeftToRight);
        var b = SceneSpace.Of(DeviceGeometry.Strip("b", 4), right, SceneSpread.Space, SceneDirection.LeftToRight);

        // Эффект идёт по холсту: правая лента целиком правее левой
        Assert.False(a.IsLinear);
        Assert.Equal(1, a.Width, Precision);
        Assert.Equal(SceneConfig.CanvasHeight, a.Height, Precision);
        Assert.True(a.Points.Max(p => p!.Value.X) < b.Points.Min(p => p!.Value.X));
    }

    [Fact]
    public void RightToLeftMirrorsCanvas()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", X = 0.25, Size = 0.4 };

        var space = SceneSpace.Of(DeviceGeometry.Strip("a", 2), placement, SceneSpread.Space, SceneDirection.RightToLeft);

        Assert.Equal(1 - 0.15, space.Points[0]!.Value.X, Precision);
        Assert.Equal(1 - 0.35, space.Points[1]!.Value.X, Precision);
    }

    [Fact]
    public void TopToBottomTurnsCanvasSoEffectGoesDown()
    {
        var top = new SceneDeviceConfig { DeviceId = "a", Y = 0 };
        var bottom = new SceneDeviceConfig { DeviceId = "b", Y = 1 };

        var a = SceneSpace.Of(DeviceGeometry.Single("a"), top, SceneSpread.Space, SceneDirection.TopToBottom);
        var b = SceneSpace.Of(DeviceGeometry.Single("b"), bottom, SceneSpread.Space, SceneDirection.TopToBottom);

        // Эффект всегда идёт по X: верхнее устройство в начале, нижнее в конце
        Assert.Equal(SceneConfig.CanvasHeight, a.Width, Precision);
        Assert.Equal(0, a.Center.X, Precision);
        Assert.Equal(SceneConfig.CanvasHeight, b.Center.X, Precision);
    }

    [Fact]
    public void SyncedStripKeepsItsOwnLineReversedOrFolded()
    {
        var placement = new SceneDeviceConfig { DeviceId = "a", X = 0.9, Size = 0.1 };
        var strip = DeviceGeometry.Strip("a", 2);

        var forward = SceneSpace.Of(strip, placement, SceneSpread.Synced, SceneDirection.LeftToRight);
        var backward = SceneSpace.Of(strip, placement, SceneSpread.Synced, SceneDirection.RightToLeft);
        var fromCenter = SceneSpace.Of(strip, placement, SceneSpread.Synced, SceneDirection.FromCenter);

        // Место на холсте синхронной сцене не важно
        Assert.Equal(0.25, forward.Points[0]!.Value.X, Precision);
        Assert.Equal(0.75, backward.Points[0]!.Value.X, Precision);
        Assert.Equal(0.5, fromCenter.Points[0]!.Value.X, Precision);
        Assert.Equal(0.5, fromCenter.Points[1]!.Value.X, Precision);
    }

    [Fact]
    public void SyncedDevicesPlaySameColorsAtSameTime()
    {
        var effect = EffectLibrary.FindOrSolid("rainbow");
        var first = SceneSpace.Of(DeviceGeometry.Strip("a", 8), new SceneDeviceConfig { DeviceId = "a", X = 0.1 }, SceneSpread.Synced, SceneDirection.LeftToRight);
        var second = SceneSpace.Of(DeviceGeometry.Strip("b", 8), new SceneDeviceConfig { DeviceId = "b", X = 0.9 }, SceneSpread.Synced, SceneDirection.LeftToRight);

        Assert.Equal(EffectRenderer.Render(effect, EffectValues.Empty, first, 2.5), EffectRenderer.Render(effect, EffectValues.Empty, second, 2.5));
    }

    [Fact]
    public void NormalizeKeepsSceneValid()
    {
        var scene = new SceneConfig
        {
            Brightness = 140,
            Effect = new ContentConfig { Kind = ContentKind.Music, EffectId = "wave" },
            Devices = new List<SceneDeviceConfig>
            {
                new() { DeviceId = "a", X = 2, Y = double.NaN, Size = 0, Angle = -90, Shape = (SceneDeviceShape)99, Panels = 500, Brightness = -5 },
                new() { DeviceId = "a", X = 0.1 },
                new() { DeviceId = "" }
            }
        };

        scene.Normalize();

        // Сцена играет только эффект, устройство входит в неё один раз
        Assert.Equal(100, scene.Brightness);
        Assert.Equal(ContentKind.Effect, scene.Effect.Kind);
        Assert.Equal("wave", scene.Effect.EffectId);

        var device = Assert.Single(scene.Devices);

        Assert.Equal(1, device.X);
        Assert.Equal(0.5, device.Y);
        Assert.Equal(SceneDeviceConfig.MinSize, device.Size);
        Assert.Equal(270, device.Angle);
        Assert.Equal(SceneDeviceShape.Auto, device.Shape);
        Assert.Equal(SceneDeviceConfig.MaxPanels, device.Panels);
        Assert.Equal(0, device.Brightness);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var scene = new SceneConfig { Devices = new List<SceneDeviceConfig> { new() { DeviceId = "a" } } };

        var copy = scene.Clone();

        copy.Devices[0].X = 0.1;
        copy.Effect.EffectId = "fire";
        copy.Devices.Add(new SceneDeviceConfig { DeviceId = "b" });

        Assert.Equal(0.5, scene.Devices[0].X);
        Assert.Equal("solid", scene.Effect.EffectId);
        Assert.Single(scene.Devices);
    }

    [Fact]
    public void SavedProfileKeepsCopyOfPlayingScene()
    {
        var config = ConfigWithDevices("a", "b");

        config.Scene = SceneOf("a", "b");

        var profile = config.SaveProfile("Вечер");

        config.Scene.Devices.Clear();

        Assert.Equal(new[] { "a", "b" }, profile.Scene!.Devices.Select(d => d.DeviceId));
    }

    [Fact]
    public void UpdatedProfileKeepsItsOwnScene()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Вечер");

        profile.Scene = SceneOf("a");
        config.UpdateProfile(profile);

        Assert.Equal("a", Assert.Single(profile.Scene!.Devices).DeviceId);
    }

    [Fact]
    public void AppliedProfilePlaysCopyOfItsSceneWithKnownDevices()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Вечер");

        profile.Scene = SceneOf("a", "забытое");
        config.ApplyProfile(profile);

        Assert.Equal("a", Assert.Single(config.Scene!.Devices).DeviceId);
        Assert.Equal(2, profile.Scene.Devices.Count);
    }

    [Fact]
    public void ProfileWithoutSceneStopsPlayingScene()
    {
        var config = ConfigWithDevices("a");
        var profile = config.SaveProfile("Вечер");

        config.Scene = SceneOf("a");
        config.ApplyProfile(profile);

        Assert.Null(config.Scene);
    }

    [Fact]
    public void ForgottenDeviceLeavesPlayingScene()
    {
        var config = ConfigWithDevices("a", "b");

        config.Scene = SceneOf("a", "b");
        config.RemoveDeviceLighting("a");

        Assert.Equal("b", Assert.Single(config.Scene.Devices).DeviceId);
    }

    [Fact]
    public void SceneIsReadFromFile()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 5,
              "Devices": [ { "Id": "a" }, { "Id": "b" } ],
              "Scene": {
                "Spread": "Synced",
                "Direction": "FromCenter",
                "Brightness": 40,
                "Effect": { "Kind": "Effect", "EffectId": "fire" },
                "Devices": [ { "DeviceId": "a", "X": 0.2, "Angle": 90, "Shape": "Hexagons", "Panels": 12 }, { "DeviceId": "нет" } ]
              },
              "Profiles": [ { "Id": "p", "Name": "Вечер", "Scene": { "Devices": [ { "DeviceId": "b" } ] } } ]
            }
            """);

        Assert.Equal(SceneSpread.Synced, config.Scene!.Spread);
        Assert.Equal(SceneDirection.FromCenter, config.Scene.Direction);
        Assert.Equal(40, config.Scene.Brightness);
        Assert.Equal("fire", config.Scene.Effect.EffectId);

        // Забытое устройство сцену не играет
        var device = Assert.Single(config.Scene.Devices);

        Assert.Equal(0.2, device.X);
        Assert.Equal(90, device.Angle);
        Assert.Equal(SceneDeviceShape.Hexagons, device.Shape);
        Assert.Equal(12, device.Panels);

        // Чего нет в файле — по умолчанию: вид по самому устройству, полная яркость
        var saved = Assert.Single(config.Profiles[0].Scene!.Devices);

        Assert.Equal("b", saved.DeviceId);
        Assert.Equal(SceneDeviceShape.Auto, saved.Shape);
        Assert.Equal(100, saved.Brightness);
    }

    [Fact]
    public void SnapshotPlaysSceneInsteadOfDeviceLighting()
    {
        var config = ConfigWithDevices("a", "b");
        var devices = config.Devices.Select(d => new ManagedDevice(d)).ToList();

        foreach (var device in devices)
            device.Connect(new FakeLed(device.Id), isLightingEnabled: true);

        config.Scene = SceneOf("a");
        config.Scene.Brightness = 50;
        config.Scene.Devices[0].Brightness = 60;
        config.MasterBrightness = 80;

        try
        {
            var snapshot = FrameSnapshot.Build(config, devices);

            // Устройство b играет свою подсветку, a — только сцену
            Assert.Equal(new[] { "b", FrameSnapshot.SceneGroupId }, snapshot.Groups.Select(g => g.Id));

            var scene = snapshot.Groups[1];

            Assert.Equal("a", Assert.Single(scene.Devices).Id);
            Assert.Equal("rainbow", scene.Active.Effect.Id);
            Assert.Equal(40, scene.Brightness);

            // Своя яркость устройства в сцене приглушает его поверх яркости сцены
            Assert.Equal(60, scene.Devices[0].Brightness);
        }
        finally
        {
            foreach (var device in devices)
                device.Disconnect();
        }
    }

    [Fact]
    public void SnapshotSkipsSceneWithoutDevices()
    {
        var config = ConfigWithDevices("a");

        config.Scene = new SceneConfig();

        var snapshot = FrameSnapshot.Build(config, Array.Empty<ManagedDevice>());

        Assert.Equal("a", Assert.Single(snapshot.Groups).Id);
    }

    private static AppConfig ConfigWithDevices(params string[] ids)
    {
        var config = new AppConfig();

        foreach (var id in ids)
            config.Devices.Add(new DeviceConfig { Id = id });

        config.Normalize();

        return config;
    }

    private static SceneConfig SceneOf(params string[] ids)
    {
        return new SceneConfig
        {
            Effect = new ContentConfig { Kind = ContentKind.Effect, EffectId = "rainbow" },
            Devices = ids.Select(id => new SceneDeviceConfig { DeviceId = id }).ToList()
        };
    }
}

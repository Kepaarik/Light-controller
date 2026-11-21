using LightSpace.Core.Config;

namespace LightSpace.Tests;

/// <summary>
/// Файлы настроек старых версий после загрузки дают то же, что играло до обновления.
/// </summary>
public sealed class ConfigMigrationTests
{
    [Fact]
    public void EmptyFileGivesDefaults()
    {
        var config = ConfigStore.Parse("{}");

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.True(config.LightingEnabled);
        Assert.Empty(config.Devices);
        Assert.Empty(config.DeviceLighting);
    }

    [Fact]
    public void DeviceColorFromVersion2BecomesSolidEffectOfTheDevice()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 2,
              "Devices": [
                { "Id": "ble:AABBCCDDEEFF", "Color": "#FF0000", "Brightness": 40 }
              ]
            }
            """);

        var lighting = Assert.Single(config.DeviceLighting);

        Assert.Equal("ble:AABBCCDDEEFF", lighting.DeviceId);
        Assert.Equal(40, lighting.Brightness);
        Assert.Equal("solid", lighting.Content.EffectId);
        Assert.Equal("#FF0000", lighting.Content.Effects["solid"].Colors["color"]);
    }

    [Fact]
    public void SharedContentFromVersion3IsCopiedToEveryDevice()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 3,
              "Brightness": 70,
              "DefaultContent": { "EffectId": "rainbow" },
              "Devices": [ { "Id": "a" }, { "Id": "b" } ]
            }
            """);

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.Equal(2, config.DeviceLighting.Count);

        foreach (var id in new[] { "a", "b" })
        {
            var lighting = config.EnsureDeviceLighting(id);

            Assert.Equal(70, lighting.Brightness);
            Assert.Equal("rainbow", lighting.Content.EffectId);
        }

        // Копии независимы: правка одного устройства не меняет другое
        config.EnsureDeviceLighting("a").Content.EffectId = "solid";
        Assert.Equal("rainbow", config.EnsureDeviceLighting("b").Content.EffectId);
    }

    [Fact]
    public void FileFromNewerVersionIsReadAsIs()
    {
        var config = ConfigStore.Parse("""
            { "Version": 99, "LightingEnabled": false, "UnknownField": 1 }
            """);

        Assert.Equal(99, config.Version);
        Assert.False(config.LightingEnabled);
    }

    [Fact]
    public void DuplicateAndEmptyDeviceIdsAreDropped()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a" }, { "Id": "a" }, { "Id": "" } ]
            }
            """);

        var device = Assert.Single(config.Devices);

        Assert.Equal("a", device.Id);
        Assert.Single(config.DeviceLighting);
    }

    [Fact]
    public void OutOfRangeBrightnessIsClamped()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a" } ],
              "Groups": [ { "Id": "g", "IsSolo": true, "DeviceIds": [ "a" ], "Brightness": 250 } ]
            }
            """);

        Assert.Equal(100, config.EnsureDeviceLighting("a").Brightness);
    }

    [Fact]
    public void DeviceLightingFromVersion4KeepsItsContentAndBrightness()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a" } ],
              "DeviceLighting": [
                {
                  "Id": "x", "IsSolo": true, "Name": "", "Color": "", "DeviceIds": [ "a" ], "Brightness": 35,
                  "DefaultContent": { "Kind": "Music", "EffectId": "rainbow" }
                }
              ]
            }
            """);

        var lighting = Assert.Single(config.DeviceLighting);

        Assert.Equal("a", lighting.DeviceId);
        Assert.Equal(35, lighting.Brightness);
        Assert.Equal(ContentKind.Music, lighting.Content.Kind);
        Assert.Equal("rainbow", lighting.Content.EffectId);
    }

    [Fact]
    public void DeviceLightingWinsOverGroupOfVersion4()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a" }, { "Id": "b" } ],
              "Groups": [
                { "Id": "g", "Name": "Стол", "DeviceIds": [ "a", "b" ], "Brightness": 20, "DefaultContent": { "EffectId": "fire" } }
              ],
              "DeviceLighting": [ { "IsSolo": true, "DeviceIds": [ "a" ], "Brightness": 90, "DefaultContent": { "EffectId": "rainbow" } } ]
            }
            """);

        // У «a» своя запись, «b» без записи играет то же, что его группа
        Assert.Equal("rainbow", config.EnsureDeviceLighting("a").Content.EffectId);
        Assert.Equal(90, config.EnsureDeviceLighting("a").Brightness);
        Assert.Equal("fire", config.EnsureDeviceLighting("b").Content.EffectId);
        Assert.Equal(20, config.EnsureDeviceLighting("b").Brightness);
    }

    [Fact]
    public void SceneContentFromVersion4BecomesEffect()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Mode": "WholeScene",
              "Scene": { "Id": "scene", "DefaultContent": { "Kind": "Scene" } },
              "Devices": [ { "Id": "a", "Placement": { "X": 10, "Y": 20, "Size": 30, "Rotation": 90 } } ],
              "DeviceLighting": [
                { "DeviceIds": [ "a" ], "DefaultContent": { "Kind": "Scene", "EffectId": "wave", "Layers": [ { "Id": "l" } ] } }
              ],
              "Profiles": [
                { "Name": "Ночь", "DeviceLighting": [ { "DeviceIds": [ "a" ], "Brightness": 10, "DefaultContent": { "Kind": "Scene" } } ] }
              ]
            }
            """);

        var lighting = Assert.Single(config.DeviceLighting);
        var saved = Assert.Single(Assert.Single(config.Profiles).DeviceLighting);

        Assert.Equal(ContentKind.Effect, lighting.Content.Kind);
        Assert.Equal("wave", lighting.Content.EffectId);
        Assert.Equal("a", saved.DeviceId);
        Assert.Equal(10, saved.Brightness);
        Assert.Equal(ContentKind.Effect, saved.Content.Kind);
    }

    [Fact]
    public void GameContentFromVersion5BecomesEffect()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 5,
              "Devices": [ { "Id": "a" } ],
              "DeviceLighting": [
                { "DeviceId": "a", "Brightness": 70, "Content": { "Kind": "Game", "EffectId": "fire", "Game": { "Game": "Dota2" } } }
              ],
              "Scene": { "Effect": { "Kind": "Game" }, "Devices": [ { "DeviceId": "a" } ] },
              "Profiles": [
                {
                  "Name": "Ночь",
                  "DeviceLighting": [ { "DeviceId": "a", "Content": { "Kind": "Game" } } ],
                  "Scene": { "Effect": { "Kind": "Game" }, "Devices": [ { "DeviceId": "a" } ] }
                }
              ]
            }
            """);

        var lighting = Assert.Single(config.DeviceLighting);
        var profile = Assert.Single(config.Profiles);

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.Equal(ContentKind.Effect, lighting.Content.Kind);
        Assert.Equal("fire", lighting.Content.EffectId);
        Assert.Equal(70, lighting.Brightness);
        Assert.Equal(ContentKind.Effect, config.Scene!.Effect.Kind);
        Assert.Equal(ContentKind.Effect, Assert.Single(profile.DeviceLighting).Content.Kind);
        Assert.Equal(ContentKind.Effect, profile.Scene!.Effect.Kind);
    }
}

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
        Assert.Empty(config.Groups);
    }

    [Fact]
    public void DeviceColorFromVersion2BecomesSolidEffectOfItsGroup()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 2,
              "Devices": [
                { "Id": "ble:AABBCCDDEEFF", "Color": "#FF0000", "Brightness": 40 }
              ]
            }
            """);

        var group = Assert.Single(config.Groups);

        Assert.True(group.IsSolo);
        Assert.Equal(new[] { "ble:AABBCCDDEEFF" }, group.DeviceIds);
        Assert.Equal(40, group.Brightness);
        Assert.Equal("solid", group.DefaultContent.EffectId);
        Assert.Equal("#FF0000", group.DefaultContent.Effects["solid"].Colors["color"]);
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
        Assert.Equal(2, config.Groups.Count);

        foreach (var id in new[] { "a", "b" })
        {
            var group = config.GroupOf(id);

            Assert.NotNull(group);
            Assert.True(group.IsSolo);
            Assert.Equal(70, group.Brightness);
            Assert.Equal("rainbow", group.DefaultContent.EffectId);
        }

        // Копии независимы: правка одной группы не меняет другую
        config.GroupOf("a")!.DefaultContent.EffectId = "solid";
        Assert.Equal("rainbow", config.GroupOf("b")!.DefaultContent.EffectId);
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
        Assert.Single(config.Groups);
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

        Assert.Equal(100, config.GroupOf("a")!.Brightness);
    }
}

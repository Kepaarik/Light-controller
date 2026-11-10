using LightSpace.Core.Config;

namespace LightSpace.Tests;

/// <summary>
/// Профиль запоминает и возвращает яркость и содержимое устройств.
/// </summary>
public sealed class ProfileTests
{
    [Fact]
    public void AppliedProfileRestoresSavedLighting()
    {
        var config = ConfigWithDevices("a", "b");

        config.EnsureDeviceLighting("a").Brightness = 30;
        config.EnsureDeviceLighting("a").DefaultContent.EffectId = "rainbow";

        var profile = config.SaveProfile("Вечер");

        config.EnsureDeviceLighting("a").Brightness = 100;
        config.EnsureDeviceLighting("a").DefaultContent.EffectId = "solid";

        config.ApplyProfile(profile);

        Assert.Equal(30, config.EnsureDeviceLighting("a").Brightness);
        Assert.Equal("rainbow", config.EnsureDeviceLighting("a").DefaultContent.EffectId);
    }

    [Fact]
    public void ProfileIsNotChangedByLaterEdits()
    {
        var config = ConfigWithDevices("a");
        var profile = config.SaveProfile("Вечер");

        config.EnsureDeviceLighting("a").DefaultContent.EffectId = "rainbow";

        Assert.Equal("solid", profile.DeviceLighting[0].DefaultContent.EffectId);
    }

    [Fact]
    public void DevicesMissingFromProfileKeepTheirLighting()
    {
        var config = ConfigWithDevices("a");
        var profile = config.SaveProfile("Только a");

        config.Devices.Add(new DeviceConfig { Id = "b" });
        config.EnsureDeviceLighting("b").DefaultContent.EffectId = "rainbow";

        config.ApplyProfile(profile);

        Assert.Equal("rainbow", config.EnsureDeviceLighting("b").DefaultContent.EffectId);
    }

    [Fact]
    public void ForgottenDevicesInProfileAreSkipped()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Оба");

        config.Devices.RemoveAll(d => d.Id == "b");
        config.DeviceLighting.RemoveAll(l => l.DeviceIds.Contains("b"));

        config.ApplyProfile(profile);

        Assert.DoesNotContain(config.DeviceLighting, l => l.DeviceIds.Contains("b"));
    }

    [Fact]
    public void UpdatedProfileGetsCurrentLighting()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Вечер");

        config.EnsureDeviceLighting("a").Brightness = 40;
        config.EnsureDeviceLighting("b").DefaultContent.EffectId = "rainbow";

        config.UpdateProfile(profile);

        Assert.Equal(2, profile.DeviceLighting.Count);
        Assert.Equal(40, profile.DeviceLighting.Single(l => l.DeviceIds[0] == "a").Brightness);
        Assert.Equal("rainbow", profile.DeviceLighting.Single(l => l.DeviceIds[0] == "b").DefaultContent.EffectId);
    }

    [Fact]
    public void UpdatedProfileIsNotChangedByLaterEdits()
    {
        var config = ConfigWithDevices("a");
        var profile = config.SaveProfile("Вечер");

        config.UpdateProfile(profile);
        config.EnsureDeviceLighting("a").Brightness = 5;
        config.EnsureDeviceLighting("a").DefaultContent.EffectId = "rainbow";

        Assert.Equal(100, profile.DeviceLighting[0].Brightness);
        Assert.Equal("solid", profile.DeviceLighting[0].DefaultContent.EffectId);
    }

    [Fact]
    public void UpdatedProfileKeepsItsIdAndName()
    {
        var config = ConfigWithDevices("a");
        var profile = config.SaveProfile("Вечер");
        var id = profile.Id;

        config.UpdateProfile(profile);

        Assert.Equal(id, profile.Id);
        Assert.Equal("Вечер", profile.Name);
        Assert.Single(config.Profiles);
    }

    [Fact]
    public void UpdatedProfileDropsForgottenDevices()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Оба");

        config.Devices.RemoveAll(d => d.Id == "b");
        config.DeviceLighting.RemoveAll(l => l.DeviceIds.Contains("b"));

        config.UpdateProfile(profile);

        Assert.Equal("a", Assert.Single(profile.DeviceLighting).DeviceIds[0]);
    }

    [Fact]
    public void CustomNameAndIconSurviveLoading()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a", "Name": "ELK-BLE", "CustomName": "Стол", "Icon": "" }, { "Id": "b" } ]
            }
            """);

        var named = config.Devices.Single(d => d.Id == "a");
        var plain = config.Devices.Single(d => d.Id == "b");

        Assert.Equal("Стол", named.CustomName);
        Assert.Equal("", named.Icon);
        Assert.Null(plain.CustomName);
        Assert.Null(plain.Icon);
    }

    [Fact]
    public void MasterBrightnessAndProfilesSurviveLoading()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "MasterBrightness": 140,
              "Devices": [ { "Id": "a" } ],
              "Profiles": [
                { "Name": "Ночь", "DeviceLighting": [ { "IsSolo": true, "DeviceIds": [ "a" ], "Brightness": 10 } ] },
                null
              ]
            }
            """);

        Assert.Equal(100, config.MasterBrightness);

        var profile = Assert.Single(config.Profiles);

        Assert.False(string.IsNullOrWhiteSpace(profile.Id));
        Assert.Equal(10, Assert.Single(profile.DeviceLighting).Brightness);
    }

    [Fact]
    public void OldFileGetsFullMasterBrightness()
    {
        Assert.Equal(100, ConfigStore.Parse("""{ "Version": 4 }""").MasterBrightness);
    }

    private static AppConfig ConfigWithDevices(params string[] ids)
    {
        var config = new AppConfig();

        foreach (var id in ids)
            config.Devices.Add(new DeviceConfig { Id = id });

        config.Normalize();

        return config;
    }
}

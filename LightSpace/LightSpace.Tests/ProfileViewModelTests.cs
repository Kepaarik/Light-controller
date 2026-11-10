using LightSpace.App.ViewModels;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;

namespace LightSpace.Tests;

/// <summary>
/// Профиль в разделе «Профили»: что он включает на каждом устройстве, правка названия, яркости и состава.
/// </summary>
public sealed class ProfileViewModelTests
{
    [Fact]
    public void EffectIsDescribedByItsTitle()
    {
        var content = new ContentConfig { Kind = ContentKind.Effect, EffectId = "solid" };

        Assert.Equal($"Эффект · {EffectLibrary.Solid.Title}", ProfileDeviceViewModel.DescribeContent(content, "a", null));
    }

    [Fact]
    public void UnknownEffectIsDescribedAsSolidColor()
    {
        var content = new ContentConfig { Kind = ContentKind.Effect, EffectId = "нет-такого" };

        Assert.Equal($"Эффект · {EffectLibrary.Solid.Title}", ProfileDeviceViewModel.DescribeContent(content, "a", null));
    }

    [Theory]
    [InlineData(ContentKind.Music, "Музыка")]
    [InlineData(ContentKind.Screen, "Экран")]
    [InlineData(ContentKind.Scene, "Сцена")]
    [InlineData(ContentKind.Game, "Игра")]
    public void OtherKindsAreDescribedByName(ContentKind kind, string expected)
    {
        Assert.Equal(expected, ProfileDeviceViewModel.DescribeContent(new ContentConfig { Kind = kind }, "a", null));
    }

    [Fact]
    public void BuiltinEffectOfConnectedDeviceIsDescribedByItsTitle()
    {
        var device = ConnectedDevice(new FakeBuiltinLed("a"));
        var content = BuiltinContent("a", 7);

        Assert.Equal("Встроенный · Пульс", ProfileDeviceViewModel.DescribeContent(content, "a", device));

        device.Disconnect();
    }

    [Fact]
    public void BuiltinEffectWithoutConnectedDeviceHasGeneralDescription()
    {
        var content = BuiltinContent("a", 7);

        Assert.Equal("Встроенный эффект", ProfileDeviceViewModel.DescribeContent(content, "a", null));
        Assert.Equal("Встроенный эффект", ProfileDeviceViewModel.DescribeContent(content, "a", new ManagedDevice(new DeviceConfig { Id = "a" })));
    }

    [Fact]
    public void UnknownBuiltinEffectHasGeneralDescription()
    {
        var device = ConnectedDevice(new FakeBuiltinLed("a"));

        Assert.Equal("Встроенный эффект", ProfileDeviceViewModel.DescribeContent(BuiltinContent("a", 99), "a", device));

        device.Disconnect();
    }

    [Fact]
    public void ForgottenDeviceIsShownAsForgotten()
    {
        var row = new ProfileDeviceViewModel(GroupConfig.Solo("a", new ContentConfig(), 50), null, () => { }, _ => { });

        Assert.Equal("Забытое устройство", row.Name);
        Assert.False(row.IsKnown);
        Assert.Equal(DeviceIcons.Strip, row.Icon);
    }

    [Fact]
    public void KnownDeviceShowsItsNameAndChosenIcon()
    {
        var device = new ManagedDevice(new DeviceConfig { Id = "a", Name = "ELK-BLE", CustomName = "Стол", Icon = DeviceIcons.All[4] });
        var row = new ProfileDeviceViewModel(GroupConfig.Solo("a", new ContentConfig(), 50), device, () => { }, _ => { });

        Assert.Equal("Стол", row.Name);
        Assert.True(row.IsKnown);
        Assert.Equal(DeviceIcons.All[4], row.Icon);
    }

    [Fact]
    public void ConnectedKeyboardWithoutChosenIconGetsKeyboardIcon()
    {
        var device = ConnectedDevice(new FakeKeyboard("a"));
        var row = new ProfileDeviceViewModel(GroupConfig.Solo("a", new ContentConfig(), 50), device, () => { }, _ => { });

        Assert.Equal(DeviceIcons.Keyboard, row.Icon);

        device.Disconnect();
    }

    [Fact]
    public void BrightnessIsClampedAndReportedOncePerChange()
    {
        var changes = 0;
        var lighting = GroupConfig.Solo("a", new ContentConfig(), 50);
        var row = new ProfileDeviceViewModel(lighting, null, () => changes++, _ => { });

        row.Brightness = 150;
        row.Brightness = 100;
        row.Brightness = -5;
        row.Brightness = 42.4;

        Assert.Equal(42, lighting.Brightness);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ProfileListsItsDevices()
    {
        var profile = Profile(("a", 30), ("b", 80));
        var option = new ProfileOption(profile, id => Device(id), () => { });

        Assert.Equal(2, option.Devices.Count);
        Assert.Equal("Устройств: 2", option.DevicesText);
        Assert.Equal(
            $"Лента a: Эффект · {EffectLibrary.Solid.Title}, 30 %{Environment.NewLine}Лента b: Эффект · {EffectLibrary.Solid.Title}, 80 %",
            option.SummaryText);
    }

    [Fact]
    public void EmptyProfileSaysSo()
    {
        var option = new ProfileOption(Profile(), _ => null, () => { });

        Assert.Equal("Устройств: 0", option.DevicesText);
        Assert.Equal("В профиле нет устройств", option.SummaryText);
    }

    [Fact]
    public void RemovedDeviceLeavesProfile()
    {
        var changes = 0;
        var profile = Profile(("a", 30), ("b", 80));
        var option = new ProfileOption(profile, id => Device(id), () => changes++);
        var changed = new List<string?>();

        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        option.Devices[0].RemoveCommand.Execute(null);

        Assert.Equal("b", Assert.Single(profile.DeviceLighting).DeviceIds[0]);
        Assert.Single(option.Devices);
        Assert.Equal("Устройств: 1", option.DevicesText);
        Assert.Contains(nameof(ProfileOption.DevicesText), changed);
        Assert.Contains(nameof(ProfileOption.SummaryText), changed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void BrightnessChangeUpdatesSummary()
    {
        var changes = 0;
        var option = new ProfileOption(Profile(("a", 30)), id => Device(id), () => changes++);
        var changed = new List<string?>();

        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        option.Devices[0].Brightness = 60;

        Assert.Contains("60 %", option.SummaryText);
        Assert.Contains(nameof(ProfileOption.SummaryText), changed);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NameIsTrimmed()
    {
        var changes = 0;
        var option = new ProfileOption(Profile(), _ => null, () => changes++);

        option.Name = "  Ночь  ";

        Assert.Equal("Ночь", option.Settings.Name);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Вечер")]
    public void BlankOrSameNameIsNotSaved(string name)
    {
        var changes = 0;
        var option = new ProfileOption(Profile(), _ => null, () => changes++);
        var changed = new List<string?>();

        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        option.Name = name;

        Assert.Equal("Вечер", option.Settings.Name);
        Assert.Equal(0, changes);

        // Поле названия возвращается к сохранённому
        Assert.Contains(nameof(ProfileOption.Name), changed);
    }

    [Fact]
    public void ReloadShowsRewrittenProfile()
    {
        var profile = Profile(("a", 30));
        var option = new ProfileOption(profile, id => Device(id), () => { });

        profile.DeviceLighting = new List<GroupConfig> { GroupConfig.Solo("b", new ContentConfig { Kind = ContentKind.Music }, 70) };
        option.Reload();

        var row = Assert.Single(option.Devices);

        Assert.Equal("Лента b", row.Name);
        Assert.Equal("Музыка", row.ContentText);
        Assert.Equal("Устройств: 1", option.DevicesText);
    }

    [Fact]
    public void IconsToChooseFromAreDistinctFluentGlyphs()
    {
        Assert.Contains(DeviceIcons.Strip, DeviceIcons.All);
        Assert.Contains(DeviceIcons.Keyboard, DeviceIcons.All);
        Assert.Equal(DeviceIcons.All.Count, DeviceIcons.All.Distinct().Count());

        // Каждый значок — один символ из области Segoe Fluent Icons (Private Use Area)
        Assert.All(DeviceIcons.All, icon =>
        {
            Assert.Single(icon);
            Assert.InRange(icon[0], '', '');
        });
    }

    private static ProfileConfig Profile(params (string Id, int Brightness)[] devices)
    {
        return new ProfileConfig
        {
            Id = GroupConfig.NewId(),
            Name = "Вечер",
            DeviceLighting = devices.Select(d => GroupConfig.Solo(d.Id, new ContentConfig(), d.Brightness)).ToList()
        };
    }

    private static ManagedDevice Device(string id)
    {
        return new ManagedDevice(new DeviceConfig { Id = id, Name = $"Лента {id}" });
    }

    private static ManagedDevice ConnectedDevice(FakeLed driver)
    {
        var device = new ManagedDevice(new DeviceConfig { Id = driver.Id });

        device.Connect(driver, isLightingEnabled: false);

        return device;
    }

    private static ContentConfig BuiltinContent(string deviceId, int effectId)
    {
        var content = new ContentConfig { Kind = ContentKind.Builtin };

        content.Builtin.Effects[deviceId] = effectId;

        return content;
    }
}

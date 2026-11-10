using LightSpace.App.ViewModels;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.Tests;

/// <summary>
/// Модель окна: название и значок устройства, профили и их отметка, строка связи в меню трея,
/// значок и состояние устройства в списке «Устройства».
/// </summary>
public sealed class MainViewModelTests
{
    [Fact]
    public void RenamedDeviceShowsNewNameEverywhere()
    {
        WithViewModel(vm =>
        {
            vm.RenameDevice("a", "  Стол  ");

            Assert.Equal("Стол", Settings(vm, "a").CustomName);
            Assert.Equal("Стол", vm.Devices.Items.Single(i => i.Device.Id == "a").Name);
            Assert.Equal("Стол", vm.DeviceNav.Single(d => d.Id == "a").Title);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ELK-BLE")]
    public void BlankNameOrDriverNameReturnsDriverName(string name)
    {
        WithViewModel(vm =>
        {
            vm.RenameDevice("a", "Стол");
            vm.RenameDevice("a", name);

            Assert.Null(Settings(vm, "a").CustomName);
            Assert.Equal("ELK-BLE", vm.Devices.Items.Single(i => i.Device.Id == "a").Name);
        });
    }

    [Fact]
    public void RenamingUnknownDeviceDoesNothing()
    {
        WithViewModel(vm =>
        {
            vm.RenameDevice("нет-такого", "Стол");

            Assert.All(vm.Devices.Items, i => Assert.Null(i.Device.Settings.CustomName));
        });
    }

    [Fact]
    public void ChosenIconShowsInMenuAndDeviceList()
    {
        WithViewModel(vm =>
        {
            var icon = DeviceIcons.All[5];

            vm.SetDeviceIcon("a", icon);

            Assert.Equal(icon, Settings(vm, "a").Icon);
            Assert.Equal(icon, vm.DeviceNav.Single(d => d.Id == "a").Icon);
            Assert.Equal(icon, vm.Devices.Items.Single(i => i.Device.Id == "a").Icon);
            Assert.Null(Settings(vm, "b").Icon);
        });
    }

    [Fact]
    public void DeviceWithoutChosenIconGetsIconByKind()
    {
        WithViewModel(vm =>
        {
            var strip = vm.Devices.Items.Single(i => i.Device.Id == "a");
            var keyboard = vm.Devices.Items.Single(i => i.Device.Id == "b");

            Assert.Equal(DeviceIcons.Strip, strip.Icon);

            keyboard.Device.Connect(new FakeKeyboard("b"), isLightingEnabled: false);

            Assert.Equal(DeviceIcons.Keyboard, keyboard.Icon);
        });
    }

    [Fact]
    public void DeviceListItemShowsConnectionStatus()
    {
        WithViewModel(vm =>
        {
            var item = vm.Devices.Items.Single(i => i.Device.Id == "a");

            Assert.Equal(DeviceStatus.NotFound, item.Status);
            Assert.Equal("Не найдено", item.StatusTitle);

            item.Device.Connect(new FakeLed("a", "ELK-BLE"), isLightingEnabled: false);

            Assert.Equal(DeviceStatus.Connected, item.Status);
            Assert.Equal("Подключено", item.StatusTitle);
        });
    }

    [Fact]
    public void TrayHeaderCountsDevicesOnline()
    {
        WithViewModel(vm =>
        {
            Assert.Equal("На связи 0 из 2", vm.Devices.ConnectionText);
            Assert.Equal("Нет связи: 2", vm.Devices.ProblemText);

            vm.Devices.Items.Single(i => i.Device.Id == "a").Device.Connect(new FakeLed("a", "ELK-BLE"), isLightingEnabled: false);

            Assert.Equal("На связи 1 из 2", vm.Devices.ConnectionText);
            Assert.Equal("Нет связи: 1", vm.Devices.ProblemText);

            vm.Devices.Items.Single(i => i.Device.Id == "b").Device.Connect(new FakeLed("b"), isLightingEnabled: false);

            Assert.Equal("На связи 2 из 2", vm.Devices.ConnectionText);
            Assert.Equal("", vm.Devices.ProblemText);
        });
    }

    [Fact]
    public void TrayHeaderWithoutDevicesSaysNoneFound()
    {
        WithViewModel(vm =>
        {
            Assert.Equal("Устройства не найдены", vm.Devices.ConnectionText);
            Assert.Equal("", vm.Devices.ProblemText);
        }, Config());
    }

    [Fact]
    public void NewProfileIsOpenedAndMarkedPlaying()
    {
        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);

            var first = Assert.Single(vm.Profiles);

            Assert.True(vm.HasProfiles);
            Assert.Same(first, vm.SelectedProfile);
            Assert.True(vm.HasSelectedProfile);
            Assert.True(first.IsActive);
            Assert.Equal(2, first.Devices.Count);

            vm.NewProfileCommand.Execute(null);

            Assert.False(first.IsActive);
            Assert.True(vm.Profiles[1].IsActive);
            Assert.Equal("Профиль 2", vm.Profiles[1].Name);
        });
    }

    [Fact]
    public void AppliedProfileIsMarkedPlaying()
    {
        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);
            vm.NewProfileCommand.Execute(null);

            vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

            Assert.True(vm.Profiles[0].IsActive);
            Assert.False(vm.Profiles[1].IsActive);
        });
    }

    [Fact]
    public void AppliedProfileRestoresDeviceBrightness()
    {
        var config = Config(("a", "ELK-BLE"), ("b", "Лента b"));

        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);
            vm.Profiles[0].Devices.Single(d => d.Lighting.DeviceIds[0] == "a").Brightness = 25;

            vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

            Assert.Equal(25, config.EnsureDeviceLighting("a").Brightness);
        }, config);
    }

    [Fact]
    public void EditedProfileIsNoLongerMarkedPlaying()
    {
        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);

            var profile = vm.Profiles[0];

            profile.Name = "Ночь";

            Assert.False(profile.IsActive);

            vm.ApplyProfileCommand.Execute(profile);
            profile.Devices[0].Brightness = 10;

            Assert.False(profile.IsActive);
        });
    }

    [Fact]
    public void SavedProfilesAreListedWithTheirDevices()
    {
        var config = Config(("a", "ELK-BLE"));
        var profile = config.SaveProfile("Ночь");

        WithViewModel(vm =>
        {
            var option = Assert.Single(vm.Profiles);

            Assert.Same(profile, option.Settings);
            Assert.False(option.IsActive);
            Assert.Equal("ELK-BLE", Assert.Single(option.Devices).Name);
        }, config);
    }

    private static DeviceConfig Settings(MainViewModel vm, string id)
    {
        return vm.Devices.Items.Single(i => i.Device.Id == id).Device.Settings;
    }

    /// <summary>
    /// Две ленты по умолчанию: «a» с именем от драйвера ELK-BLE и «b».
    /// </summary>
    private static void WithViewModel(Action<MainViewModel> test, AppConfig? config = null)
    {
        config ??= Config(("a", "ELK-BLE"), ("b", "Лента b"));

        UiThread.Invoke(() =>
        {
            var vm = new MainViewModel(config);

            try
            {
                test(vm);
            }
            finally
            {
                vm.Shutdown();
            }
        });
    }

    private static AppConfig Config(params (string Id, string Name)[] devices)
    {
        var config = new AppConfig();

        foreach (var (id, name) in devices)
            config.Devices.Add(new DeviceConfig { Id = id, Name = name });

        config.Normalize();

        return config;
    }
}

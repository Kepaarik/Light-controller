using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LightSpace.App;
using LightSpace.App.ViewModels;
using LightSpace.App.Views;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.Tests;

/// <summary>
/// Выключатель отдельного устройства: гасит только его, переживает общий выключатель и перезапуск, не трогает профиль.
/// </summary>
public sealed class DevicePowerTests
{
    [Fact]
    public void DeviceIsOnByDefaultAndWhenSettingIsMissing()
    {
        var config = ConfigStore.Parse("""
            {
              "Version": 4,
              "Devices": [ { "Id": "a" }, { "Id": "b", "IsOn": false } ]
            }
            """);

        Assert.True(new DeviceConfig().IsOn);
        Assert.True(config.Devices.Single(d => d.Id == "a").IsOn);
        Assert.False(config.Devices.Single(d => d.Id == "b").IsOn);
    }

    [Fact]
    public void SwitchedOffDeviceGoesDarkAndOthersKeepPlaying()
    {
        using var manager = Manager(out var a, out var b);

        manager.SetDeviceOn(a, false);

        Assert.False(a.Settings.IsOn);
        Assert.False(a.Output!.IsEnabled);
        Assert.True(b.Output!.IsEnabled);

        manager.SetDeviceOn(a, true);

        Assert.True(a.Settings.IsOn);
        Assert.True(a.Output.IsEnabled);
    }

    [Fact]
    public void SwitchingReportsChangeOnlyWhenStateChanges()
    {
        using var manager = Manager(out var a, out _);
        var changes = 0;

        manager.DevicesChanged += (_, _) => changes++;

        manager.SetDeviceOn(a, true);
        manager.SetDeviceOn(a, false);
        manager.SetDeviceOn(a, false);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void SwitchedOffDeviceStaysDarkWhenAllLightingComesBack()
    {
        using var manager = Manager(out var a, out var b);

        manager.SetDeviceOn(a, false);
        manager.SetLightingEnabled(false);

        Assert.False(b.Output!.IsEnabled);

        manager.SetLightingEnabled(true);

        Assert.False(a.Output!.IsEnabled);
        Assert.True(b.Output.IsEnabled);
    }

    [Fact]
    public void DeviceSwitchedOnWhileAllLightingIsOffStaysDark()
    {
        using var manager = Manager(out var a, out _);

        manager.SetDeviceOn(a, false);
        manager.SetLightingEnabled(false);
        manager.SetDeviceOn(a, true);

        Assert.True(a.Settings.IsOn);
        Assert.False(a.Output!.IsEnabled);

        manager.SetLightingEnabled(true);

        Assert.True(a.Output.IsEnabled);
    }

    [Fact]
    public void SwitchedOffDeviceIsShownOffInMenuAndList()
    {
        WithViewModel(vm =>
        {
            vm.SetDeviceOn("a", false);

            Assert.False(vm.DeviceNav.Single(d => d.Id == "a").Device.IsOn);
            Assert.True(vm.DeviceNav.Single(d => d.Id == "b").Device.IsOn);
            Assert.False(vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn);
        });
    }

    [Fact]
    public void SwitchInDeviceListTurnsDeviceOff()
    {
        WithViewModel(vm =>
        {
            var item = vm.Devices.Items.Single(i => i.Device.Id == "a");

            item.IsOn = false;

            Assert.False(item.Device.Settings.IsOn);
            Assert.False(item.Device.Output!.IsEnabled);
            Assert.False(vm.DeviceNav.Single(d => d.Id == "a").Device.IsOn);
        });
    }

    [Fact]
    public void SwitchingDeviceKeepsProfileMarkedPlaying()
    {
        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);
            vm.SetDeviceOn("a", false);

            Assert.True(vm.Profiles[0].IsActive);

            vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn = true;

            Assert.True(vm.Profiles[0].IsActive);
        });
    }

    [Fact]
    public void ProfileDoesNotSwitchDevicesOnOrOff()
    {
        WithViewModel(vm =>
        {
            vm.NewProfileCommand.Execute(null);
            vm.SetDeviceOn("a", false);

            vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

            Assert.False(vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn);
        });
    }

    [Fact]
    public void SwitchingUnknownDeviceDoesNothing()
    {
        WithViewModel(vm =>
        {
            vm.SetDeviceOn("нет-такого", false);

            Assert.All(vm.Devices.Items, i => Assert.True(i.IsOn));
        });
    }

    [Fact]
    public void SwitchInDeviceHeaderTurnsDeviceOffAndBack()
    {
        WithViewModel(vm =>
        {
            var window = new MainWindow(vm);

            try
            {
                vm.SelectedDeviceId = "a";
                UiThread.ShowOffscreen(window);

                var page = UiThread.Descendants<DevicePage>(window).Single();

                Assert.True(PowerSwitch(page).IsChecked);

                Toggle(PowerSwitch(page));
                window.UpdateLayout();

                Assert.False(vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn);
                Assert.True(vm.Devices.Items.Single(i => i.Device.Id == "b").IsOn);
                Assert.False(PowerSwitch(page).IsChecked);
                Assert.Equal(AppPage.Device, vm.SelectedPage);

                Toggle(PowerSwitch(page));

                Assert.True(vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DeviceListHasSwitchForEveryDevice()
    {
        WithViewModel(vm =>
        {
            var window = new MainWindow(vm);

            try
            {
                vm.SetDeviceOn("b", false);
                vm.SelectedPage = AppPage.Devices;
                UiThread.ShowOffscreen(window);

                var page = UiThread.Descendants<DevicesPage>(window).Single();
                var list = UiThread.Descendants<ListBox>(page).First();
                var switches = UiThread.Descendants<CheckBox>(list).ToList();

                Assert.Equal(new bool?[] { true, false }, switches.Select(s => s.IsChecked));

                switches[0].IsChecked = false;

                Assert.False(vm.Devices.Items.Single(i => i.Device.Id == "a").IsOn);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static CheckBox PowerSwitch(DevicePage page)
    {
        return (CheckBox)page.FindName("PowerSwitch")!;
    }

    /// <summary>
    /// Щелчок по переключателю, как мышью: сначала он переключается, потом приходит событие щелчка.
    /// </summary>
    private static void Toggle(CheckBox checkBox)
    {
        checkBox.IsChecked = checkBox.IsChecked != true;
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>
    /// Менеджер с двумя подключёнными устройствами «a» и «b»; подсветка включена.
    /// </summary>
    private static DeviceManager Manager(out ManagedDevice a, out ManagedDevice b)
    {
        var config = Config();
        var manager = new DeviceManager(config);

        a = manager.Devices.Single(d => d.Id == "a");
        b = manager.Devices.Single(d => d.Id == "b");

        a.Connect(new FakeLed("a"), isLightingEnabled: true);
        b.Connect(new FakeLed("b"), isLightingEnabled: true);

        return manager;
    }

    /// <summary>
    /// Модель окна с двумя подключёнными устройствами «a» и «b».
    /// </summary>
    private static void WithViewModel(Action<MainViewModel> test)
    {
        UiThread.Invoke(() =>
        {
            var vm = new MainViewModel(Config());

            try
            {
                foreach (var id in new[] { "a", "b" })
                    vm.Devices.Items.Single(i => i.Device.Id == id).Device.Connect(new FakeLed(id), isLightingEnabled: true);

                test(vm);
            }
            finally
            {
                vm.Shutdown();
            }
        });
    }

    private static AppConfig Config()
    {
        var config = new AppConfig();

        config.Devices.Add(new DeviceConfig { Id = "a", Name = "Лента a" });
        config.Devices.Add(new DeviceConfig { Id = "b", Name = "Лента b" });
        config.Normalize();

        return config;
    }
}

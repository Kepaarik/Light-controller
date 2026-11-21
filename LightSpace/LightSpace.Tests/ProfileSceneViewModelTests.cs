using LightSpace.App.ViewModels;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.Tests;

/// <summary>
/// Сцена в профиле: сборка, состав, места устройств, режим, направление, яркость и эффект; правки сцены — правки профиля.
/// </summary>
public sealed class ProfileSceneViewModelTests
{
    [Fact]
    public void ProfileWithoutSceneOffersToBuildIt()
    {
        var (scene, _, _) = SceneOf("a", "b");

        Assert.False(scene.HasScene);
        Assert.Empty(scene.Members);
        Assert.Empty(scene.Devices);
        Assert.False(scene.IsSynced);
        Assert.Null(scene.SummaryText);
    }

    [Fact]
    public void BuiltSceneTakesKnownDevicesInRow()
    {
        var (scene, profile, edits) = SceneOf("a", "b", "забытое");

        scene.BuildCommand.Execute(null);

        Assert.True(scene.HasScene);
        Assert.Single(edits);
        Assert.Equal("rainbow", profile.Scene!.Effect.EffectId);
        Assert.Equal(new[] { "a", "b" }, profile.Scene.Devices.Select(d => d.DeviceId));
        Assert.Equal(new[] { "a", "b" }, scene.Members.Select(m => m.DeviceId));
        Assert.All(scene.Members, m => Assert.True(m.IsIncluded));

        // Слева направо по порядку профиля, не налезая друг на друга
        Assert.Equal(0.25, profile.Scene.Devices[0].X);
        Assert.Equal(0.75, profile.Scene.Devices[1].X);
        Assert.True(profile.Scene.Devices[0].Size <= 0.5);
    }

    [Fact]
    public void MemberToggleChangesSceneComposition()
    {
        var (scene, profile, edits) = SceneOf("a", "b");

        scene.BuildCommand.Execute(null);
        scene.SelectedDevice = scene.Devices[0];

        scene.Members[0].IsIncluded = false;

        Assert.Equal("b", Assert.Single(profile.Scene!.Devices).DeviceId);
        Assert.Equal("b", Assert.Single(scene.Devices).DeviceId);
        Assert.Null(scene.SelectedDevice);

        scene.Members[0].IsIncluded = true;

        Assert.Equal(new[] { "b", "a" }, profile.Scene.Devices.Select(d => d.DeviceId));
        Assert.Equal(2, scene.Devices.Count);
        Assert.Equal(3, edits.Count);
    }

    [Fact]
    public void SpreadDirectionAndBrightnessGoToProfile()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);

        scene.IsSynced = true;
        scene.SelectedDirection = scene.Directions.Single(d => d.Value == SceneDirection.BottomToTop);
        scene.Brightness = 35.4;

        Assert.Equal(SceneSpread.Synced, profile.Scene!.Spread);
        Assert.False(scene.IsSpaceSpread);
        Assert.Equal(SceneDirection.BottomToTop, profile.Scene.Direction);
        Assert.Equal(35, profile.Scene.Brightness);
        Assert.Equal(4, edits.Count);

        // Тот же выбор ещё раз — не правка
        scene.Brightness = 35;
        scene.IsSynced = true;

        Assert.Equal(4, edits.Count);
    }

    [Fact]
    public void EffectChosenInGalleryPlaysInScene()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        scene.Editor.SelectedTile = scene.Editor.Tiles.Single(t => t.Definition.Id == "fire");

        Assert.Equal("fire", profile.Scene!.Effect.EffectId);
        Assert.Equal(ContentKind.Effect, profile.Scene.Effect.Kind);
        Assert.True(scene.Editor.IsPlaying);
        Assert.Contains("Огонь", scene.SummaryText);
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public void SelectedDeviceIsRotatedAndResized()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        scene.SelectedDevice = scene.Devices[0];

        Assert.True(scene.Devices[0].IsSelected);

        scene.RotateSelectedCommand.Execute(null);
        scene.Devices[0].SizePercent = 30;

        Assert.Equal(90, profile.Scene!.Devices[0].Angle);
        Assert.Equal(0.3, profile.Scene.Devices[0].Size, 9);
        Assert.Equal(3, edits.Count);

        // Размер не выходит за пределы холста
        scene.Devices[0].SizePercent = 500;

        Assert.Equal(SceneDeviceConfig.MaxSize, profile.Scene.Devices[0].Size);
    }

    [Fact]
    public void SelectedDeviceLeavesScene()
    {
        var (scene, profile, edits) = SceneOf("a", "b");

        scene.BuildCommand.Execute(null);
        scene.SelectedDevice = scene.Devices[0];

        scene.RemoveSelectedCommand.Execute(null);

        Assert.Equal("b", Assert.Single(profile.Scene!.Devices).DeviceId);
        Assert.Equal("b", Assert.Single(scene.Devices).DeviceId);
        Assert.Null(scene.SelectedDevice);
        Assert.False(scene.Members.Single(m => m.DeviceId == "a").IsIncluded);
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public void ForgottenDeviceLeavesScene()
    {
        var (scene, profile, _) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        profile.Scene!.Devices.Add(new SceneDeviceConfig { DeviceId = "забытое" });
        scene.Reload();
        scene.SelectedDevice = scene.Devices.Single(d => d.DeviceId == "забытое");

        scene.RemoveSelectedCommand.Execute(null);

        Assert.Equal("a", Assert.Single(profile.Scene.Devices).DeviceId);
        Assert.Equal("a", Assert.Single(scene.Devices).DeviceId);
    }

    [Fact]
    public void SingleColorDeviceIsDrawnAsStripOfItsLength()
    {
        var (scene, profile, _) = SceneOf("a");

        scene.BuildCommand.Execute(null);

        var placement = profile.Scene!.Devices[0];

        placement.X = 0.5;
        placement.Y = 0.5;
        placement.Size = 0.4;

        var (start, end) = scene.Devices[0].CanvasLine;

        // Не подключённое устройство светится одним цветом, но на холсте у него есть длина
        Assert.Empty(scene.Devices[0].CanvasPoints.Points);
        Assert.Equal(0.3, start.X, 9);
        Assert.Equal(0.7, end.X, 9);
        Assert.Equal(0.5 * SceneConfig.CanvasHeight, start.Y, 9);
        Assert.Equal(DeviceIcons.Strip, scene.Devices[0].Icon);

        scene.Devices[0].Rotate();
        (start, end) = scene.Devices[0].CanvasLine;

        Assert.Equal(0.5, start.X, 9);
        Assert.Equal(0.5 * SceneConfig.CanvasHeight - 0.2, start.Y, 9);
        Assert.Equal(0.5 * SceneConfig.CanvasHeight + 0.2, end.Y, 9);
    }

    [Fact]
    public void SelectedDeviceGetsItsShapePanelsAndBrightness()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);

        var device = scene.Devices[0];

        // По умолчанию вид — по самому устройству: не подключённое светится одним цветом и показано лентой
        Assert.Equal(SceneDeviceShape.Strip, device.SelectedShape!.Value);
        Assert.False(device.IsHexagons);

        device.SelectedShape = device.Shapes.Single(s => s.Value == SceneDeviceShape.Hexagons);
        device.Panels = 12.4;
        device.Brightness = 55.6;

        var placement = profile.Scene!.Devices[0];

        Assert.Equal(SceneDeviceShape.Hexagons, placement.Shape);
        Assert.True(device.IsHexagons);
        Assert.Equal(12, placement.Panels);
        Assert.Equal(56, placement.Brightness);
        Assert.Equal(12, device.CanvasShape.Panels.Count);
        Assert.Equal(4, edits.Count);

        // Тот же выбор ещё раз — не правка
        device.SelectedShape = device.Shapes.Single(s => s.Value == SceneDeviceShape.Hexagons);
        device.Panels = 12;

        Assert.Equal(4, edits.Count);
    }

    [Fact]
    public void DeviceInSceneGetsItsOwnEffect()
    {
        var (scene, profile, edits) = SceneOf("a", "b");

        scene.BuildCommand.Execute(null);

        var device = scene.Devices[0];

        Assert.False(device.HasOwnEffect);

        device.HasOwnEffect = true;
        device.Content.Editor.SelectedTile = device.Content.Editor.Tiles.Single(t => t.Definition.Id == "fire");

        var own = profile.Scene!.Devices[0].Effect!;

        Assert.Equal(ContentKind.Effect, own.Kind);
        Assert.Equal("fire", own.EffectId);
        Assert.True(device.Content.Editor.IsPlaying);
        Assert.Null(profile.Scene.Devices[1].Effect);
        Assert.Equal(3, edits.Count);

        // Без своего эффекта устройство снова играет эффект сцены
        device.HasOwnEffect = false;

        Assert.Null(profile.Scene.Devices[0].Effect);
        Assert.Equal(4, edits.Count);
    }

    [Fact]
    public void PickingEffectTurnsOnOwnEffect()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);

        var device = scene.Devices[0];

        // Пока играет эффект сцены, в галерее ничего не выделено
        Assert.False(device.Content.Editor.IsPlaying);

        device.Content.Editor.SelectedTile = device.Content.Editor.Tiles.Single(t => t.Definition.Id == "fire");

        Assert.True(device.HasOwnEffect);
        Assert.True(device.Content.Editor.IsPlaying);
        Assert.Equal("fire", profile.Scene!.Devices[0].Effect!.EffectId);
        Assert.Equal(2, edits.Count);

        // Выключенный и снова включённый свой эффект — тот же
        device.HasOwnEffect = false;

        Assert.False(device.Content.Editor.IsPlaying);

        device.HasOwnEffect = true;

        Assert.Equal("fire", profile.Scene.Devices[0].Effect!.EffectId);
    }

    [Fact]
    public void WholeSceneGetsSceneEffectBack()
    {
        var (scene, profile, _) = SceneOf("a", "b");
        var picked = new List<string?>();

        scene.BuildCommand.Execute(null);
        scene.DevicePicked += picked.Add;

        Assert.False(scene.HasOwnEffects);

        scene.Devices[0].HasOwnEffect = true;
        scene.Devices[1].Content.Editor.SelectedTile = scene.Devices[1].Content.Editor.Tiles.Single(t => t.Definition.Id == "fire");

        Assert.True(scene.HasOwnEffects);

        // «Эффект сцены» снимает выбор устройства
        scene.SelectedDevice = scene.Devices[0];
        scene.ShowEffectCommand.Execute(null);

        Assert.Null(scene.SelectedDevice);
        Assert.Equal(new string?[] { null }, picked);

        // «Всем устройствам» — вся сцена играет эффект сцены, свои эффекты не забываются
        scene.PlaySceneEffectEverywhereCommand.Execute(null);

        Assert.False(scene.HasOwnEffects);
        Assert.All(profile.Scene!.Devices, d => Assert.Null(d.Effect));

        scene.Devices[1].HasOwnEffect = true;

        Assert.Equal("fire", profile.Scene.Devices[1].Effect!.EffectId);
    }

    [Fact]
    public void DeviceWithBuiltinEffectIsGrayInPreview()
    {
        var (scene, profile, _) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        profile.Scene!.Devices[0].Effect = new ContentConfig { Kind = ContentKind.Builtin };
        scene.Reload();
        scene.Advance(0.5);

        Assert.Equal(new LightSpace.Core.RgbColor(0x60, 0x64, 0x70), Assert.Single(scene.Devices[0].Colors));
        Assert.True(scene.Devices[0].HasOwnEffect);
        Assert.True(scene.Devices[0].Content.IsBuiltinPlaying);
    }

    [Fact]
    public void DeviceMovesInsideCanvas()
    {
        var (scene, profile, _) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        scene.Devices[0].MoveTo(-0.5, 0.25);

        Assert.Equal(0, profile.Scene!.Devices[0].X);
        Assert.Equal(0.25, profile.Scene.Devices[0].Y);
    }

    [Fact]
    public void PreviewPaintsEveryDevice()
    {
        var (scene, _, _) = SceneOf("a", "b");
        var rendered = 0;

        scene.BuildCommand.Execute(null);
        scene.Rendered += () => rendered++;

        scene.Advance(0.5);

        Assert.Equal(1, rendered);
        Assert.All(scene.Devices, d => Assert.Single(d.Colors));
    }

    [Fact]
    public void RemovedSceneLeavesProfile()
    {
        var (scene, profile, edits) = SceneOf("a");

        scene.BuildCommand.Execute(null);
        scene.RemoveCommand.Execute(null);

        Assert.Null(profile.Scene);
        Assert.False(scene.HasScene);
        Assert.Empty(scene.Devices);
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public void DeviceRemovedFromProfileLeavesItsScene()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Вечер");
        var devices = config.Devices.Select(d => new ManagedDevice(d)).ToList();
        var option = new ProfileOption(profile, id => devices.FirstOrDefault(d => d.Id == id), () => { });

        option.OpenSceneCommand.Execute(null);

        Assert.True(option.IsSceneOpen);

        option.Devices[0].RemoveCommand.Execute(null);

        Assert.Equal("b", Assert.Single(profile.Scene!.Devices).DeviceId);
        Assert.Equal("b", Assert.Single(option.Scene.Members).DeviceId);
        Assert.Contains("Сцена: Радуга", option.SummaryText);

        option.RemoveSceneCommand.Execute(null);

        Assert.False(option.IsSceneOpen);
        Assert.Null(profile.Scene);
    }

    [Fact]
    public void CanvasPickAndDeviceSelectionStayInStep()
    {
        var config = ConfigWithDevices("a", "b");
        var profile = config.SaveProfile("Вечер");
        var devices = config.Devices.Select(d => new ManagedDevice(d)).ToList();
        var option = new ProfileOption(profile, id => devices.FirstOrDefault(d => d.Id == id), () => { });

        Assert.False(option.HasEditedDevice);

        // Без сцены устройство открывается со своим эффектом в профиле
        option.Devices[0].EditCommand.Execute(null);

        Assert.Same(option.Devices[0], option.EditedDevice);
        Assert.False(option.IsEditedInScene);
        Assert.False(option.HasEditedMember);

        option.OpenSceneCommand.Execute(null);

        // Сцена собрана: открытое устройство выделено на холсте
        Assert.True(option.IsSceneOpen);
        Assert.True(option.HasEditedMember);
        Assert.True(option.IsEditedInScene);
        Assert.Same(option.EditedSceneDevice, option.Scene.SelectedDevice);

        // Щелчок по другому устройству на холсте открывает его, щелчок мимо — эффект сцены
        option.Scene.Pick(option.Scene.Devices.First(d => d.DeviceId == "b"));

        Assert.Same(option.Devices[1], option.EditedDevice);

        option.Scene.Pick(null);

        Assert.False(option.HasEditedDevice);
        Assert.Null(option.Scene.SelectedDevice);

        // Устройство, выведенное из сцены, остаётся открытым
        option.EditedDevice = option.Devices[0];
        option.EditedMember!.IsIncluded = false;

        Assert.Same(option.Devices[0], option.EditedDevice);
        Assert.False(option.IsEditedInScene);
        Assert.Null(option.Scene.SelectedDevice);

        option.EditedMember!.IsIncluded = true;

        Assert.Same(option.EditedSceneDevice, option.Scene.SelectedDevice);

        option.CloseDeviceCommand.Execute(null);

        Assert.False(option.HasEditedDevice);
        Assert.Null(option.Scene.SelectedDevice);
    }

    [Fact]
    public void SceneEditMarksProfileAsNotPlaying()
    {
        UiThread.Invoke(() =>
        {
            var config = ConfigWithDevices("a");

            config.SaveProfile("Вечер");

            var vm = new MainViewModel(config);

            try
            {
                vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

                Assert.True(vm.Profiles[0].IsActive);

                vm.Profiles[0].OpenSceneCommand.Execute(null);

                Assert.False(vm.Profiles[0].IsActive);
            }
            finally
            {
                vm.Shutdown();
            }
        });
    }

    [Fact]
    public void DeviceOfAppliedSceneShowsBannerAndLeavesScene()
    {
        UiThread.Invoke(() =>
        {
            var config = ConfigWithDevices("a", "b");

            config.SaveProfile("Вечер");

            var vm = new MainViewModel(config);

            try
            {
                vm.Profiles[0].OpenSceneCommand.Execute(null);
                vm.ApplyProfileCommand.Execute(vm.Profiles[0]);
                vm.SelectedDeviceId = "a";

                Assert.True(vm.Lighting.IsInScene);

                vm.Lighting.LeaveSceneCommand.Execute(null);

                // Сцена играет дальше без устройства; в профиле она прежняя
                Assert.False(vm.Lighting.IsInScene);
                Assert.Equal("b", Assert.Single(config.Scene!.Devices).DeviceId);
                Assert.Equal(2, config.Profiles[0].Scene!.Devices.Count);
                Assert.False(vm.Profiles[0].IsActive);
            }
            finally
            {
                vm.Shutdown();
            }
        });
    }

    private static (ProfileSceneViewModel Scene, ProfileConfig Profile, List<int> Edits) SceneOf(params string[] ids)
    {
        var known = ids.Where(id => id != "забытое").Select(id => new ManagedDevice(new DeviceConfig { Id = id, Name = id })).ToList();
        var profile = new ProfileConfig
        {
            Id = "p",
            Name = "Вечер",
            DeviceLighting = ids.Select(id => DeviceLightingConfig.For(id, new ContentConfig(), 100)).ToList()
        };
        var edits = new List<int>();
        var scene = new ProfileSceneViewModel(profile, id => known.FirstOrDefault(d => d.Id == id), () => edits.Add(edits.Count));

        return (scene, profile, edits);
    }

    private static AppConfig ConfigWithDevices(params string[] ids)
    {
        var config = new AppConfig();

        foreach (var id in ids)
            config.Devices.Add(new DeviceConfig { Id = id, Name = id });

        config.Normalize();

        return config;
    }
}

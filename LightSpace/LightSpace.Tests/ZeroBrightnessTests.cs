using System.Collections.Concurrent;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine;
using LightSpace.Engine.Frames;
using LightSpace.Engine.Sources;

namespace LightSpace.Tests;

/// <summary>
/// Нулевая яркость гасит устройство целиком — и эффект приложения, и встроенный эффект, который свою яркость может не уметь.
/// </summary>
public sealed class ZeroBrightnessTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void ZeroBrightnessSendsBlackInsteadOfEffect()
    {
        var config = Config(new ContentConfig { Kind = ContentKind.Effect, EffectId = "solid" }, brightness: 0);
        var driver = new RecordingBuiltinLed("a");

        using var manager = new DeviceManager(config);

        manager.Devices.Single(d => d.Id == "a").Connect(driver, isLightingEnabled: true);

        using var sources = new SourceHub();
        using var loop = new FrameLoop(FrameSnapshot.Build(config, manager.Devices), sources);

        WaitUntil(() => driver.Commands.Contains("color 000000"));

        Assert.DoesNotContain(driver.Commands, c => c.StartsWith("color", StringComparison.Ordinal) && c != "color 000000");
    }

    [Fact]
    public void ZeroBrightnessStopsBuiltinEffectAndItComesBackAfterReset()
    {
        var config = Config(BuiltinContent(), brightness: 0);
        var driver = new RecordingBuiltinLed("a");

        using var manager = new DeviceManager(config);

        manager.Devices.Single(d => d.Id == "a").Connect(driver, isLightingEnabled: true);

        using var sources = new SourceHub();
        using var loop = new FrameLoop(FrameSnapshot.Build(config, manager.Devices), sources);

        WaitUntil(() => driver.Commands.Contains("color 000000"));

        Assert.DoesNotContain(driver.Commands, c => c.StartsWith("builtin", StringComparison.Ordinal));

        // Яркость вернули — встроенный эффект запускается снова, после сброса
        config.DeviceLighting.Single().Brightness = 40;
        loop.Update(FrameSnapshot.Build(config, manager.Devices));

        WaitUntil(() => driver.Commands.Contains("builtin 7 40"));

        var commands = driver.Commands.ToList();
        var start = commands.IndexOf("builtin 7 40");

        Assert.Equal("reset", commands[start - 1]);
    }

    [Fact]
    public void ZeroMasterBrightnessAlsoGoesDark()
    {
        var config = Config(BuiltinContent(), brightness: 80);
        var driver = new RecordingBuiltinLed("a");

        config.MasterBrightness = 0;

        using var manager = new DeviceManager(config);

        manager.Devices.Single(d => d.Id == "a").Connect(driver, isLightingEnabled: true);

        using var sources = new SourceHub();
        using var loop = new FrameLoop(FrameSnapshot.Build(config, manager.Devices), sources);

        WaitUntil(() => driver.Commands.Contains("color 000000"));

        Assert.DoesNotContain(driver.Commands, c => c.StartsWith("builtin", StringComparison.Ordinal));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Устройство так и не получило ожидаемую команду");
            Thread.Sleep(10);
        }
    }

    private static AppConfig Config(ContentConfig content, int brightness)
    {
        var config = new AppConfig();

        config.Devices.Add(new DeviceConfig { Id = "a", Name = "Лента a" });
        config.Normalize();

        var lighting = config.EnsureDeviceLighting("a");

        lighting.Content = content;
        lighting.Brightness = brightness;

        return config;
    }

    private static ContentConfig BuiltinContent()
    {
        var content = new ContentConfig { Kind = ContentKind.Builtin };

        content.Builtin.Effects["a"] = 7;

        return content;
    }

    /// <summary>
    /// Контроллер со встроенными эффектами, который записывает каждую полученную команду.
    /// </summary>
    private sealed class RecordingBuiltinLed : LedDevice, IBuiltinEffectsDevice
    {
        public RecordingBuiltinLed(string id)
        {
            Id = id;
        }

        public ConcurrentQueue<string> Commands { get; } = new();

        public override string Id { get; }

        public override string Name => "Контроллер";

        public override string Protocol => "Тест";

        public override string Address => "test";

        public IReadOnlyList<BuiltinEffect> BuiltinEffects { get; } = new[] { new BuiltinEffect(7, "Пульс") };

        public bool HasBuiltinColor => false;

        public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
        {
            Commands.Enqueue(isOn ? "on" : "off");
            return Task.CompletedTask;
        }

        public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
        {
            Commands.Enqueue($"color {color.R:X2}{color.G:X2}{color.B:X2}");
            return Task.CompletedTask;
        }

        public override Task ResetAsync(CancellationToken ct = default)
        {
            Commands.Enqueue("reset");
            return Task.CompletedTask;
        }

        public Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default)
        {
            Commands.Enqueue($"builtin {effectId} {brightness}");
            return Task.CompletedTask;
        }
    }
}

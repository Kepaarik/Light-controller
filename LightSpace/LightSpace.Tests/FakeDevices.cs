using LightSpace.Core;
using LightSpace.Devices;

namespace LightSpace.Tests;

/// <summary>
/// Устройство одного цвета для тестов: принимает любые команды и ничего не отправляет.
/// </summary>
public class FakeLed : LedDevice
{
    public FakeLed(string id, string name = "Лента")
    {
        Id = id;
        Name = name;
    }

    public override string Id { get; }

    public override string Name { get; }

    public override string Protocol => "Тест";

    public override string Address => "test";

    public override Task SetPowerAsync(bool isOn, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public override Task SetColorAsync(RgbColor color, int brightness, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Клавиатура для тестов: две клавиши с подсветкой.
/// </summary>
public sealed class FakeKeyboard : FakeLed, IKeyboardDevice
{
    public FakeKeyboard(string id) : base(id, "Клавиатура")
    {
    }

    public int LedCount => Keys.Count;

    public IReadOnlyList<KeyboardKey> Keys { get; } = new[] { new KeyboardKey(0, "Q", 0, 0), new KeyboardKey(1, "W", 1, 0) };

    public Task SetLedColorsAsync(IReadOnlyList<RgbColor> colors, int brightness, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Контроллер со встроенными эффектами для тестов.
/// </summary>
public sealed class FakeBuiltinLed : FakeLed, IBuiltinEffectsDevice
{
    public FakeBuiltinLed(string id) : base(id, "Контроллер")
    {
    }

    public IReadOnlyList<BuiltinEffect> BuiltinEffects { get; } = new[] { new BuiltinEffect(3, "Радуга"), new BuiltinEffect(7, "Пульс") };

    public bool HasBuiltinColor => false;

    public Task StartBuiltinEffectAsync(int effectId, int speed, int brightness, RgbColor? color, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

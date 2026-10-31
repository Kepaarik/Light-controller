using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.App.Controls;
using LightSpace.Core;
using LightSpace.Devices;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство на сцене: название и живые цвета, которые сейчас уходят на него. Лента показывается полосой
/// своих светодиодов, клавиатура — раскладкой с закрашенными клавишами, устройство одного цвета — одной заливкой.
/// </summary>
public sealed partial class SceneDeviceViewModel : ObservableObject
{
    private readonly ColorBitmap _bitmap;

    [ObservableProperty]
    private IReadOnlyList<RgbColor>? _keyColors;

    // Устройство выделено на сцене: кликом, Ctrl+кликом или рамкой
    [ObservableProperty]
    private bool _isSelected;

    public SceneDeviceViewModel(ManagedDevice device)
    {
        var capabilities = device.Capabilities;

        Id = device.Id;
        Name = device.Name;
        Keys = capabilities?.Keys ?? Array.Empty<KeyboardKey>();

        KindText = capabilities switch
        {
            null => "Не подключено",
            { IsKeyboard: true } => "Клавиатура",
            { IsAddressable: true } => $"Лента, {capabilities.LedCount} светодиодов",
            _ => "Один цвет на всё устройство"
        };

        // Полоса ленты — по точке на светодиод; у устройства одного цвета — одна точка на всю полосу
        _bitmap = new ColorBitmap(capabilities is { IsAddressable: true, IsKeyboard: false } ? capabilities.LedCount : 1, 1);
        _bitmap.Write(Array.Empty<RgbColor>());
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Что это за устройство, или что оно не подключено.
    /// </summary>
    public string KindText { get; }

    public IReadOnlyList<KeyboardKey> Keys { get; }

    public bool IsKeyboard => Keys.Count > 0;

    /// <summary>
    /// Полоса живых цветов ленты или устройства одного цвета.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    /// <summary>
    /// Показывает цвета, ушедшие на устройство, с их яркостью; null — устройство сейчас не светится.
    /// </summary>
    public void Show(IReadOnlyList<RgbColor>? colors, int brightness)
    {
        if (IsKeyboard)
        {
            KeyColors = colors?.Select(c => c.Scale(brightness)).ToArray();
            return;
        }

        _bitmap.Write(colors ?? Array.Empty<RgbColor>(), brightness);
    }
}

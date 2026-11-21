using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство на холсте сцены: его место, поворот, размер и цвета превью.
/// </summary>
public sealed partial class SceneDeviceViewModel : ObservableObject
{
    private readonly Action _resized;

    /// <summary>
    /// own — собственное пространство устройства: по нему расставляются светодиоды на холсте.
    /// resized вызывается, когда устройству поменяли размер.
    /// </summary>
    public SceneDeviceViewModel(SceneDeviceConfig placement, string name, DeviceGeometry own, Action resized)
    {
        Placement = placement;
        Name = name;
        Own = own;
        _resized = resized;
        Colors = new RgbColor[Math.Max(own.Points.Count, 1)];
    }

    /// <summary>
    /// Место устройства в настройках сцены; правки места сразу попадают в него.
    /// </summary>
    public SceneDeviceConfig Placement { get; }

    public string DeviceId => Placement.DeviceId;

    public string Name { get; }

    /// <summary>
    /// Собственное пространство устройства.
    /// </summary>
    public DeviceGeometry Own { get; }

    /// <summary>
    /// Устройство выбрано на холсте: его можно повернуть и поменять размер.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Цвета превью по номерам светодиодов; у устройства одного цвета — один.
    /// </summary>
    public RgbColor[] Colors { get; set; }

    /// <summary>
    /// Места светодиодов на холсте: X от 0 до 1, Y от 0 до высоты холста. У устройства одного цвета — только середина.
    /// </summary>
    public (IReadOnlyList<EffectPixel?> Points, EffectPixel Center) CanvasPoints => SceneSpace.Place(Own, Placement);

    /// <summary>
    /// Ставит середину устройства в точку x, y холста в долях его ширины и высоты; середина не уходит за край.
    /// </summary>
    public void MoveTo(double x, double y)
    {
        Placement.X = Math.Clamp(x, 0, 1);
        Placement.Y = Math.Clamp(y, 0, 1);
    }

    /// <summary>
    /// Длина ленты или ширина клавиатуры в процентах ширины холста.
    /// </summary>
    public double SizePercent
    {
        get => Placement.Size * 100;
        set
        {
            var size = Math.Clamp(value / 100, SceneDeviceConfig.MinSize, SceneDeviceConfig.MaxSize);

            if (size == Placement.Size)
                return;

            Placement.Size = size;
            OnPropertyChanged();
            _resized();
        }
    }

    /// <summary>
    /// Поворачивает устройство на четверть оборота по часовой стрелке.
    /// </summary>
    public void Rotate()
    {
        Placement.Angle = (Placement.Angle + 90) % 360;
    }
}

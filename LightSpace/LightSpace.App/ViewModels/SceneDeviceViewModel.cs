using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Scene;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство на холсте сцены: его место, поворот, размер, вид, число шестигранников, своя яркость и цвета превью.
/// </summary>
public sealed partial class SceneDeviceViewModel : ObservableObject
{
    private readonly Action _changed;

    // Свой эффект устройства; пока устройство играет эффект сцены — тот, что включится при выборе в галерее
    private readonly ContentConfig _ownEffect;

    /// <summary>
    /// icon — значок устройства, как в левом меню; own — собственное пространство устройства: по нему расставляются
    /// светодиоды на холсте; monitor — монитор, за которым висит лента; findDevice находит устройство для его
    /// встроенных эффектов. changed вызывается, когда устройству поменяли размер, вид, число шестигранников, яркость
    /// или свой эффект.
    /// </summary>
    public SceneDeviceViewModel(SceneDeviceConfig placement, string name, string icon, DeviceGeometry own, MonitorConfig? monitor,
        Func<string, ManagedDevice?> findDevice, Action changed)
    {
        Placement = placement;
        Name = name;
        Icon = icon;
        Own = own;
        Monitor = monitor;
        _changed = changed;
        Colors = new RgbColor[Math.Max(own.Points.Count, 1)];

        _ownEffect = placement.Effect ?? new ContentConfig { Kind = ContentKind.Effect };

        Content = new DeviceContentViewModel(findDevice, OnContentChanged);
        Content.Edit(_ownEffect, placement.DeviceId, placement.Effect != null);
    }

    /// <summary>
    /// Свой эффект устройства в сцене: галерея и встроенные эффекты устройства. Выбор в них включает свой эффект.
    /// </summary>
    public DeviceContentViewModel Content { get; }

    /// <summary>
    /// Устройство играет свой эффект, а не эффект сцены. Выключенный и снова включённый свой эффект — тот же, что был.
    /// </summary>
    public bool HasOwnEffect
    {
        get => Placement.Effect != null;
        set
        {
            if (value == HasOwnEffect)
                return;

            Placement.Effect = value ? _ownEffect : null;
            Content.Edit(_ownEffect, Placement.DeviceId, value);

            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// В галерее или во встроенных эффектах что-то выбрали или поменяли: устройство играет свой эффект.
    /// </summary>
    private void OnContentChanged()
    {
        if (Placement.Effect == null)
        {
            Placement.Effect = _ownEffect;
            OnPropertyChanged(nameof(HasOwnEffect));
        }

        _changed();
    }

    /// <summary>
    /// Виды устройства на холсте.
    /// </summary>
    public IReadOnlyList<SceneChoice<SceneDeviceShape>> Shapes { get; } = new[]
    {
        new SceneChoice<SceneDeviceShape>(SceneDeviceShape.Strip, "Лента"),
        new SceneChoice<SceneDeviceShape>(SceneDeviceShape.Keyboard, "Клавиатура"),
        new SceneChoice<SceneDeviceShape>(SceneDeviceShape.Monitor, "Монитор"),
        new SceneChoice<SceneDeviceShape>(SceneDeviceShape.Hexagons, "Шестигранники")
    };

    /// <summary>
    /// Место устройства в настройках сцены; правки места сразу попадают в него.
    /// </summary>
    public SceneDeviceConfig Placement { get; }

    public string DeviceId => Placement.DeviceId;

    public string Name { get; }

    /// <summary>
    /// Значок устройства (символ Segoe Fluent Icons): выбранный пользователем или по типу устройства.
    /// </summary>
    public string Icon { get; }

    /// <summary>
    /// Собственное пространство устройства.
    /// </summary>
    public DeviceGeometry Own { get; }

    /// <summary>
    /// Монитор, за которым висит лента: вид «Монитор» берёт её раскладку по краям; null — ленты за монитором нет.
    /// </summary>
    public MonitorConfig? Monitor { get; }

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
    /// Места светодиодов на холсте в виде устройства: X от 0 до 1, Y от 0 до высоты холста. У устройства одного цвета —
    /// только середина.
    /// </summary>
    public (IReadOnlyList<EffectPixel?> Points, EffectPixel Center) CanvasPoints =>
        SceneSpace.Place(SceneShapes.Of(Own, Placement, Monitor).Local, Placement);

    /// <summary>
    /// Устройство в своём виде на холсте: светодиоды, углы корпуса и шестигранников в точках холста.
    /// </summary>
    public SceneShape CanvasShape => SceneSpace.Place(SceneShapes.Of(Own, Placement, Monitor), Placement);

    /// <summary>
    /// Как устройство выглядит на холсте; «по самому устройству» показывается тем видом, который выбран за него.
    /// </summary>
    public SceneChoice<SceneDeviceShape>? SelectedShape
    {
        get => Shapes.First(s => s.Value == SceneShapes.KindOf(Own, Placement));
        set
        {
            // Сегменты сбрасывают выбор в null, когда пересобираются
            if (value == null || value.Value == SceneShapes.KindOf(Own, Placement))
                return;

            Placement.Shape = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHexagons));
            _changed();
        }
    }

    /// <summary>
    /// Устройство — шестигранники: видно, сколько их.
    /// </summary>
    public bool IsHexagons => SceneShapes.KindOf(Own, Placement) == SceneDeviceShape.Hexagons;

    /// <summary>
    /// Сколько шестигранников у устройства.
    /// </summary>
    public double Panels
    {
        get => Placement.Panels;
        set
        {
            var panels = (int)Math.Round(Math.Clamp(value, SceneDeviceConfig.MinPanels, SceneDeviceConfig.MaxPanels));

            if (panels == Placement.Panels)
                return;

            Placement.Panels = panels;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Яркость устройства в сцене, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => Placement.Brightness;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == Placement.Brightness)
                return;

            Placement.Brightness = brightness;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Начало и конец ленты на холсте — и у адресной, и у ленты одного цвета: линия длиной в размер устройства через
    /// его середину, повёрнутая вместе с ним.
    /// </summary>
    public (Point Start, Point End) CanvasLine
    {
        get
        {
            var radians = Placement.Angle * Math.PI / 180;
            var half = new Vector(Math.Cos(radians), Math.Sin(radians)) * (Placement.Size / 2);
            var center = new Point(Placement.X, Placement.Y * SceneConfig.CanvasHeight);

            return (center - half, center + half);
        }
    }

    /// <summary>
    /// Ставит середину устройства в точку x, y холста в долях его ширины и высоты; середина не уходит за край.
    /// </summary>
    public void MoveTo(double x, double y)
    {
        Placement.X = Math.Clamp(x, 0, 1);
        Placement.Y = Math.Clamp(y, 0, 1);
    }

    /// <summary>
    /// Длина ленты или ширина клавиатуры, монитора, сот шестигранников в процентах ширины холста.
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
            _changed();
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

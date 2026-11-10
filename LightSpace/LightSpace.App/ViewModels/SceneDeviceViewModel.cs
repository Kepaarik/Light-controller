using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.App.Controls;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Devices;
using LightSpace.Engine;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Устройство на сцене: название и живые цвета, которые сейчас уходят на него. Лента показывается полосой
/// своих светодиодов, клавиатура — раскладкой с закрашенными клавишами, устройство одного цвета — одной заливкой.
/// </summary>
public sealed partial class SceneDeviceViewModel : ObservableObject
{
    // Где стоит устройство, которое ещё не расставили: в середине сцены
    private static readonly DevicePlacement Unplaced = new();

    private readonly ColorBitmap _bitmap;
    private readonly ManagedDevice _device;
    private readonly DeviceConfig _settings;

    // Состояние связи, которое сейчас показано: меняется само, без пересборки устройств
    private DeviceStatus _status;

    [ObservableProperty]
    private IReadOnlyList<RgbColor>? _keyColors;

    // Устройство выделено на сцене: кликом, Ctrl+кликом или рамкой
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Устройство само играет встроенный эффект: его цвета окну неизвестны, и на сцене вместо них виден знак.
    /// </summary>
    [ObservableProperty]
    private bool _isBuiltinPlaying;

    public SceneDeviceViewModel(ManagedDevice device)
    {
        var capabilities = device.Capabilities;

        Id = device.Id;
        Name = device.Name;
        IsConnected = device.Device != null;
        Keys = capabilities?.Keys ?? Array.Empty<KeyboardKey>();
        Geometry = device.Geometry;
        _device = device;
        _settings = device.Settings;
        _status = device.Status;

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
    /// Устройство подключено; неподключённому настраивать нечего — команды до него не дойдут.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Подсветка устройства включена; выключенное не светится, и живых цветов у него нет.
    /// </summary>
    public bool IsOn => _settings.IsOn;

    /// <summary>
    /// Что это за устройство, или что оно не подключено.
    /// </summary>
    public string KindText { get; }

    /// <summary>
    /// Значок устройства в левом меню: выбранный пользователем или по типу устройства.
    /// </summary>
    public string Icon => _settings.Icon ?? (IsKeyboard ? DeviceIcons.Keyboard : DeviceIcons.Strip);

    /// <summary>
    /// Состояние связи с устройством.
    /// </summary>
    public DeviceStatus Status => _status;

    public string StatusTitle => _status switch
    {
        DeviceStatus.Connected => "Подключено",
        DeviceStatus.NeedsSetup => "Нужна настройка",
        DeviceStatus.Failing => "Нет связи",
        _ => "Не найдено"
    };

    public IReadOnlyList<KeyboardKey> Keys { get; }

    public bool IsKeyboard => Keys.Count > 0;

    /// <summary>
    /// Пространство устройства: где находится каждый его светодиод.
    /// </summary>
    public DeviceGeometry Geometry { get; }

    /// <summary>
    /// Устройство одного цвета: на сцене это кружок.
    /// </summary>
    public bool IsPoint => !Geometry.IsAddressable;

    /// <summary>
    /// Полоса живых цветов ленты или устройства одного цвета.
    /// </summary>
    public Brush Preview => _bitmap.Brush;

    /// <summary>
    /// Место устройства на сцене без поворота, в единицах сцены.
    /// </summary>
    public Rect Place
    {
        get
        {
            var placement = Placement;
            var (width, height) = SceneProjection.ExtentOf(Geometry, placement);

            return new Rect(placement.X - width / 2, placement.Y - height / 2, width, height);
        }
    }

    /// <summary>
    /// Поворот устройства на сцене в градусах по часовой стрелке.
    /// </summary>
    public double Angle => Placement.Rotation;

    /// <summary>
    /// Прямоугольник на сцене, в который целиком входит повёрнутое устройство.
    /// </summary>
    public SceneRect Bounds => SceneProjection.BoundsOf(Geometry, Placement);

    /// <summary>
    /// Места светодиодов устройства на сцене.
    /// </summary>
    public IReadOnlyList<EffectPixel?> ScenePoints => SceneProjection.Project(Geometry, Placement);

    /// <summary>
    /// Длина ленты, ширина клавиатуры или поперечник устройства одного цвета на сцене.
    /// </summary>
    public double SceneSize
    {
        get => SceneProjection.SizeOf(Geometry, Placement);
        set
        {
            EditablePlacement.Size = Math.Clamp(value, DevicePlacement.MinSize, DevicePlacement.MaxSize);
            OnPlacementChanged();
        }
    }

    // Место из настроек; у ещё не расставленного устройства — середина сцены
    private DevicePlacement Placement => _settings.Placement ?? Unplaced;

    // Место, которое можно менять: у ещё не расставленного устройства оно заводится в середине сцены
    private DevicePlacement EditablePlacement => _settings.Placement ??= new DevicePlacement();

    /// <summary>
    /// Сдвигает устройство на dx, dy единиц сцены; середина устройства не уходит за край сцены.
    /// </summary>
    public void MoveBy(double dx, double dy)
    {
        var placement = EditablePlacement;

        placement.X = Math.Clamp(placement.X + dx, 0, SceneArea.Width);
        placement.Y = Math.Clamp(placement.Y + dy, 0, SceneArea.Height);
        OnPlacementChanged();
    }

    /// <summary>
    /// Поворачивает устройство на четверть оборота по часовой стрелке.
    /// </summary>
    public void Rotate()
    {
        var placement = EditablePlacement;

        placement.Rotation = (placement.Rotation + 90) % 360;
        OnPlacementChanged();
    }

    /// <summary>
    /// Поворачивает устройство на угол degrees по часовой стрелке от исходного положения; угол приводится к 0–360.
    /// </summary>
    public void SetRotation(double degrees)
    {
        EditablePlacement.Rotation = (degrees % 360 + 360) % 360;
        OnPlacementChanged();
    }

    /// <summary>
    /// Место устройства в настройках поменяли целиком, например автокомпоновкой, — показывает новое.
    /// </summary>
    public void OnPlacementChanged()
    {
        OnPropertyChanged(nameof(Place));
        OnPropertyChanged(nameof(Angle));
        OnPropertyChanged(nameof(SceneSize));
    }

    /// <summary>
    /// Показывает цвета, ушедшие на устройство, с их яркостью; null — устройство сейчас не светится.
    /// </summary>
    public void Show(IReadOnlyList<RgbColor>? colors, int brightness)
    {
        if (IsKeyboard)
        {
            KeyColors = colors?.Select(c => c.Scale(brightness)).ToArray();

            // Полоса клавиатуры — одна точка: средний цвет клавиш, например для значка в меню
            _bitmap.Write(colors is { Count: > 0 } ? new[] { Average(colors) } : Array.Empty<RgbColor>(), brightness);
            return;
        }

        _bitmap.Write(colors ?? Array.Empty<RgbColor>(), brightness);
    }

    /// <summary>
    /// Показывает новое состояние связи, если оно сменилось.
    /// </summary>
    public void RefreshStatus()
    {
        if (_device.Status == _status)
            return;

        _status = _device.Status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusTitle));
    }

    private static RgbColor Average(IReadOnlyList<RgbColor> colors)
    {
        int r = 0, g = 0, b = 0;

        foreach (var color in colors)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        return new RgbColor((byte)(r / colors.Count), (byte)(g / colors.Count), (byte)(b / colors.Count));
    }
}

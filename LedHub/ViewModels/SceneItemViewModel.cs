using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Services;
using LedHub.Services.Scene;

namespace LedHub.ViewModels;

/// <summary>
/// Элемент на сцене — монитор или устройство — с настройками его места, размера и поворота.
/// </summary>
public sealed partial class SceneItemViewModel : ObservableObject
{
    private readonly Action<SceneItemViewModel> _edited;

    public SceneItemViewModel(SceneItemConfig config, string title, DeviceItemViewModel? device, MonitorInfo? monitor,
        Action<SceneItemViewModel> edited)
    {
        Config = config;
        Title = title;
        Device = device;
        Monitor = monitor;
        _edited = edited;
    }

    public SceneItemConfig Config { get; }

    public string Title { get; }

    /// <summary>
    /// Устройство элемента; null у монитора и у устройства, которое сейчас не найдено.
    /// </summary>
    public DeviceItemViewModel? Device { get; }

    public MonitorInfo? Monitor { get; }

    /// <summary>
    /// Форма на сцене; пересчитывается сценой после каждой правки.
    /// </summary>
    public SceneShape? Shape { get; set; }

    public bool IsMonitor => Config.Kind == SceneItemKind.Monitor;

    public bool IsPanel => Config.Kind == SceneItemKind.Panel;

    public bool IsDevice => !IsMonitor;

    public bool IsLineKind
    {
        get => Config.Kind == SceneItemKind.Strip;
        set
        {
            if (value)
                SetKind(SceneItemKind.Strip);
        }
    }

    public bool IsPanelKind
    {
        get => Config.Kind == SceneItemKind.Panel;
        set
        {
            if (value)
                SetKind(SceneItemKind.Panel);
        }
    }

    public string Subtitle => Config.Kind switch
    {
        SceneItemKind.Monitor => "Монитор",
        SceneItemKind.Panel => Device == null ? "Панель · не найдена" : "Панель из шестигранников",
        _ => Device == null ? "Лента · не найдена" : "Лента"
    };

    public string Icon => Config.Kind switch
    {
        SceneItemKind.Monitor => "",
        SceneItemKind.Panel => "",
        _ => ""
    };

    public bool ShowsSize => !IsPanel;

    public string SizeTitle => IsMonitor ? "Ширина монитора" : "Длина ленты";

    public bool ShowsRotation => !IsMonitor;

    public bool ShowsReverse => Config.Kind == SceneItemKind.Strip;

    /// <summary>
    /// Число шестигранников задаётся, только если форма панели не собрана в редакторе «Форма».
    /// </summary>
    public bool ShowsHexCount => IsPanel && Shape?.Leds.Count is null or 0;

    public double Size
    {
        get => Config.Size;
        set => Edit(() => Config.Size = Math.Clamp(Math.Round(value), 5, 400));
    }

    public double Rotation
    {
        get => Config.Rotation;
        set => Edit(() => Config.Rotation = NormalizeAngle(Math.Round(value)));
    }

    public bool IsReversed
    {
        get => Config.IsReversed;
        set => Edit(() => Config.IsReversed = value);
    }

    public double HexSize
    {
        get => Config.HexSize;
        set => Edit(() => Config.HexSize = Math.Clamp(Math.Round(value, 1), 3, 40));
    }

    public int HexCount
    {
        get => Config.HexCount;
        set => Edit(() => Config.HexCount = Math.Clamp(value, 1, 60));
    }

    /// <summary>
    /// Сообщает интерфейсу, что поля элемента поменялись снаружи, например мышью на схеме.
    /// </summary>
    public void RefreshFields()
    {
        OnPropertyChanged(string.Empty);
    }

    public static double NormalizeAngle(double degrees)
    {
        degrees %= 360;

        if (degrees > 180)
            degrees -= 360;
        else if (degrees <= -180)
            degrees += 360;

        return degrees;
    }

    [RelayCommand]
    private void RotateBy(string degrees)
    {
        Rotation = Config.Rotation + double.Parse(degrees, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SetKind(SceneItemKind kind)
    {
        if (Config.Kind == kind || IsMonitor)
            return;

        Edit(() => Config.Kind = kind);
    }

    private void Edit(Action change)
    {
        change();
        _edited(this);
        OnPropertyChanged(string.Empty);
    }
}

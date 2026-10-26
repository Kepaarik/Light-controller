using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Карточка монитора на странице подсветки: какая лента его подсвечивает, как он повёрнут и как проложена лента.
/// </summary>
public sealed partial class MonitorItemViewModel : ObservableObject
{
    private readonly Func<string, ScreenLayoutConfig> _layoutFor;
    private readonly Action _save;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceText))]
    [NotifyPropertyChangedFor(nameof(LedCount))]
    private DeviceItemViewModel? _device;

    /// <summary>
    /// Раскладка ленты вокруг монитора; только у адресных лент.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLayout))]
    [NotifyPropertyChangedFor(nameof(LayoutSummary))]
    private ScreenLayoutConfig? _layout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhysicalAspect))]
    [NotifyPropertyChangedFor(nameof(LayoutSummary))]
    private RotationOption _rotation;

    /// <summary>
    /// Увеличивается при правках, чтобы схема перерисовалась.
    /// </summary>
    [ObservableProperty]
    private int _layoutVersion;

    public MonitorItemViewModel(MonitorInfo info, MonitorConfig config, IReadOnlyList<RotationOption> rotations,
        Func<string, DeviceItemViewModel?> findDevice, Func<string, ScreenLayoutConfig> layoutFor, Action save)
    {
        Info = info;
        Config = config;
        _layoutFor = layoutFor;
        _save = save;

        // Поля задаются напрямую: загрузка сохранённого не должна считаться правкой
        _rotation = rotations.FirstOrDefault(r => r.Rotation == config.Rotation) ?? rotations[0];
        _device = config.DeviceId != null ? findDevice(config.DeviceId) : null;
        _layout = _device?.Strip != null ? layoutFor(_device.Id) : null;

        if (_layout != null)
            _layout.PropertyChanged += OnLayoutEdited;
    }

    public MonitorInfo Info { get; }

    public MonitorConfig Config { get; }

    public string Title => Info.IsPrimary ? $"Монитор {Info.Number} · основной" : $"Монитор {Info.Number}";

    public string Details =>
        $"{Info.Width}×{Info.Height}, в Windows — {(Info.Width >= Info.Height ? "альбомная" : "книжная")} ориентация";

    public string DeviceText
    {
        get
        {
            if (Device != null)
                return Device.Strip != null ? $"Лента: {Device.Name} — цвета краёв по раскладке ниже" : $"Лента: {Device.Name} — средний цвет монитора";

            return Config.DeviceId != null
                ? "Выбранная лента сейчас не найдена — включите её и повторите поиск"
                : "Лента не выбрана — монитор без подсветки";
        }
    }

    public bool HasLayout => Layout != null;

    public IReadOnlyList<StartOption> StartOptions => StartOption.All;

    /// <summary>
    /// Начало и направление ленты одним выбором; хранится в раскладке как угол и «по часовой».
    /// </summary>
    public StartOption? SelectedStart
    {
        get => Layout == null
            ? null
            : StartOption.All.FirstOrDefault(o => o.Corner == Layout.StartCorner && o.IsClockwise == Layout.IsClockwise);
        set
        {
            if (Layout == null || value == null)
                return;

            Layout.StartCorner = value.Corner;
            Layout.IsClockwise = value.IsClockwise;
        }
    }

    public int LedCount => Device?.Strip?.LedCount ?? 0;

    /// <summary>
    /// Отношение ширины к высоте монитора так, как он стоит на самом деле.
    /// </summary>
    public double PhysicalAspect => MonitorSyncSource.PhysicalAspect(Binding);

    /// <summary>
    /// Привязка для подсветки; читается на каждом кадре.
    /// </summary>
    public MonitorBinding Binding => new(Info, Config.Rotation, Layout);

    public string LayoutSummary
    {
        get
        {
            if (Layout == null || LedCount == 0)
                return "";

            var used = ScreenLayoutMath.Build(Layout, LedCount, PhysicalAspect).Count(p => p != null);

            return used == LedCount
                ? $"Светодиодов на ленте: {LedCount}, все разложены по краям"
                : $"Светодиодов на ленте: {LedCount}, разложено по краям: {used} — остальные будут погашены";
        }
    }

    /// <summary>
    /// Отписывается от раскладки, когда карточка больше не нужна (например, после повторного поиска лент).
    /// </summary>
    public void Detach()
    {
        if (Layout != null)
            Layout.PropertyChanged -= OnLayoutEdited;
    }

    partial void OnDeviceChanged(DeviceItemViewModel? value)
    {
        Config.DeviceId = value?.Id;
        Layout = value?.Strip != null ? _layoutFor(value.Id) : null;
        _save();
    }

    partial void OnLayoutChanged(ScreenLayoutConfig? oldValue, ScreenLayoutConfig? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnLayoutEdited;

        if (newValue != null)
            newValue.PropertyChanged += OnLayoutEdited;

        LayoutVersion++;
        OnPropertyChanged(nameof(SelectedStart));
    }

    partial void OnRotationChanged(RotationOption value)
    {
        Config.Rotation = value.Rotation;
        _save();
        LayoutVersion++;
    }

    /// <summary>
    /// Правка раскладки сразу сохраняется; запущенная подсветка подхватит её на следующем кадре.
    /// </summary>
    private void OnLayoutEdited(object? sender, PropertyChangedEventArgs e)
    {
        _save();
        LayoutVersion++;
        OnPropertyChanged(nameof(LayoutSummary));
        OnPropertyChanged(nameof(SelectedStart));
    }
}

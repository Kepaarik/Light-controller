using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core.Config;
using LightSpace.Engine.Sources;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Карточка монитора на странице «Мониторы»: какая лента его подсвечивает, как он повёрнут и как проложена лента.
/// </summary>
public sealed partial class MonitorItemViewModel : ObservableObject
{
    private readonly Action<MonitorItemViewModel> _deviceChanged;
    private readonly Action _changed;

    private MonitorDeviceOption _device;

    /// <summary>
    /// Увеличивается при каждой правке, чтобы схема ленты перерисовалась.
    /// </summary>
    [ObservableProperty]
    private int _layoutVersion;

    /// <summary>
    /// deviceChanged — у монитора сменилась лента (страница снимает её с других мониторов); changed — любая правка.
    /// </summary>
    public MonitorItemViewModel(MonitorInfo info, MonitorConfig config, IReadOnlyList<MonitorDeviceOption> devices,
        Action<MonitorItemViewModel> deviceChanged, Action changed)
    {
        Info = info;
        Config = config;
        Devices = devices;
        _deviceChanged = deviceChanged;
        _changed = changed;

        // Ленту, которой сейчас нет среди устройств, показываем как «Без ленты», но в настройках не стираем
        _device = devices.FirstOrDefault(d => d.Id == config.DeviceId) ?? devices[0];
    }

    public MonitorInfo Info { get; }

    public MonitorConfig Config { get; }

    /// <summary>
    /// Варианты ленты: «Без ленты» и все устройства.
    /// </summary>
    public IReadOnlyList<MonitorDeviceOption> Devices { get; }

    public IReadOnlyList<RotationOption> Rotations => RotationOption.All;

    public IReadOnlyList<StartOption> StartOptions => StartOption.All;

    public string Title => Info.IsPrimary ? $"Монитор {Info.Number} · основной" : $"Монитор {Info.Number}";

    public string Details =>
        $"{Info.Width}×{Info.Height}, в Windows — {(Info.Width >= Info.Height ? "альбомная" : "книжная")} ориентация";

    public MonitorDeviceOption Device
    {
        get => _device;
        set
        {
            // «Без ленты» при ненайденной ленте — тоже правка: она стирает ленту из настроек
            if (value == null || (value == _device && value.Id == Config.DeviceId))
                return;

            _device = value;
            Config.DeviceId = value.Id;
            OnPropertyChanged();
            _deviceChanged(this);
            NotifyLayout();
            OnPropertyChanged(nameof(DeviceText));
            OnPropertyChanged(nameof(HasLayout));
            OnPropertyChanged(nameof(LedCount));
            _changed();
        }
    }

    public string DeviceText
    {
        get
        {
            if (Device.Id != null)
                return Device.LedCount > 0
                    ? $"{Device.Name} берёт цвета краёв монитора по раскладке ниже"
                    : $"{Device.Name} светится средним цветом монитора";

            return Config.DeviceId != null
                ? "Выбранная лента сейчас не найдена — подключите её"
                : "Монитор без подсветки";
        }
    }

    /// <summary>
    /// Раскладка по краям есть только у адресной ленты.
    /// </summary>
    public bool HasLayout => Device.LedCount > 0;

    public int LedCount => Device.LedCount;

    public RotationOption Rotation
    {
        get => RotationOption.All.FirstOrDefault(r => r.Rotation == Config.Rotation) ?? RotationOption.All[0];
        set
        {
            if (value == null || value.Rotation == Config.Rotation)
                return;

            Config.Rotation = value.Rotation;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PhysicalAspect));
            Edited();
        }
    }

    /// <summary>
    /// Отношение ширины к высоте монитора так, как он стоит на самом деле.
    /// </summary>
    public double PhysicalAspect => ScreenEdges.PhysicalAspect(Info.Width, Info.Height, Config.Rotation);

    public ScreenLayoutConfig Layout => Config.Layout;

    public bool IsViewedFromBack
    {
        get => Layout.IsViewedFromBack;
        set => EditLayout(() => Layout.IsViewedFromBack = value);
    }

    public bool IsViewedFromFront
    {
        get => !Layout.IsViewedFromBack;
        set => EditLayout(() => Layout.IsViewedFromBack = !value);
    }

    public bool UseLeft
    {
        get => Layout.UseLeft;
        set => EditLayout(() => Layout.UseLeft = value);
    }

    public bool UseTop
    {
        get => Layout.UseTop;
        set => EditLayout(() => Layout.UseTop = value);
    }

    public bool UseRight
    {
        get => Layout.UseRight;
        set => EditLayout(() => Layout.UseRight = value);
    }

    public bool UseBottom
    {
        get => Layout.UseBottom;
        set => EditLayout(() => Layout.UseBottom = value);
    }

    public int LeftCount
    {
        get => Layout.LeftCount;
        set => EditLayout(() => Layout.LeftCount = Math.Max(0, value));
    }

    public int TopCount
    {
        get => Layout.TopCount;
        set => EditLayout(() => Layout.TopCount = Math.Max(0, value));
    }

    public int RightCount
    {
        get => Layout.RightCount;
        set => EditLayout(() => Layout.RightCount = Math.Max(0, value));
    }

    public int BottomCount
    {
        get => Layout.BottomCount;
        set => EditLayout(() => Layout.BottomCount = Math.Max(0, value));
    }

    /// <summary>
    /// Сдвиг начала ленты в светодиодах; ползунок отдаёт дробное число.
    /// </summary>
    public double Offset
    {
        get => Layout.Offset;
        set => EditLayout(() => Layout.Offset = Math.Max(0, (int)Math.Round(value)));
    }

    /// <summary>
    /// Начало и направление ленты одним выбором; хранится в раскладке как угол и «по часовой».
    /// </summary>
    public StartOption? SelectedStart
    {
        get => StartOption.All.FirstOrDefault(o => o.Corner == Layout.StartCorner && o.IsClockwise == Layout.IsClockwise);
        set
        {
            if (value == null)
                return;

            EditLayout(() =>
            {
                Layout.StartCorner = value.Corner;
                Layout.IsClockwise = value.IsClockwise;
            });
        }
    }

    public string LayoutSummary
    {
        get
        {
            if (LedCount == 0)
                return "";

            var used = ScreenEdges.Build(Layout, LedCount, PhysicalAspect).Count(p => p != null);

            return used == LedCount
                ? $"Светодиодов на ленте: {LedCount}, все разложены по краям"
                : $"Светодиодов на ленте: {LedCount}, разложено по краям: {used} — остальные будут погашены";
        }
    }

    /// <summary>
    /// Лента ушла на другой монитор: этот остаётся без подсветки.
    /// </summary>
    public void ClearDevice()
    {
        _device = Devices[0];
        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(DeviceText));
        OnPropertyChanged(nameof(HasLayout));
        OnPropertyChanged(nameof(LedCount));
    }

    private void EditLayout(Action edit)
    {
        edit();
        NotifyLayout();
        Edited();
    }

    /// <summary>
    /// Все поля раскладки перечитываются: одна правка может менять сразу несколько (вид спереди и сзади, начало ленты).
    /// </summary>
    private void NotifyLayout()
    {
        OnPropertyChanged(nameof(IsViewedFromBack));
        OnPropertyChanged(nameof(IsViewedFromFront));
        OnPropertyChanged(nameof(UseLeft));
        OnPropertyChanged(nameof(UseTop));
        OnPropertyChanged(nameof(UseRight));
        OnPropertyChanged(nameof(UseBottom));
        OnPropertyChanged(nameof(LeftCount));
        OnPropertyChanged(nameof(TopCount));
        OnPropertyChanged(nameof(RightCount));
        OnPropertyChanged(nameof(BottomCount));
        OnPropertyChanged(nameof(Offset));
        OnPropertyChanged(nameof(SelectedStart));
        OnPropertyChanged(nameof(LayoutSummary));
    }

    /// <summary>
    /// Правка сразу уходит в подсветку и сохраняется; схема перерисовывается.
    /// </summary>
    private void Edited()
    {
        LayoutVersion++;
        OnPropertyChanged(nameof(LayoutSummary));
        _changed();
    }
}

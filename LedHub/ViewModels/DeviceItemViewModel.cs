using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;
using LedHub.Protocols.Ble;
using LedHub.Protocols.Robobloq;
using LedHub.Protocols.RoyalKludge;
using LedHub.Protocols.Serial;

namespace LedHub.ViewModels;

public sealed partial class DeviceItemViewModel : ObservableObject, ILightTarget
{
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>
    /// Имя, которое дал пользователь; null — показывать имя самого устройства.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string? _customName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorHex))]
    private RgbColor _color = RgbColor.White;

    [ObservableProperty]
    private bool? _isOn;

    public DeviceItemViewModel(LedDevice device)
    {
        Device = device;
    }

    public LedDevice Device { get; }

    public string Id => Device.Id;

    public string Name => CustomName ?? Device.Name;

    public string Protocol => Device.Protocol;

    public string Address => Device.Address;

    public string? StatusText => Device.StatusText;

    public string Title => Name;

    public string Subtitle => CustomName != null
        ? $"{Device.Name} · {Device.Protocol} · {Device.Address}"
        : $"{Device.Protocol} · {Device.Address}";

    /// <summary>
    /// Последний выставленный цвет для значка в списке.
    /// </summary>
    public string ColorHex => Color.ToHex();

    /// <summary>
    /// Есть ли у контроллера встроенные эффекты.
    /// </summary>
    public IHardwareEffectsDevice? HardwareEffects => Device is IHardwareEffectsDevice { HardwareEffects.Count: > 0 } effects ? effects : null;

    /// <summary>
    /// Встроенный микрофон контроллера, если он есть.
    /// </summary>
    public IMicrophoneDevice? Microphone => Device is IMicrophoneDevice { MicrophoneModes.Count: > 0 } microphone ? microphone : null;

    /// <summary>
    /// Значок способа подключения из шрифта Segoe Fluent Icons / MDL2 Assets.
    /// </summary>
    public string Icon => Device switch
    {
        RkKeyboardDevice => "",
        BleLedDevice => "",
        SerialLedDevice or RobobloqDevice => "",
        _ => ""
    };

    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Лента как адресная, если она умеет задавать цвет каждого светодиода; иначе null.
    /// </summary>
    public IAddressableLedDevice? Strip => Device is IAddressableLedDevice { LedCount: > 0 } strip ? strip : null;

    /// <summary>
    /// Можно ли собрать для ленты форму из шестигранников: только адресные ленты, которым рисунок можно отправить.
    /// MELK сюда не входит — его контроллер не принимает цвета отдельных светодиодов (проверено на стене).
    /// Клавиатура тоже: её форма — раскладка клавиш.
    /// </summary>
    public bool SupportsHexLayout => Strip != null && Device is not IKeyboardDevice;

    /// <summary>
    /// Последние цвета светодиодов из редактора; null, пока лентой не управляли по светодиодам.
    /// </summary>
    public RgbColor[]? LedColors { get; set; }

    /// <summary>
    /// Лента показывает разные цвета на светодиодах: смена цвета на круге задаёт кисть, а не заливку.
    /// </summary>
    public bool IsPixelMode { get; set; }

    public IReadOnlyList<LedDevice> GetDevices() => new[] { Device };

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
    }
}

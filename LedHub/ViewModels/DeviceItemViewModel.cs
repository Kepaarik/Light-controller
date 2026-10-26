using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;
using LedHub.Protocols.Ble;
using LedHub.Protocols.Serial;

namespace LedHub.ViewModels;

public sealed partial class DeviceItemViewModel : ObservableObject, ILightTarget
{
    [ObservableProperty]
    private bool _isChecked;

    public DeviceItemViewModel(LedDevice device)
    {
        Device = device;
    }

    public LedDevice Device { get; }

    public string Id => Device.Id;

    public string Name => Device.Name;

    public string Protocol => Device.Protocol;

    public string Address => Device.Address;

    public string? StatusText => Device.StatusText;

    public string Title => Device.Name;

    public string Subtitle => $"{Device.Protocol} · {Device.Address}";

    /// <summary>
    /// Значок способа подключения из шрифта Segoe Fluent Icons / MDL2 Assets.
    /// </summary>
    public string Icon => Device switch
    {
        BleLedDevice => "",
        SerialLedDevice => "",
        _ => ""
    };

    public RgbColor Color { get; set; } = RgbColor.White;

    public int Brightness { get; set; } = 100;

    public IReadOnlyList<LedDevice> GetDevices() => new[] { Device };

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
    }
}

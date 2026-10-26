using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;

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

    public RgbColor Color { get; set; } = RgbColor.White;

    public int Brightness { get; set; } = 100;

    public IReadOnlyList<LedDevice> GetDevices() => new[] { Device };

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
    }
}

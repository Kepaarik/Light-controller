using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;
using LedHub.Services;

namespace LedHub.ViewModels;

public sealed record GroupMemberViewModel(string Id, string Name, bool IsOnline);

public sealed partial class GroupItemViewModel : ObservableObject, ILightTarget
{
    private readonly Func<string, DeviceItemViewModel?> _findDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorHex))]
    private RgbColor _color = RgbColor.White;

    public GroupItemViewModel(GroupConfig config, Func<string, DeviceItemViewModel?> findDevice)
    {
        Config = config;
        _findDevice = findDevice;

        Refresh();
    }

    public GroupConfig Config { get; }

    public string Name => Config.Name;

    public string Title => $"Группа «{Config.Name}»";

    public string Summary => $"{Members.Count(m => m.IsOnline)} из {Members.Count} в сети";

    public string Subtitle => Summary;

    public ObservableCollection<GroupMemberViewModel> Members { get; } = new();

    public string ColorHex => Color.ToHex();

    public int Brightness { get; set; } = 100;

    public void Rename(string name)
    {
        Config.Name = name;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
    }

    public IReadOnlyList<LedDevice> GetDevices()
    {
        return Config.Members
            .Select(m => _findDevice(m.Id))
            .OfType<DeviceItemViewModel>()
            .Select(d => d.Device)
            .ToList();
    }

    /// <summary>
    /// Пересобирает список участников с учётом того, какие устройства найдены сейчас.
    /// </summary>
    public void Refresh()
    {
        Members.Clear();

        foreach (var member in Config.Members)
        {
            var device = _findDevice(member.Id);
            Members.Add(new GroupMemberViewModel(member.Id, device?.Name ?? member.Name, device != null));
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Subtitle));
    }
}

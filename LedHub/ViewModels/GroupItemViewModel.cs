using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;
using LedHub.Services;

namespace LedHub.ViewModels;

public sealed record GroupMemberViewModel(string Id, string Name, bool IsOnline);

public sealed class GroupItemViewModel : ObservableObject, ILightTarget
{
    private readonly Func<string, DeviceItemViewModel?> _findDevice;

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

    public RgbColor Color { get; set; } = RgbColor.White;

    public int Brightness { get; set; } = 100;

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
            Members.Add(new GroupMemberViewModel(member.Id, member.Name, _findDevice(member.Id) != null));

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Subtitle));
    }
}

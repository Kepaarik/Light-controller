using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Плитка режима «Музыка с компьютера».
/// </summary>
public sealed partial class MusicModeOption : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public MusicModeOption(MusicMode mode, string title, string description)
    {
        Mode = mode;
        Title = title;
        Description = description;
    }

    public MusicMode Mode { get; }

    public string Title { get; }

    public string Description { get; }
}

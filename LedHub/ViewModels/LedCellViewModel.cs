using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Core;

namespace LedHub.ViewModels;

/// <summary>
/// Один светодиод адресной ленты в редакторе.
/// </summary>
public sealed partial class LedCellViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hex))]
    private RgbColor _color;

    public LedCellViewModel(int index, RgbColor color)
    {
        Index = index;
        _color = color;
    }

    public int Index { get; }

    public string Title => $"Светодиод {Index + 1}";

    public string Hex => Color.ToHex();
}

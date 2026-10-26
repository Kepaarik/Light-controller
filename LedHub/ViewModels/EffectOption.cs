using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Плитка программного эффекта; цвета превью задают градиент на плитке.
/// </summary>
public sealed partial class EffectOption : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public EffectOption(EffectKind kind, string title, string previewFrom, string previewTo)
    {
        Kind = kind;
        Title = title;
        PreviewFrom = previewFrom;
        PreviewTo = previewTo;
    }

    public EffectKind Kind { get; }

    public string Title { get; }

    public string PreviewFrom { get; }

    public string PreviewTo { get; }
}

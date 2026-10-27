using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services.Effects;

namespace LedHub.ViewModels;

/// <summary>
/// Плитка эффекта в галерее: выбранный эффект показывает свои настройки, запущенный — подсвечен.
/// </summary>
public sealed partial class EffectOption : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSelected;

    public EffectOption(EffectDefinition definition)
    {
        Definition = definition;
    }

    public EffectDefinition Definition { get; }

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    public bool UsesAudio => Definition.UsesAudio;
}

/// <summary>
/// Раздел галереи эффектов: «Цвета», «Движение», «Природа», «Музыка».
/// </summary>
public sealed record EffectGroup(string Title, IReadOnlyList<EffectOption> Effects);

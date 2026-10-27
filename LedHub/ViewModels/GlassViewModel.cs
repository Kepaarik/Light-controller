using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Core;
using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Настройки стекла окна: пресеты и ползунки прозрачности, тонировки, плотности и блика.
/// </summary>
public sealed partial class GlassViewModel : ObservableObject
{
    private readonly GlassConfig _glass;
    private readonly Action _save;

    public GlassViewModel(GlassConfig glass, Action save)
    {
        _glass = glass;
        _save = save;

        GlassTheme.Apply(_glass);
    }

    public IReadOnlyList<GlassPreset> Presets => GlassTheme.Presets;

    public double Transparency
    {
        get => _glass.Transparency;
        set => Change(() => _glass.Transparency = Math.Round(value));
    }

    public double TintStrength
    {
        get => _glass.TintStrength;
        set => Change(() => _glass.TintStrength = Math.Round(value));
    }

    public double Frost
    {
        get => _glass.Frost;
        set => Change(() => _glass.Frost = Math.Round(value));
    }

    public double Shine
    {
        get => _glass.Shine;
        set => Change(() => _glass.Shine = Math.Round(value));
    }

    /// <summary>
    /// Тон тонировки по кругу цветов, от 0 до 360.
    /// </summary>
    public double TintHue
    {
        get => RgbColor.Parse(_glass.Tint).ToHsv().Hue;
        set => Change(() => _glass.Tint = RgbColor.FromHsv(value, 0.65, 1).ToHex());
    }

    public Brush TintSwatch => GlassTheme.SwatchOf(_glass.Tint);

    [RelayCommand]
    private void ApplyPreset(GlassPreset preset)
    {
        Change(() =>
        {
            _glass.Tint = preset.Tint;
            _glass.Transparency = preset.Transparency;
            _glass.TintStrength = preset.TintStrength;
            _glass.Frost = preset.Frost;
            _glass.Shine = preset.Shine;
        });
    }

    private void Change(Action change)
    {
        change();
        GlassTheme.Apply(_glass);
        _save();

        // Пресет меняет сразу все ползунки
        OnPropertyChanged(string.Empty);
    }
}

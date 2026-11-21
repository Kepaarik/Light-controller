using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Редактор содержимого-экрана: насыщенность, яркость и сглаживание. Все правки сразу попадают в содержимое.
/// </summary>
public sealed class ScreenEditorViewModel : ObservableObject
{
    private readonly Action _changed;

    private ScreenConfig? _screen;

    /// <summary>
    /// changed вызывается после каждой правки.
    /// </summary>
    public ScreenEditorViewModel(Action changed)
    {
        _changed = changed;
    }

    public double Saturation
    {
        get => _screen?.Saturation ?? ScreenConfig.DefaultSaturation;
        set => SetNumber(value, s => s.Saturation, (s, v) => s.Saturation = v);
    }

    public double Brightness
    {
        get => _screen?.Brightness ?? 100;
        set => SetNumber(value, s => s.Brightness, (s, v) => s.Brightness = v);
    }

    public double Smoothing
    {
        get => _screen?.Smoothing ?? 0;
        set => SetNumber(value, s => s.Smoothing, (s, v) => s.Smoothing = v);
    }

    /// <summary>
    /// Открывает содержимое для правки; null — ничего не открыто.
    /// </summary>
    public void Edit(ContentConfig? content)
    {
        _screen = content?.Screen;

        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Smoothing));
    }

    private void SetNumber(double value, Func<ScreenConfig, int> get, Action<ScreenConfig, int> set, [CallerMemberName] string? name = null)
    {
        var number = (int)Math.Round(value);

        if (_screen == null || number == get(_screen))
            return;

        set(_screen, number);
        OnPropertyChanged(name);
        _changed();
    }
}

using System.Globalization;
using System.Windows.Data;
using LedHub.Core;
using LedHub.Services;
using LedHub.Services.Effects;
using LedHub.ViewModels;

namespace LedHub.Controls;

/// <summary>
/// Какой эффект рисовать на плитке: эффект библиотеки — сам себя, встроенный эффект ленты или клавиатуры — похожий по названию.
/// </summary>
public sealed class EffectLookConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            EffectDefinition effect => effect,
            EffectOption option => option.Definition,
            HardwareEffect hardware => EffectLibrary.LookAlike(hardware.Title),
            MusicModeOption music => EffectLibrary.Find(music.Mode switch
            {
                MusicMode.Spectrum => "music-spectrum",
                MusicMode.RainbowBeat => "music-beat",
                _ => "music-pulse"
            }),
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

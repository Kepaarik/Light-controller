using System.Windows;
using System.Windows.Controls;

namespace LightSpace.App.Views;

/// <summary>
/// Редактор содержимого-эффекта: галерея плиток и настройки выбранного эффекта.
/// </summary>
public partial class EffectEditor : UserControl
{
    public static readonly DependencyProperty ShowsSettingsProperty =
        DependencyProperty.Register(nameof(ShowsSettings), typeof(bool), typeof(EffectEditor), new PropertyMetadata(true));

    public EffectEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Показываются ли настройки выбранного эффекта под галереей.
    /// </summary>
    public bool ShowsSettings
    {
        get => (bool)GetValue(ShowsSettingsProperty);
        set => SetValue(ShowsSettingsProperty, value);
    }
}

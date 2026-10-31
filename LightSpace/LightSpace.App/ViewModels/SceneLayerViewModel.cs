using System.Runtime.CompilerServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core;
using LightSpace.Core.Config;
using LightSpace.Engine.Effects;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Режим смешивания слоя в сегментах.
/// </summary>
public sealed record LayerBlendOption(LayerBlend Blend, string Title)
{
    public static readonly IReadOnlyList<LayerBlendOption> All = new LayerBlendOption[]
    {
        new(LayerBlend.Normal, "Обычное"),
        new(LayerBlend.Add, "Сложение"),
        new(LayerBlend.Lighten, "Светлее")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Слой в редакторе содержимого-сцены: название по его эффекту, границы на холсте, видимость, непрозрачность
/// и смешивание. Правки сразу попадают в настройки слоя.
/// </summary>
public sealed partial class SceneLayerViewModel : ObservableObject
{
    private readonly Action _changed;

    // Слой выбран: его границы можно тянуть за ручку, ниже открыты его настройки и эффект
    [ObservableProperty]
    private bool _isSelected;

    // Порядок слоя на холсте: верхний слой списка лежит поверх остальных и первым ловит мышь
    [ObservableProperty]
    private int _zIndex;

    /// <summary>
    /// changed вызывается после каждой правки видимости, непрозрачности или смешивания.
    /// </summary>
    public SceneLayerViewModel(SceneLayerConfig settings, Action changed)
    {
        Settings = settings;
        _changed = changed;
    }

    /// <summary>
    /// Сохраняемые настройки слоя.
    /// </summary>
    public SceneLayerConfig Settings { get; }

    /// <summary>
    /// Как слой подписан: названием его эффекта.
    /// </summary>
    public string Title => EffectLibrary.FindOrSolid(Settings.Effect.EffectId).Title;

    /// <summary>
    /// Смешивание и непрозрачность словами: «Сложение · 60 %».
    /// </summary>
    public string Caption => $"{Blend.Title} · {Settings.Opacity} %";

    /// <summary>
    /// Границы слоя на сцене.
    /// </summary>
    public Rect Place => new(Settings.X, Settings.Y, Settings.Width, Settings.Height);

    /// <summary>
    /// Левый край границ слоя в единицах сцены, как он вводится в поле.
    /// </summary>
    public string LeftText
    {
        get => NumberText.Format(Settings.X);
        set => SetBoundsText(value, (bounds, number) => bounds with { X = number });
    }

    /// <summary>
    /// Верхний край границ слоя в единицах сцены, как он вводится в поле.
    /// </summary>
    public string TopText
    {
        get => NumberText.Format(Settings.Y);
        set => SetBoundsText(value, (bounds, number) => bounds with { Y = number });
    }

    /// <summary>
    /// Ширина границ слоя в единицах сцены, как она вводится в поле.
    /// </summary>
    public string WidthText
    {
        get => NumberText.Format(Settings.Width);
        set => SetBoundsText(value, (bounds, number) => bounds with { Width = number });
    }

    /// <summary>
    /// Высота границ слоя в единицах сцены, как она вводится в поле.
    /// </summary>
    public string HeightText
    {
        get => NumberText.Format(Settings.Height);
        set => SetBoundsText(value, (bounds, number) => bounds with { Height = number });
    }

    /// <summary>
    /// Слой виден; скрытый слой ничего не меняет.
    /// </summary>
    public bool IsVisible
    {
        get => Settings.IsVisible;
        set
        {
            if (value == Settings.IsVisible)
                return;

            Settings.IsVisible = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Непрозрачность слоя, 0–100 %.
    /// </summary>
    public double Opacity
    {
        get => Settings.Opacity;
        set
        {
            var opacity = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (opacity == Settings.Opacity)
                return;

            Settings.Opacity = opacity;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Caption));
            _changed();
        }
    }

    /// <summary>
    /// Как цвет слоя ложится на то, что под ним.
    /// </summary>
    public LayerBlendOption Blend
    {
        get => LayerBlendOption.All.First(o => o.Blend == Settings.Blend);
        set
        {
            if (value == null || value.Blend == Settings.Blend)
                return;

            Settings.Blend = value.Blend;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Caption));
            _changed();
        }
    }

    /// <summary>
    /// Задаёт границы слоя, пока их тянут мышью; они не выходят за сцену. В настройки и движку уходят, когда мышь отпустят.
    /// </summary>
    public void SetBounds(SceneRect bounds)
    {
        Settings.SetBounds(bounds);
        OnPropertyChanged(nameof(Place));
        OnPropertyChanged(nameof(LeftText));
        OnPropertyChanged(nameof(TopText));
        OnPropertyChanged(nameof(WidthText));
        OnPropertyChanged(nameof(HeightText));
    }

    /// <summary>
    /// У слоя выбрали другой эффект — меняется его название.
    /// </summary>
    public void OnEffectChanged()
    {
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Границы ввели числом в поле: они сразу уходят движку. Не число — поле возвращает прежнее значение.
    /// </summary>
    private void SetBoundsText(string value, Func<SceneRect, double, SceneRect> change, [CallerMemberName] string? name = null)
    {
        if (!NumberText.TryParse(value, out var number))
        {
            OnPropertyChanged(name);
            return;
        }

        SetBounds(change(Settings.Bounds, number));
        _changed();
    }
}

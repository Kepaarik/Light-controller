using System.Text.Json.Serialization;

namespace LightSpace.Core.Config;

/// <summary>
/// Как цвет слоя ложится на то, что под ним.
/// </summary>
public enum LayerBlend
{
    /// <summary>Слой закрывает то, что под ним, с учётом своей непрозрачности.</summary>
    Normal,

    /// <summary>Цвета слоя прибавляются к цветам под ним.</summary>
    Add,

    /// <summary>По каждому каналу берётся более яркий цвет: слоя или того, что под ним.</summary>
    Lighten
}

/// <summary>
/// Слой содержимого-сцены: эффект из галереи в прямоугольных границах на сцене. Светодиоды за границами слой не меняет.
/// </summary>
public sealed class SceneLayerConfig
{
    /// <summary>
    /// Самая короткая сторона границ слоя.
    /// </summary>
    public const double MinSize = 4;

    /// <summary>
    /// Постоянный идентификатор слоя.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Эффект слоя и сохранённые настройки эффектов, как у содержимого-эффекта.
    /// </summary>
    public ContentConfig Effect { get; set; } = new();

    /// <summary>
    /// Левый край границ слоя на сцене.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Верхний край границ слоя на сцене.
    /// </summary>
    public double Y { get; set; }

    public double Width { get; set; } = SceneArea.Width;

    public double Height { get; set; } = SceneArea.Height;

    /// <summary>
    /// Слой виден; скрытый слой ничего не меняет, но остаётся в списке.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Непрозрачность слоя, 0–100 %.
    /// </summary>
    public int Opacity { get; set; } = 100;

    public LayerBlend Blend { get; set; } = LayerBlend.Normal;

    /// <summary>
    /// Границы слоя на сцене.
    /// </summary>
    [JsonIgnore]
    public SceneRect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// Новый слой во всю сцену с эффектом и его настройками из содержимого content.
    /// </summary>
    public static SceneLayerConfig Covering(ContentConfig content)
    {
        var effect = content.Clone();

        effect.Kind = ContentKind.Effect;
        effect.Layers.Clear();

        return new SceneLayerConfig { Id = GroupConfig.NewId(), Effect = effect };
    }

    /// <summary>
    /// Задаёт границы слоя; они сдвигаются и сжимаются так, чтобы целиком лежать на сцене.
    /// </summary>
    public void SetBounds(SceneRect bounds)
    {
        Width = Math.Clamp(bounds.Width, MinSize, SceneArea.Width);
        Height = Math.Clamp(bounds.Height, MinSize, SceneArea.Height);
        X = Math.Clamp(bounds.X, 0, SceneArea.Width - Width);
        Y = Math.Clamp(bounds.Y, 0, SceneArea.Height - Height);
    }

    /// <summary>
    /// Независимая копия слоя: правки копии не меняют оригинал.
    /// </summary>
    public SceneLayerConfig Clone()
    {
        return new SceneLayerConfig
        {
            Id = Id,
            Effect = Effect.Clone(),
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            IsVisible = IsVisible,
            Opacity = Opacity,
            Blend = Blend
        };
    }

    /// <summary>
    /// Приводит слой к допустимому: в слое играет только эффект, границы лежат на сцене, непрозрачность 0–100 %.
    /// </summary>
    public void Normalize()
    {
        Id ??= "";
        Effect ??= new ContentConfig();

        // Слой внутри слоя не играет: у слоя всегда содержимое-эффект
        Effect.Kind = ContentKind.Effect;
        Effect.Layers = new List<SceneLayerConfig>();
        Effect.Normalize();

        // Испорченные руками границы в файле становятся всей сценой
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height))
            SetBounds(SceneRect.Whole);
        else
            SetBounds(Bounds);

        Opacity = Math.Clamp(Opacity, 0, 100);

        if (!Enum.IsDefined(Blend))
            Blend = LayerBlend.Normal;
    }
}

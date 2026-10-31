namespace LightSpace.Core;

/// <summary>
/// Прямоугольник на сцене в единицах сцены: левый верхний угол, ширина и высота.
/// </summary>
public readonly record struct SceneRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Вся сцена.
    /// </summary>
    public static readonly SceneRect Whole = new(0, 0, SceneArea.Width, SceneArea.Height);

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>
    /// Точка лежит внутри прямоугольника или на его краю.
    /// </summary>
    public bool Contains(double x, double y)
    {
        return x >= X && x <= Right && y >= Y && y <= Bottom;
    }

    /// <summary>
    /// У прямоугольников есть общая часть, а не только общий край.
    /// </summary>
    public bool IntersectsWith(SceneRect other)
    {
        return other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;
    }

    /// <summary>
    /// Прямоугольник, раздвинутый на margin в каждую сторону.
    /// </summary>
    public SceneRect Inflate(double margin)
    {
        return new SceneRect(X - margin, Y - margin, Width + margin * 2, Height + margin * 2);
    }

    /// <summary>
    /// Наименьший прямоугольник, в который входят оба.
    /// </summary>
    public SceneRect Union(SceneRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);

        return new SceneRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }
}

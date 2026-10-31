namespace LightSpace.Core;

/// <summary>
/// Сцена — карта стола, на которой стоят устройства и лежат слои эффектов. Это прямоугольник Width × Height
/// в единицах сцены (примерно сантиметрах): начало координат в левом верхнем углу, Y растёт вниз.
/// </summary>
public static class SceneArea
{
    /// <summary>
    /// Ширина сцены в единицах сцены.
    /// </summary>
    public const double Width = 200;

    /// <summary>
    /// Высота сцены в единицах сцены.
    /// </summary>
    public const double Height = 120;
}

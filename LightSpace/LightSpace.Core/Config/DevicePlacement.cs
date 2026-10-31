namespace LightSpace.Core.Config;

/// <summary>
/// Место устройства на сцене: где его середина, какого оно размера и как повёрнуто. Единицы — единицы сцены.
/// </summary>
public sealed class DevicePlacement
{
    /// <summary>
    /// Самый маленький и самый большой размер устройства на сцене.
    /// </summary>
    public const double MinSize = 4;
    public const double MaxSize = 400;

    /// <summary>
    /// Середина устройства по горизонтали.
    /// </summary>
    public double X { get; set; } = SceneArea.Width / 2;

    /// <summary>
    /// Середина устройства по вертикали.
    /// </summary>
    public double Y { get; set; } = SceneArea.Height / 2;

    /// <summary>
    /// Длина ленты, ширина клавиатуры или поперечник устройства одного цвета; 0 — размер по умолчанию для такого устройства.
    /// </summary>
    public double Size { get; set; }

    /// <summary>
    /// Поворот в градусах по часовой стрелке, 0–360; у ленты без поворота первый светодиод слева.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Приводит место к допустимому: середина внутри сцены, размер в разумных пределах, поворот от 0 до 360.
    /// </summary>
    public void Normalize()
    {
        // Испорченные руками числа в файле дали бы устройство вне сцены или без размера
        X = double.IsFinite(X) ? Math.Clamp(X, 0, SceneArea.Width) : SceneArea.Width / 2;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, 0, SceneArea.Height) : SceneArea.Height / 2;
        Size = !double.IsFinite(Size) || Size <= 0 ? 0 : Math.Clamp(Size, MinSize, MaxSize);
        Rotation = double.IsFinite(Rotation) ? (Rotation % 360 + 360) % 360 : 0;
    }
}

namespace LightSpace.Core.Config;

/// <summary>
/// Настройки содержимого-экрана: с какого монитора берётся картинка и как её цвета ложатся на светодиоды.
/// Сцена целиком растягивается на кадр монитора, каждый светодиод берёт цвет картинки под своим местом на сцене.
/// </summary>
public sealed class ScreenConfig
{
    /// <summary>
    /// Насыщенность по умолчанию: средний цвет картинки обычно блёклый, поэтому чуть сильнее обычной.
    /// </summary>
    public const int DefaultSaturation = 135;

    /// <summary>
    /// Системное имя монитора (например, \\.\DISPLAY1); пусто — основной монитор Windows.
    /// Если такого монитора сейчас нет, картинка берётся с основного.
    /// </summary>
    public string MonitorKey { get; set; } = "";

    /// <summary>
    /// Насыщенность цветов картинки, 0–200 %: 100 — как на экране, 0 — оттенки серого.
    /// </summary>
    public int Saturation { get; set; } = DefaultSaturation;

    /// <summary>
    /// Яркость цветов картинки, 10–200 %: 100 — как на экране.
    /// </summary>
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Сглаживание, 0–95 %: сколько от прежнего цвета остаётся за 1/20 секунды. Больше — плавнее, но с запаздыванием.
    /// </summary>
    public int Smoothing { get; set; } = 55;

    /// <summary>
    /// Независимая копия настроек: правки копии не меняют оригинал.
    /// </summary>
    public ScreenConfig Clone()
    {
        return new ScreenConfig
        {
            MonitorKey = MonitorKey,
            Saturation = Saturation,
            Brightness = Brightness,
            Smoothing = Smoothing
        };
    }

    /// <summary>
    /// Приводит настройки к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        MonitorKey ??= "";
        Saturation = Math.Clamp(Saturation, 0, 200);
        Brightness = Math.Clamp(Brightness, 10, 200);
        Smoothing = Math.Clamp(Smoothing, 0, 95);
    }
}

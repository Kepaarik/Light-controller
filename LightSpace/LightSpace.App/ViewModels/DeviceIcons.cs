namespace LightSpace.App.ViewModels;

/// <summary>
/// Значки, из которых пользователь выбирает значок устройства в левом меню (символы Segoe Fluent Icons).
/// </summary>
public static class DeviceIcons
{
    /// <summary>
    /// Значок ленты и устройства одного цвета, пока пользователь не выбрал свой.
    /// </summary>
    public const string Strip = "";

    /// <summary>
    /// Значок клавиатуры, пока пользователь не выбрал свой.
    /// </summary>
    public const string Keyboard = "";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Strip,
        "", // Яркость
        Keyboard,
        "", // Мышь
        "", // Монитор
        "", // Ноутбук
        "", // Колонки
        "", // Звук
        "", // Игры
        "", // Дом
        "", // Луна
        "", // Звезда
        "", // Сердце
        ""  // Устройства
    };
}

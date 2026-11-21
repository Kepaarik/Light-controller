namespace LightSpace.App.ViewModels;

/// <summary>
/// Тип содержимого устройства в сегментах: эффект, экран, музыка, встроенный эффект.
/// </summary>
/// <param name="Title">Название типа на сегменте.</param>
/// <param name="IsAvailable">Тип уже можно выбрать; остальные показаны выключенными.</param>
public sealed record ContentKindOption(string Title, bool IsAvailable)
{
    /// <summary>
    /// Эффект из галереи.
    /// </summary>
    public static readonly ContentKindOption Effect = new("Эффект", true);

    /// <summary>
    /// Цвета краёв монитора, за которым висит лента.
    /// </summary>
    public static readonly ContentKindOption Screen = new("Экран", true);

    /// <summary>
    /// Свет, который следует за звуком компьютера или микрофона.
    /// </summary>
    public static readonly ContentKindOption Music = new("Музыка", true);

    /// <summary>
    /// Эффекты, которые устройства играют сами, без кадров от приложения.
    /// </summary>
    public static readonly ContentKindOption Builtin = new("Встроенный", true);

    /// <summary>
    /// Все типы содержимого по порядку; экран — только у ленты за монитором.
    /// </summary>
    public static readonly IReadOnlyList<ContentKindOption> All = new ContentKindOption[]
    {
        Effect,
        Screen,
        Music,
        Builtin
    };

    public override string ToString() => Title;
}

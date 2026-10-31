namespace LightSpace.App.ViewModels;

/// <summary>
/// Тип содержимого сценария в сегментах «Что»: эффект, сцена, экран, музыка, игра, встроенный эффект.
/// </summary>
/// <param name="Title">Название типа на сегменте.</param>
/// <param name="IsAvailable">Тип уже можно выбрать; остальные показаны выключенными.</param>
public sealed record ContentKindOption(string Title, bool IsAvailable)
{
    /// <summary>
    /// Эффект из галереи на всех устройствах группы.
    /// </summary>
    public static readonly ContentKindOption Effect = new("Эффект", true);

    /// <summary>
    /// Слои эффектов, разложенные по сцене.
    /// </summary>
    public static readonly ContentKindOption Scene = new("Сцена", true);

    /// <summary>
    /// Цвета картинки монитора под местами светодиодов на сцене.
    /// </summary>
    public static readonly ContentKindOption Screen = new("Экран", true);

    /// <summary>
    /// Свет, который следует за звуком компьютера или микрофона.
    /// </summary>
    public static readonly ContentKindOption Music = new("Музыка", true);

    /// <summary>
    /// Полоски здоровья и маны или брони и события Dota 2 и CS2; другие игры — картинка монитора.
    /// </summary>
    public static readonly ContentKindOption Game = new("Игра", true);

    /// <summary>
    /// Все типы содержимого по порядку; пока работают все, кроме встроенного эффекта устройства.
    /// </summary>
    public static readonly IReadOnlyList<ContentKindOption> All = new ContentKindOption[]
    {
        Effect,
        Scene,
        Screen,
        Music,
        Game,
        new("Встроенный", false)
    };

    public override string ToString() => Title;
}

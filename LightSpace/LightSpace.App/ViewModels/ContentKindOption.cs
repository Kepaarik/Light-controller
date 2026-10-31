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
    /// Все типы содержимого по порядку; пока работают эффект из галереи и сцена.
    /// </summary>
    public static readonly IReadOnlyList<ContentKindOption> All = new ContentKindOption[]
    {
        Effect,
        Scene,
        new("Экран", false),
        new("Музыка", false),
        new("Игра", false),
        new("Встроенный", false)
    };

    public override string ToString() => Title;
}

namespace LightSpace.App.ViewModels;

/// <summary>
/// Тип содержимого сценария в сегментах «Что»: эффект, сцена, экран, музыка, игра, встроенный эффект.
/// </summary>
/// <param name="Title">Название типа на сегменте.</param>
/// <param name="IsAvailable">Тип уже можно выбрать; остальные показаны выключенными.</param>
public sealed record ContentKindOption(string Title, bool IsAvailable)
{
    /// <summary>
    /// Все типы содержимого по порядку; пока работает только эффект из галереи.
    /// </summary>
    public static readonly IReadOnlyList<ContentKindOption> All = new ContentKindOption[]
    {
        new("Эффект", true),
        new("Сцена", false),
        new("Экран", false),
        new("Музыка", false),
        new("Игра", false),
        new("Встроенный", false)
    };

    public override string ToString() => Title;
}

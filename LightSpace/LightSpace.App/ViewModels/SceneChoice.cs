namespace LightSpace.App.ViewModels;

/// <summary>
/// Вариант настройки сцены в сегментах, например режим или направление: значение и его название.
/// </summary>
public sealed record SceneChoice<T>(T Value, string Title)
{
    public override string ToString() => Title;
}

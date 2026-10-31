namespace LightSpace.Core.Config;

/// <summary>
/// Сценарий: что играть в группе, пока выполнено его условие. Один сценарий можно поставить в несколько групп —
/// группы ссылаются на него по идентификатору.
/// </summary>
public sealed class ScenarioConfig
{
    /// <summary>
    /// Постоянный идентификатор сценария, по которому на него ссылаются группы.
    /// </summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// Что играет, пока сценарий активен.
    /// </summary>
    public ContentConfig Content { get; set; } = new();
}

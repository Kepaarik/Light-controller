namespace LightSpace.Core.Config;

/// <summary>
/// Группа устройств: на всех её устройствах играет одно и то же — её содержимое. Устройство входит ровно в одну группу.
/// </summary>
public sealed class GroupConfig
{
    /// <summary>
    /// Постоянный идентификатор группы.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Группа-одиночка: устройство без группы, которое само себе группа. У неё ровно одно устройство,
    /// нет своего названия и обводки — на сцене она выглядит как само устройство.
    /// </summary>
    public bool IsSolo { get; set; }

    /// <summary>
    /// Название группы; у группы-одиночки пустое.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Цвет обводки группы на сцене в формате #RRGGBB; у группы-одиночки пустой.
    /// </summary>
    public string Color { get; set; } = "";

    /// <summary>
    /// Идентификаторы устройств группы в порядке их появления.
    /// </summary>
    public List<string> DeviceIds { get; set; } = new();

    /// <summary>
    /// Яркость подсветки устройств группы, 0–100 %.
    /// </summary>
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Что играет на устройствах группы.
    /// </summary>
    public ContentConfig DefaultContent { get; set; } = new();

    /// <summary>
    /// Группа-одиночка для устройства без группы.
    /// </summary>
    public static GroupConfig Solo(string deviceId, ContentConfig content, int brightness)
    {
        return new GroupConfig
        {
            Id = NewId(),
            IsSolo = true,
            DeviceIds = new List<string> { deviceId },
            Brightness = brightness,
            DefaultContent = content
        };
    }

    /// <summary>
    /// Новый уникальный идентификатор группы.
    /// </summary>
    public static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

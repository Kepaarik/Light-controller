namespace LightSpace.Core.Config;

/// <summary>
/// Сохранённый набор подсветки: что играло на каждом устройстве и с какой яркостью. Применённый профиль возвращает
/// устройствам это содержимое; устройства, которых в профиле нет, продолжают играть своё.
/// </summary>
public sealed class ProfileConfig
{
    public string Id { get; set; } = "";

    /// <summary>
    /// Название профиля, которое задаёт пользователь.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Яркость и содержимое устройств: по одной группе-одиночке на устройство, как в режиме «Устройства».
    /// </summary>
    public List<GroupConfig> DeviceLighting { get; set; } = new();

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = GroupConfig.NewId();

        Name ??= "";

        DeviceLighting = (DeviceLighting ?? new List<GroupConfig>())
            .Where(g => g?.DeviceIds is { Count: 1 } && !string.IsNullOrWhiteSpace(g.DeviceIds[0]))
            .GroupBy(g => g.DeviceIds[0])
            .Select(g => g.First())
            .ToList();

        foreach (var lighting in DeviceLighting)
        {
            lighting.Brightness = Math.Clamp(lighting.Brightness, 0, 100);
            lighting.DefaultContent ??= new ContentConfig();
            lighting.DefaultContent.Normalize();
        }
    }
}

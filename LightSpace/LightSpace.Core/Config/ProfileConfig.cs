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
    /// Яркость и содержимое устройств: по одной записи на устройство.
    /// </summary>
    public List<DeviceLightingConfig> DeviceLighting { get; set; } = new();

    /// <summary>
    /// Сцена профиля: начинает играть, когда профиль применяют; null — сцены у профиля нет.
    /// </summary>
    public SceneConfig? Scene { get; set; }

    /// <summary>
    /// Новый уникальный идентификатор профиля.
    /// </summary>
    public static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = NewId();

        Name ??= "";

        DeviceLighting = (DeviceLighting ?? new List<DeviceLightingConfig>())
            .Where(l => l != null && !string.IsNullOrWhiteSpace(l.DeviceId))
            .GroupBy(l => l.DeviceId)
            .Select(g => g.First())
            .ToList();

        foreach (var lighting in DeviceLighting)
            lighting.Normalize();

        Scene?.Normalize();
    }
}

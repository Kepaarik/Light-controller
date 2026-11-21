namespace LightSpace.Core.Config;

/// <summary>
/// Подсветка одного устройства: что на нём играет и с какой яркостью.
/// </summary>
public sealed class DeviceLightingConfig
{
    /// <summary>
    /// Устройство, которому принадлежит подсветка.
    /// </summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// Яркость подсветки устройства, 0–100 %.
    /// </summary>
    public int Brightness { get; set; } = 100;

    /// <summary>
    /// Что играет на устройстве.
    /// </summary>
    public ContentConfig Content { get; set; } = new();

    /// <summary>
    /// Подсветка устройства с содержимым content и яркостью brightness.
    /// </summary>
    public static DeviceLightingConfig For(string deviceId, ContentConfig content, int brightness)
    {
        return new DeviceLightingConfig { DeviceId = deviceId, Content = content, Brightness = brightness };
    }

    /// <summary>
    /// Независимая копия: правки копии не меняют оригинал.
    /// </summary>
    public DeviceLightingConfig Clone()
    {
        return For(DeviceId, Content.Clone(), Brightness);
    }

    /// <summary>
    /// Приводит значения к допустимым: файл могли поправить руками.
    /// </summary>
    public void Normalize()
    {
        DeviceId ??= "";
        Brightness = Math.Clamp(Brightness, 0, 100);
        Content ??= new ContentConfig();
        Content.Normalize();
    }
}

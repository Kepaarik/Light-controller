namespace LightSpace.Core.Config;

/// <summary>
/// Известное устройство: как оно называется и где находится, а также его ручные настройки связи.
/// Остаётся в настройках, даже когда устройство не найдено при поиске.
/// </summary>
public sealed class DeviceConfig
{
    /// <summary>
    /// Постоянный идентификатор устройства, который сообщает его драйвер.
    /// </summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>
    /// Способ подключения и тип контроллера, например «BLE · ELK-BLEDOM».
    /// </summary>
    public string Protocol { get; set; } = "";

    /// <summary>
    /// Адрес, по которому устройство нашлось в последний раз: IP, Bluetooth-адрес или COM-порт.
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// Локальный ключ, без которого устройство не принимает команды по сети (Tuya); null — не нужен или не задан.
    /// </summary>
    public string? LocalKey { get; set; }

    /// <summary>
    /// Набор точек данных, которым контроллер описывает своё состояние (у Tuya — «V2» или «V1»); null — по умолчанию.
    /// </summary>
    public string? DataPoints { get; set; }

    /// <summary>
    /// Где устройство стоит на сцене; null — устройство ещё не расставлено.
    /// </summary>
    public DevicePlacement? Placement { get; set; }
}

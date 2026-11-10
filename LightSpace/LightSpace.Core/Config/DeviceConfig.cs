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
    /// Имя, которое дал устройству пользователь; оно важнее имени от драйвера. null — своего имени нет.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Значок устройства в левом меню, выбранный пользователем (символ Segoe Fluent Icons). null — значок по типу устройства.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Подсветка устройства включена. Выключенное устройство не светится, остальные играют как играли.
    /// </summary>
    public bool IsOn { get; set; } = true;

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

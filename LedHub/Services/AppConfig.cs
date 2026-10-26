using LedHub.Core;
using LedHub.Protocols.Tuya;

namespace LedHub.Services;

/// <summary>
/// Пользовательские настройки, сохраняемые между запусками.
/// </summary>
public sealed class AppConfig
{
    public List<GroupConfig> Groups { get; set; } = new();

    /// <summary>
    /// Настройки Tuya-устройств по их Device ID.
    /// </summary>
    public Dictionary<string, TuyaDeviceConfig> Tuya { get; set; } = new();

    /// <summary>
    /// Имена, которые пользователь дал устройствам, по идентификатору устройства.
    /// </summary>
    public Dictionary<string, string> DeviceNames { get; set; } = new();

    /// <summary>
    /// Раскладки из шестигранников по идентификатору устройства.
    /// </summary>
    public Dictionary<string, HexLayoutConfig> HexLayouts { get; set; } = new();
}

/// <summary>
/// Форма, собранная из шестигранников; порядок ячеек — порядок шестигранников в цепочке.
/// </summary>
public sealed class HexLayoutConfig
{
    public List<HexCellConfig> Cells { get; set; } = new();
}

/// <summary>
/// Шестигранник на сетке в осевых координатах (Q, R) и цвета его светодиодов.
/// </summary>
public sealed class HexCellConfig
{
    public const int LedCount = 7;

    public int Q { get; set; }

    public int R { get; set; }

    /// <summary>
    /// Центральный светодиод, затем шесть вокруг по часовой стрелке, начиная с верхнего.
    /// </summary>
    public RgbColor[] Leds { get; set; } = new RgbColor[LedCount];
}

public sealed class GroupConfig
{
    public string Name { get; set; } = "";

    public List<GroupMemberConfig> Members { get; set; } = new();
}

public sealed class GroupMemberConfig
{
    public string Id { get; set; } = "";

    /// <summary>
    /// Имя устройства на момент добавления — чтобы показывать участника, даже когда он не найден.
    /// </summary>
    public string Name { get; set; } = "";
}

public sealed class TuyaDeviceConfig
{
    public string LocalKey { get; set; } = "";

    public TuyaDpScheme DpScheme { get; set; } = TuyaDpScheme.V2;
}

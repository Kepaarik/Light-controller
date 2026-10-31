using System.Text.Json.Serialization;

namespace LightSpace.Core.Config;

/// <summary>
/// Когда условие сценария считается выполненным.
/// </summary>
public enum ConditionMode
{
    /// <summary>Окно приложения на переднем плане.</summary>
    Focused,

    /// <summary>Приложение запущено, неважно, какое окно на переднем плане.</summary>
    Running
}

/// <summary>
/// Условие сценария: приложение и режим «в фокусе» или «запущен». Приложение узнаётся по имени exe-файла без учёта регистра.
/// </summary>
public sealed class ScenarioCondition
{
    /// <summary>
    /// Путь к exe-файлу приложения или только имя файла, если путь неизвестен; пусто — приложение не выбрано.
    /// </summary>
    public string ExePath { get; set; } = "";

    public ConditionMode Mode { get; set; } = ConditionMode.Focused;

    /// <summary>
    /// Имя exe-файла приложения, например dota2.exe; пусто — приложение не выбрано.
    /// </summary>
    [JsonIgnore]
    public string ExeName => Path.GetFileName(ExePath.Trim());

    /// <summary>
    /// Приложение выбрано; без него условие никогда не выполняется.
    /// </summary>
    [JsonIgnore]
    public bool IsSet => ExeName.Length > 0;
}

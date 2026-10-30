using LedHub.Core;
using LedHub.Protocols.Tuya;
using LedHub.Services.Effects;
using LedHub.Services.Games;
using LedHub.Services.Scene;

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

    public List<SceneConfig> Scenes { get; set; } = new();

    /// <summary>
    /// Раскладки адресных лент вокруг монитора для подсветки под экран, по идентификатору устройства.
    /// </summary>
    public Dictionary<string, ScreenLayoutConfig> ScreenLayouts { get; set; } = new();

    /// <summary>
    /// Настройки подсветки по мониторам, по системному имени монитора.
    /// </summary>
    public Dictionary<string, MonitorConfig> Monitors { get; set; } = new();

    /// <summary>
    /// Недавно выбранные цвета в формате #RRGGBB, самый свежий первым.
    /// </summary>
    public List<string> RecentColors { get; set; } = new();

    /// <summary>
    /// Настройки эффектов по идентификатору эффекта.
    /// </summary>
    public Dictionary<string, EffectValues> Effects { get; set; } = new();

    /// <summary>
    /// Расстановка мониторов и устройств на сцене.
    /// </summary>
    public List<SceneItemConfig> SceneItems { get; set; } = new();

    /// <summary>
    /// Окно из прозрачного стекла (Acrylic); иначе — матовое (Mica).
    /// </summary>
    public bool UseAcrylic { get; set; } = true;

    /// <summary>
    /// Тонировка и прозрачность стекла окна.
    /// </summary>
    public GlassConfig Glass { get; set; } = new();

    /// <summary>
    /// Искать ленты сразу при запуске.
    /// </summary>
    public bool ScanOnStartup { get; set; } = true;

    /// <summary>
    /// Список устройств свёрнут в колонку значков.
    /// </summary>
    public bool IsDeviceListCollapsed { get; set; }

    /// <summary>
    /// Главный монитор для режимов по картинке экрана и игр; null — основной монитор Windows.
    /// </summary>
    public string? MainMonitor { get; set; }

    /// <summary>
    /// Игровые режимы: какие игры подсвечивать и чем.
    /// </summary>
    public GamesConfig Games { get; set; } = new();
}

/// <summary>
/// Вид стекла окна; все силы — от 0 до 100.
/// </summary>
public sealed class GlassConfig
{
    /// <summary>
    /// Цвет тонировки в формате #RRGGBB.
    /// </summary>
    public string Tint { get; set; } = "#7C6CFF";

    /// <summary>
    /// Насколько сквозь окно видно то, что под ним.
    /// </summary>
    public double Transparency { get; set; } = 55;

    /// <summary>
    /// Насколько сильно стекло окрашено в цвет тонировки.
    /// </summary>
    public double TintStrength { get; set; } = 30;

    /// <summary>
    /// Плотность карточек: чем больше, тем они матовее и заметнее на стекле.
    /// </summary>
    public double Frost { get; set; } = 30;

    /// <summary>
    /// Яркость блика по краю карточек.
    /// </summary>
    public double Shine { get; set; } = 50;
}

/// <summary>
/// Настройки игровых режимов.
/// </summary>
public sealed class GamesConfig
{
    /// <summary>
    /// Что включать при запуске Dota 2.
    /// </summary>
    public GameProfile Dota { get; set; } = new();

    /// <summary>
    /// Что включать при запуске Counter-Strike 2.
    /// </summary>
    public GameProfile CounterStrike { get; set; } = new();

    /// <summary>
    /// Что включать при запуске любой другой игры на весь главный монитор.
    /// </summary>
    public GameProfile OtherGames { get; set; } = new();

    /// <summary>
    /// Кроме подсветки главного монитора, показывать игру на всех найденных лентах.
    /// </summary>
    public bool UseAllDevices { get; set; }

    /// <summary>
    /// Режим по картинке экрана для игр без своей интеграции — идентификатор эффекта.
    /// </summary>
    public string OtherGamesEffect { get; set; } = "screen-ambient";

    /// <summary>
    /// Показывать игру на клавиатурах, даже если «все ленты» выключены.
    /// </summary>
    public bool UseKeyboards { get; set; } = true;

    /// <summary>
    /// Что светится под полосками и событиями в играх с интеграцией.
    /// </summary>
    public GameBackground Background { get; set; } = GameBackground.Screen;

    /// <summary>
    /// Яркость фона в процентах.
    /// </summary>
    public int BackgroundBrightness { get; set; } = 40;

    /// <summary>
    /// Где на клавиатуре показывать полоски здоровья и маны или брони.
    /// </summary>
    public KeyboardHud KeyboardHud { get; set; } = KeyboardHud.FunctionAndNumberRows;

    /// <summary>
    /// На остальных лентах (не за монитором и не клавиатурах) показывать полоски; иначе — фон и события.
    /// </summary>
    public bool BarsOnOtherDevices { get; set; } = true;

    /// <summary>
    /// События, которые пользователь выключил, — ключи из <see cref="Games.GameTriggers.ConfigKey"/>.
    /// </summary>
    public HashSet<string> DisabledTriggers { get; set; } = new();

    public GameProfile ProfileFor(GameKind kind)
    {
        return kind switch
        {
            GameKind.Dota2 => Dota,
            GameKind.CounterStrike2 => CounterStrike,
            _ => OtherGames
        };
    }
}

/// <summary>
/// Что делать с подсветкой, когда запущена игра.
/// </summary>
public sealed class GameProfile
{
    /// <summary>
    /// Подсвечивать ли эту игру.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Имя пресета, который включается при запуске игры; пустая строка — игровая подсветка.
    /// </summary>
    public string Preset { get; set; } = "";
}

/// <summary>
/// Фон игровой подсветки.
/// </summary>
public enum GameBackground
{
    /// <summary>Картинка экрана выбранным режимом «по картинке монитора».</summary>
    Screen,

    /// <summary>Цвет своей команды.</summary>
    Team,

    /// <summary>Без фона: светятся только полоски и события.</summary>
    Off
}

/// <summary>
/// Где на клавиатуре полоски игры.
/// </summary>
public enum KeyboardHud
{
    /// <summary>Здоровье на F1–F12, мана или броня на цифрах.</summary>
    FunctionAndNumberRows,

    /// <summary>Обе полоски на F1–F12: здоровье слева, мана или броня справа.</summary>
    FunctionRow,

    /// <summary>Без полосок: только фон и события.</summary>
    Off
}

/// <summary>
/// Какая лента подсвечивает монитор и как монитор повёрнут.
/// </summary>
public sealed class MonitorConfig
{
    /// <summary>
    /// Идентификатор ленты; null — монитор без подсветки.
    /// </summary>
    public string? DeviceId { get; set; }

    public MonitorRotation Rotation { get; set; }
}

/// <summary>
/// Сохранённое состояние лент, которое включается одним кликом.
/// </summary>
public sealed class SceneConfig
{
    public string Name { get; set; } = "";

    public List<SceneDeviceState> Devices { get; set; } = new();
}

public sealed class SceneDeviceState
{
    public string DeviceId { get; set; } = "";

    public bool IsOn { get; set; }

    public RgbColor Color { get; set; }

    public int Brightness { get; set; } = 100;
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

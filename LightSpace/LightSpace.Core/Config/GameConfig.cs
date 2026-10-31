namespace LightSpace.Core.Config;

/// <summary>
/// Игра, под которую подстраивается содержимое-игра.
/// </summary>
public enum GameKind
{
    /// <summary>Dota 2: здоровье и мана героя, состояния героя, смерть и конец матча.</summary>
    Dota2,

    /// <summary>Counter-Strike 2: здоровье и броня, ослепление, горение, бомба и конец раунда.</summary>
    CounterStrike2,

    /// <summary>Любая другая игра без своей интеграции: свет повторяет картинку монитора.</summary>
    Other
}

/// <summary>
/// Что светится под полосками и событиями в играх с интеграцией.
/// </summary>
public enum GameBackground
{
    /// <summary>Картинка монитора под местом светодиода на сцене.</summary>
    Screen,

    /// <summary>Цвет своей команды.</summary>
    Team,

    /// <summary>Без фона: светятся только полоски и события.</summary>
    Off
}

/// <summary>
/// Настройки содержимого-игры: какая игра, что светится фоном, где показывать полоски здоровья и маны или брони
/// и какие события игры выключены.
/// </summary>
public sealed class GameConfig
{
    /// <summary>
    /// Яркость фона по умолчанию, в процентах.
    /// </summary>
    public const int DefaultBackgroundBrightness = 40;

    public GameKind Game { get; set; } = GameKind.Dota2;

    public GameBackground Background { get; set; } = GameBackground.Team;

    /// <summary>
    /// Яркость фона, 0–100 %.
    /// </summary>
    public int BackgroundBrightness { get; set; } = DefaultBackgroundBrightness;

    /// <summary>
    /// Системное имя монитора, чья картинка светится фоном «Картинка экрана» и в других играх; пусто — основной
    /// монитор Windows. Если такого монитора сейчас нет, картинка берётся с основного.
    /// </summary>
    public string MonitorKey { get; set; } = "";

    /// <summary>
    /// Полоски на клавиатуре: здоровье на верхней половине рядов, мана или броня на нижней. Иначе клавиатура
    /// светится фоном и событиями.
    /// </summary>
    public bool KeyboardBars { get; set; } = true;

    /// <summary>
    /// Полоски на лентах: первая половина ленты — здоровье, вторая — мана или броня; устройства одного цвета
    /// светятся цветом здоровья. Иначе ленты и устройства одного цвета светятся фоном и событиями.
    /// </summary>
    public bool StripBars { get; set; } = true;

    /// <summary>
    /// События, которые пользователь выключил, — ключи вида «Игра:событие», например «Dota2:damage».
    /// У каждой игры свои ключи, поэтому выбор сохраняется, когда переключают игру.
    /// </summary>
    public List<string> DisabledEvents { get; set; } = new();

    /// <summary>
    /// Ключ события игры в <see cref="DisabledEvents"/>.
    /// </summary>
    public static string EventKey(GameKind game, string eventKey)
    {
        return $"{game}:{eventKey}";
    }

    /// <summary>
    /// Независимая копия настроек: правки копии не меняют оригинал.
    /// </summary>
    public GameConfig Clone()
    {
        return new GameConfig
        {
            Game = Game,
            Background = Background,
            BackgroundBrightness = BackgroundBrightness,
            MonitorKey = MonitorKey,
            KeyboardBars = KeyboardBars,
            StripBars = StripBars,
            DisabledEvents = new List<string>(DisabledEvents)
        };
    }

    /// <summary>
    /// Приводит настройки к допустимым: файл могли поправить руками или записать в другой версии.
    /// </summary>
    public void Normalize()
    {
        MonitorKey ??= "";

        if (!Enum.IsDefined(Game))
            Game = GameKind.Dota2;

        if (!Enum.IsDefined(Background))
            Background = GameBackground.Team;

        BackgroundBrightness = Math.Clamp(BackgroundBrightness, 0, 100);

        // Пустые ключи и повторы ничего не выключают
        DisabledEvents = (DisabledEvents ?? new List<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct()
            .ToList();
    }
}

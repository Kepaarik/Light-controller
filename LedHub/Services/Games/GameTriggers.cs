namespace LedHub.Services.Games;

/// <summary>
/// Событие игры, которое подсветка показывает вспышкой или своим цветом; его можно выключить.
/// </summary>
public sealed record GameTrigger(string Key, string Title, string Description);

/// <summary>
/// События, которые умеет показывать подсветка в играх с интеграцией.
/// </summary>
public static class GameTriggers
{
    public const string Damage = "damage";
    public const string LowHealth = "low-health";
    public const string Death = "death";
    public const string Victory = "victory";
    public const string HeroSelection = "hero-selection";
    public const string Status = "status";
    public const string Flash = "flash";
    public const string Burn = "burn";
    public const string Bomb = "bomb";

    public static IReadOnlyList<GameTrigger> Dota { get; } = new[]
    {
        new GameTrigger(Damage, "Урон", "Красная вспышка, когда здоровье резко падает"),
        new GameTrigger(LowHealth, "Мало здоровья", "Тревожная пульсация, когда здоровья меньше четверти"),
        new GameTrigger(Status, "Состояния героя", "Оглушение — белое мерцание, превращение, немота и дымка — свой оттенок"),
        new GameTrigger(HeroSelection, "Выбор героя", "Мигание золотым, пока идёт выбор"),
        new GameTrigger(Death, "Смерть", "Тусклый красный и белое мигание перед возрождением"),
        new GameTrigger(Victory, "Конец матча", "Победа — радуга, поражение — тусклый красный")
    };

    public static IReadOnlyList<GameTrigger> CounterStrike { get; } = new[]
    {
        new GameTrigger(Damage, "Урон", "Красная вспышка, когда здоровье резко падает"),
        new GameTrigger(Flash, "Ослепление", "Белый свет, пока ослеплён"),
        new GameTrigger(Burn, "Горение", "Оранжевое мерцание в огне"),
        new GameTrigger(Bomb, "Бомба", "Красный писк всё чаще, взрыв и разминирование — вспышка"),
        new GameTrigger(Victory, "Конец раунда", "Победа — радуга, поражение — тусклый красный")
    };

    /// <summary>
    /// Ключ события в настройках: у каждой игры свои, хотя «урон» есть в обеих.
    /// </summary>
    public static string ConfigKey(GameKind kind, string key)
    {
        return $"{kind}:{key}";
    }
}

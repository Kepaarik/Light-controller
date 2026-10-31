using LightSpace.Core.Config;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Событие игры, которое свет показывает вспышкой или своим цветом; его можно выключить.
/// </summary>
/// <param name="Key">Ключ события, одинаковый в разных играх, например «урон».</param>
public sealed record GameEvent(string Key, string Title, string Description);

/// <summary>
/// События, которые умеет показывать свет в играх с интеграцией.
/// </summary>
public static class GameEvents
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

    public static IReadOnlyList<GameEvent> Dota { get; } = new[]
    {
        new GameEvent(Damage, "Урон", "Красная вспышка, когда здоровье резко падает"),
        new GameEvent(LowHealth, "Мало здоровья", "Тревожная пульсация, когда здоровья меньше четверти"),
        new GameEvent(Status, "Состояния героя", "Оглушение — белое мерцание, превращение, немота и дымка — свой оттенок"),
        new GameEvent(HeroSelection, "Выбор героя", "Мигание золотым, пока идёт выбор"),
        new GameEvent(Death, "Смерть", "Тусклый красный и белое мигание перед возрождением"),
        new GameEvent(Victory, "Конец матча", "Победа — радуга, поражение — тусклый красный")
    };

    public static IReadOnlyList<GameEvent> CounterStrike { get; } = new[]
    {
        new GameEvent(Damage, "Урон", "Красная вспышка, когда здоровье резко падает"),
        new GameEvent(Flash, "Ослепление", "Белый свет, пока ослеплён"),
        new GameEvent(Burn, "Горение", "Оранжевое мерцание в огне"),
        new GameEvent(Bomb, "Бомба", "Красный писк всё чаще, взрыв и разминирование — вспышка"),
        new GameEvent(Victory, "Конец раунда", "Победа — радуга, поражение — тусклый красный")
    };

    /// <summary>
    /// События игры; у игр без интеграции событий нет.
    /// </summary>
    public static IReadOnlyList<GameEvent> Of(GameKind game)
    {
        return game switch
        {
            GameKind.Dota2 => Dota,
            GameKind.CounterStrike2 => CounterStrike,
            _ => Array.Empty<GameEvent>()
        };
    }
}

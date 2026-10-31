namespace LightSpace.Engine.Sources;

/// <summary>
/// Что Dota 2 сообщила о матче и своём герое в последний раз.
/// </summary>
/// <param name="GameState">Стадия матча, например <see cref="InProgress"/>; пусто — игра в меню.</param>
/// <param name="Health">Здоровье героя, 0–100 %.</param>
/// <param name="Mana">Мана героя, 0–100 %.</param>
/// <param name="ReceivedAt">Когда пришло это состояние, по UTC.</param>
public sealed record DotaState(
    string GameState,
    bool IsDaytime,
    string WinTeam,
    string Team,
    bool HasHero,
    bool IsAlive,
    int Health,
    int Mana,
    int RespawnSeconds,
    bool IsStunned,
    bool IsSilenced,
    bool IsHexed,
    bool IsSmoked,
    DateTime ReceivedAt)
{
    public const string HeroSelection = "DOTA_GAMERULES_STATE_HERO_SELECTION";
    public const string StrategyTime = "DOTA_GAMERULES_STATE_STRATEGY_TIME";
    public const string PreGame = "DOTA_GAMERULES_STATE_PRE_GAME";
    public const string InProgress = "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS";
    public const string PostGame = "DOTA_GAMERULES_STATE_POST_GAME";
}

/// <summary>
/// Что Counter-Strike 2 сообщила о раунде и игроке в последний раз.
/// </summary>
/// <param name="IsOwnPlayer">Состояние своего игрока; иначе — того, за кем он смотрит после смерти.</param>
/// <param name="Health">Здоровье, 0–100.</param>
/// <param name="Armor">Броня, 0–100.</param>
/// <param name="Flashed">Сила ослепления, 0–255.</param>
/// <param name="Burning">Сила горения, 0–255.</param>
/// <param name="ReceivedAt">Когда пришло это состояние, по UTC.</param>
public sealed record CsState(
    bool IsOwnPlayer,
    string Team,
    int Health,
    int Armor,
    int Flashed,
    int Burning,
    string MapPhase,
    string RoundPhase,
    string Bomb,
    string WinTeam,
    DateTime ReceivedAt);

/// <summary>
/// Последнее, что прислали игры: состояние матча Dota 2 и раунда Counter-Strike 2.
/// </summary>
/// <param name="Dota">Последнее состояние Dota 2; null — игра ещё ничего не присылала.</param>
/// <param name="CounterStrike">Последнее состояние Counter-Strike 2; null — игра ещё ничего не присылала.</param>
public sealed record GameState(DotaState? Dota, CsState? CounterStrike)
{
    /// <summary>
    /// Игры ничего не присылали.
    /// </summary>
    public static readonly GameState None = new(null, null);
}

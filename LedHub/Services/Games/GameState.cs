namespace LedHub.Services.Games;

/// <summary>
/// Игра, под которую подстраивается подсветка.
/// </summary>
public enum GameKind
{
    Dota2,
    CounterStrike2,

    /// <summary>Любая другая игра на весь главный монитор: подсветка по картинке экрана.</summary>
    Other
}

/// <summary>
/// Запущенная игра: её вид, название для показа и процесс.
/// </summary>
public sealed record DetectedGame(GameKind Kind, string Title, int ProcessId);

/// <summary>
/// Что Dota 2 сообщила о матче и своём герое в последний раз.
/// </summary>
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

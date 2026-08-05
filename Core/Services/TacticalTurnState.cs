using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

public enum SimulationMode
{
    RealTime,
    TurnBased
}

public enum TacticalPhase
{
    Player,
    PlayerEffects,
    EnemyActions,
    WorldUpkeep
}

public enum TacticalIntentKind
{
    Attack,
    Advance,
    Reposition,
    Hold
}

public sealed record TacticalEnemyIntent(
    int Order,
    Enemy Actor,
    string ActorName,
    TacticalIntentKind Kind,
    int FromX,
    int FromY,
    int? TargetX,
    int? TargetY,
    string Detail);

public sealed record TacticalAttackPreview(
    string AttackName,
    int TargetX,
    int TargetY,
    int ImpactX,
    int ImpactY,
    float Range,
    float AreaRadius,
    bool TargetWalkable,
    bool InRange,
    bool HasLineOfSight,
    bool CanAfford,
    bool CanCommit,
    int AffectedEnemyCount,
    string Status,
    IReadOnlyList<(int x, int y)> RangeCells,
    IReadOnlyList<(int x, int y)> AffectedCells);

/// <summary>
/// Read-only-to-callers action economy for the optional tactical mode. GameState owns all
/// mutations so a frontend cannot spend movement or actions without performing a valid command.
/// </summary>
public sealed class TacticalTurnState
{
    public int TurnNumber { get; internal set; }
    public int MovementAllowance { get; internal set; }
    public int MovementRemaining { get; internal set; }
    public bool ActionAvailable { get; internal set; }
    public bool BonusActionAvailable { get; internal set; }
    public bool IsPlayerTurn { get; internal set; }
    public int ResolutionTicksRemaining { get; internal set; }
    public TacticalPhase Phase { get; internal set; } = TacticalPhase.Player;
    public int EnemyActionsTotal { get; internal set; }
    public int EnemyActionsRemaining { get; internal set; }
    public string ActiveEnemy { get; internal set; } = "";
    public string LastEnemyAction { get; internal set; } = "";
    internal List<TacticalEnemyIntent> MutableEnemyIntents { get; } = new();
    public IReadOnlyList<TacticalEnemyIntent> EnemyIntents => MutableEnemyIntents;

    public int MovementSpent => Math.Max(0, MovementAllowance - MovementRemaining);
}

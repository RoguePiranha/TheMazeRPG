using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Systems;

namespace TheMazeRPG.Core.Services
{

/// <summary>
/// How the hero is driven. <see cref="Auto"/> is the default in the dungeon: the hero auto-explores
/// and auto-fights (the original screensaver-style behavior). <see cref="Manual"/> hands movement to
/// the player (attacks still auto-fire at engaged enemies) — the two are toggled live, never removing
/// the auto-run option. The Overworld is always player-driven (Manual): the town is entered in Manual
/// and structures are used via the Press-E interaction, not an auto script.
/// </summary>
public enum ControlMode
{
    Auto,
    Manual
}

/// <summary>
/// Manages the current game state and simulation
/// </summary>
public class GameState
{

    // Shared vision cone parameters (used for all entities)
    public float VisionConeAngleRad { get; set; } = MathF.PI / 2; // 90 degrees
    public float VisionRange { get; set; } = 7.5f; // default vision range in tiles

    // Attack cone/range are per-attack/class and used only for combat checks

    public Hero Hero { get; set; }
    public Maze CurrentMaze { get; set; } = null!;
    public List<Enemy> Enemies { get; set; } = new();
    /// <summary>The current gate Guardian once spawned in a safe room (also used for the balance
    /// dump). Null on regular floors — regular floors no longer have an embedded boss.</summary>
    public Enemy? Boss { get; set; }
    public (int x, int y)? StairsLocation { get; set; }

    /// <summary>True while the hero is in the interstitial safe room just before a Guardian
    /// floor (e.g. 4.5, before floor 5) — see GameState.EnterSafeRoom.</summary>
    public bool IsInSafeRoom { get; private set; }
    // Guardian floors are every 5th floor (5, 10, 15, ...); the safe room sits just before each
    // one (4.5, 9.5, 14.5, ...). The Guardian fight itself IS the 5th/10th/... floor — beating
    // it advances to floor 6/11/....
    private const int GuardianFloorInterval = 5;

    /// <summary>True while the hero is in the persistent Overworld (the Starting Region town)
    /// rather than the dungeon. Set by EnterOverworld/StartFreshDungeonDive.</summary>
    public bool IsInOverworld { get; private set; }

    /// <summary>Auto-run vs. player-driven movement — see ControlMode. Defaults to Auto; a session
    /// preference, not saved. Forced to Manual while in the Overworld (the town is player-driven).</summary>
    public ControlMode ControlMode { get; private set; } = ControlMode.Auto;

    /// <summary>The optional tactical clock is player-controlled and never activates implicitly.</summary>
    public SimulationMode SimulationMode { get; private set; } = SimulationMode.RealTime;
    public TacticalTurnState TacticalTurn { get; } = new();
    public const int TacticalMovementCap = 8;
    private ControlMode _controlModeBeforeTactical = ControlMode.Manual;
    private readonly Queue<Enemy> _tacticalEnemyQueue = new();
    private Enemy? _tacticalMovingEnemy;
    private float _tacticalMoveStartX;
    private float _tacticalMoveStartY;
    private float _tacticalMoveTargetX;
    private float _tacticalMoveTargetY;
    private int _tacticalMoveTicksRemaining;
    private int _tacticalActionDelayTicks;
    private int _tacticalEffectSafetyTicks;
    private const int TacticalMoveAnimationTicks = 5;
    private const int TacticalActionDelayTicks = 3;
    private const int TacticalEffectSafetyLimit = 90;

    /// <summary>The nearby chest or town structure available through the Press-E interaction.</summary>
    public MazeFeature? NearbyInteractable { get; private set; }

    // Player movement intent for Manual mode, set by the UI from held keys (each component in
    // [-1,1]); consumed once per Tick. Ignored entirely in Auto mode.
    private float _manualMoveX;
    private float _manualMoveY;
    // Last non-zero move direction (for dashing when standing still); defaults to facing right.
    private float _lastMoveDirX = 1f;
    private float _lastMoveDirY = 0f;

    // Active dodge (Space): a brief fast dash with invulnerability, then a cooldown.
    private float _dashDirX, _dashDirY;
    private int _dashTicksRemaining;
    private int _dashCooldownRemaining;
    private readonly int _dashTicks;          // dash duration
    private readonly int _dashCooldownTicks;  // cooldown after the dash ends
    private const float DashSpeed = 0.26f;    // tiles/tick during a dash (vs ~0.09 normal manual)

    /// <summary>True during a dash — the hero shrugs off incoming hits (i-frames).</summary>
    public bool IsHeroInvulnerable => _dashTicksRemaining > 0;

    // Brief "!" alert over the hero, raised when they suddenly notice something (spotting a trap).
    private int _heroAlertTicks;
    private readonly int _alertTicks;
    /// <summary>True while the "!" awareness alert should render over the hero.</summary>
    public bool HeroAlertActive => _heroAlertTicks > 0;

    /// <summary>0..1 dash cooldown progress for the HUD (1 = ready, 0 = just used).</summary>
    public float DashReadyFraction => _dashCooldownTicks <= 0 ? 1f
        : 1f - Math.Clamp(_dashCooldownRemaining / (float)_dashCooldownTicks, 0f, 1f);

    /// <summary>Set the player's desired movement direction (Manual mode). Called by the UI when
    /// held movement keys change.</summary>
    public void SetManualMoveIntent(float x, float y)
    {
        _manualMoveX = x;
        _manualMoveY = y;
        if (x != 0f || y != 0f)
        {
            _lastMoveDirX = x;
            _lastMoveDirY = y;
        }
    }

    /// <summary>Begin a dodge dash in the current move direction (or last-faced if standing still).
    /// No-op outside Manual mode, while dead/paused/already dashing, or on cooldown.</summary>
    public void TryDash()
    {
        if (ControlMode != ControlMode.Manual || IsHeroDead || !IsRunning) return;
        if (_dashTicksRemaining > 0 || _dashCooldownRemaining > 0) return;

        float dx = _manualMoveX != 0f || _manualMoveY != 0f ? _manualMoveX : _lastMoveDirX;
        float dy = _manualMoveX != 0f || _manualMoveY != 0f ? _manualMoveY : _lastMoveDirY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) { dx = _lastMoveDirX; dy = _lastMoveDirY; len = MathF.Sqrt(dx * dx + dy * dy); }
        if (len < 0.01f) return;

        _dashDirX = dx / len;
        _dashDirY = dy / len;
        _dashTicksRemaining = _dashTicks;
        LogMessage("Dodge!", MessageKind.System);
    }

    public void SetControlMode(ControlMode mode)
    {
        ControlMode = mode;
        if (mode == ControlMode.Auto)
        {
            _manualMoveX = 0;
            _manualMoveY = 0;
        }
    }

    public void ToggleControlMode() =>
        SetControlMode(ControlMode == ControlMode.Auto ? ControlMode.Manual : ControlMode.Auto);

    public void SetSimulationMode(SimulationMode mode)
    {
        if (SimulationMode == mode) return;

        _manualMoveX = 0f;
        _manualMoveY = 0f;
        _dashTicksRemaining = 0;

        if (mode == SimulationMode.TurnBased)
        {
            _controlModeBeforeTactical = ControlMode;
            SimulationMode = mode;
            SetControlMode(ControlMode.Manual);
            Hero.X = Hero.GridX;
            Hero.Y = Hero.GridY;
            foreach (Enemy enemy in Enemies.Where(enemy => enemy.IsAlive))
            {
                enemy.X = MathF.Round(enemy.X);
                enemy.Y = MathF.Round(enemy.Y);
            }
            ResetTacticalResolution();
            TacticalTurn.TurnNumber = 0;
            BeginTacticalPlayerTurn();
            LogMessage("Turn-based mode engaged.", MessageKind.System);
            return;
        }

        SimulationMode = mode;
        TacticalTurn.IsPlayerTurn = false;
        TacticalTurn.ResolutionTicksRemaining = 0;
        TacticalTurn.MovementRemaining = 0;
        TacticalTurn.ActionAvailable = false;
        TacticalTurn.BonusActionAvailable = false;
        ResetTacticalResolution();
        SetControlMode(IsInOverworld ? ControlMode.Manual : _controlModeBeforeTactical);
        LogMessage("Real-time mode resumed.", MessageKind.System);
    }

    public void ToggleSimulationMode() =>
        SetSimulationMode(SimulationMode == SimulationMode.RealTime
            ? SimulationMode.TurnBased
            : SimulationMode.RealTime);

    /// <summary>Agility grants a modest tactical movement increase without allowing extreme
    /// stat values to dominate the whole map.</summary>
    public int CalculateTacticalMovementAllowance()
    {
        int agilityBonus = (int)MathF.Floor(MathF.Max(0f, Hero.EffectiveAgility - 1f) / 3f);
        return Math.Clamp(3 + agilityBonus, 3, TacticalMovementCap);
    }

    /// <summary>Spend one movement point to enter an adjacent cardinal tile.</summary>
    public bool TryTacticalMove(int dx, int dy)
    {
        if (!CanTakeTacticalPlayerAction() || TacticalTurn.MovementRemaining <= 0) return false;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;

        int targetX = Hero.GridX + dx;
        int targetY = Hero.GridY + dy;
        if (IsEnemyOccupyingCell(targetX, targetY)) return false;

        // Bump-to-open: stepping into a closed door spends the movement point opening it; the
        // hero comes through on a later step. Locked doors (and walls) simply refuse.
        if (!CurrentMaze.IsWalkable(targetX, targetY))
        {
            if (!CurrentMaze.TryOpenDoor(targetX, targetY)) return false;
            _lastMoveDirX = dx;
            _lastMoveDirY = dy;
            TacticalTurn.MovementRemaining--;
            RefreshTacticalIntentPreview(rememberObserved: true);
            UpdateNearbyInteractable();
            EndTacticalTurnIfSpent();
            return true;
        }

        Hero.X = targetX;
        Hero.Y = targetY;
        _lastMoveDirX = dx;
        _lastMoveDirY = dy;
        CurrentMaze.Explored[targetX, targetY] = true;
        TacticalTurn.MovementRemaining--;
        RefreshTacticalIntentPreview(rememberObserved: true);
        UpdateNearbyInteractable();
        EndTacticalTurnIfSpent();
        return true;
    }

    /// <summary>Return the shortest currently legal tactical path to an empty destination. The
    /// hero's starting cell is omitted, so the result count is also the movement cost.</summary>
    public IReadOnlyList<(int x, int y)> GetTacticalPathTo(int targetX, int targetY)
    {
        if (!CanTakeTacticalPlayerAction() || TacticalTurn.MovementRemaining <= 0)
            return Array.Empty<(int x, int y)>();

        var start = (x: Hero.GridX, y: Hero.GridY);
        var target = (x: targetX, y: targetY);
        if (target == start || !CurrentMaze.IsWalkable(targetX, targetY) ||
            IsEnemyOccupyingCell(targetX, targetY))
            return Array.Empty<(int x, int y)>();

        var queue = new Queue<(int x, int y)>();
        var previous = new Dictionary<(int x, int y), (int x, int y)> { [start] = start };
        var distance = new Dictionary<(int x, int y), int> { [start] = 0 };
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) break;
            if (distance[current] >= TacticalTurn.MovementRemaining) continue;

            foreach (var next in CardinalCells(current.x, current.y))
            {
                // Traversable, not walkable: a route may pass through a closed door, which the
                // mover opens on arrival (see MovementSystem's bump-to-open).
                if (previous.ContainsKey(next) || !CurrentMaze.IsTraversable(next.x, next.y) ||
                    IsEnemyOccupyingCell(next.x, next.y))
                    continue;
                previous[next] = current;
                distance[next] = distance[current] + 1;
                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(target)) return Array.Empty<(int x, int y)>();
        var path = new List<(int x, int y)>();
        for (var cursor = target; cursor != start; cursor = previous[cursor])
            path.Add(cursor);
        path.Reverse();
        return path;
    }

    /// <summary>Traverse a previewed tactical path as one destination command, spending one
    /// movement point per cell and preserving every normal movement-side effect.</summary>
    public bool TryTacticalMoveTo(int targetX, int targetY)
    {
        IReadOnlyList<(int x, int y)> path = GetTacticalPathTo(targetX, targetY);
        if (path.Count == 0) return false;

        int previousX = Hero.GridX;
        int previousY = Hero.GridY;
        foreach (var cell in path)
        {
            _lastMoveDirX = cell.x - previousX;
            _lastMoveDirY = cell.y - previousY;

            // The route may pass through a closed door; opening it consumes this step
            // (bump-to-open) and ends the command there — the hero stands at the threshold
            // rather than ghosting through a door that stays shut behind them.
            if (CurrentMaze.TryOpenDoor(cell.x, cell.y))
            {
                TacticalTurn.MovementRemaining--;
                RefreshTacticalIntentPreview(rememberObserved: true);
                break;
            }

            Hero.X = cell.x;
            Hero.Y = cell.y;
            CurrentMaze.Explored[cell.x, cell.y] = true;
            previousX = cell.x;
            previousY = cell.y;
            TacticalTurn.MovementRemaining--;
            RefreshTacticalIntentPreview(rememberObserved: true);
        }

        UpdateNearbyInteractable();
        EndTacticalTurnIfSpent();
        return true;
    }

    public TacticalAttackPreview GetTacticalAttackPreview(int targetX, int targetY)
    {
        Attack attack = Hero.CurrentAttack ?? new Attack
        {
            Name = "Unarmed Strike",
            Range = 1f,
            Animation = AttackAnimation.Melee
        };
        bool sameCell = targetX == Hero.GridX && targetY == Hero.GridY;
        bool targetWalkable = CurrentMaze.IsWalkable(targetX, targetY);
        Enemy? targetEnemy = Enemies.FirstOrDefault(enemy => enemy.IsAlive &&
            (int)MathF.Round(enemy.X) == targetX && (int)MathF.Round(enemy.Y) == targetY);
        float centerDistance = Distance(Hero.X, Hero.Y, targetX, targetY);
        float effectiveDistance = MathF.Max(0f, centerDistance - (targetEnemy?.Radius ?? 0f));
        bool inRange = !sameCell && effectiveDistance <= attack.Range + 0.01f;
        bool hasLineOfSight = targetWalkable && HasLineOfSight(Hero.X, Hero.Y, targetX, targetY);
        bool canAfford = CanAffordCurrentAttack();
        bool canControl = SimulationMode == SimulationMode.TurnBased && TacticalTurn.IsPlayerTurn &&
            IsRunning && !IsHeroDead && CurrentActivity == null;

        string status = !canControl ? "UNAVAILABLE"
            : !TacticalTurn.ActionAvailable ? "ACTION SPENT"
            : !canAfford ? "NO RESOURCES"
            : sameCell ? "SELECT TARGET"
            : !targetWalkable ? "INVALID TARGET"
            : !inRange ? "OUT OF RANGE"
            : !hasLineOfSight ? "BLOCKED"
            : "READY";
        bool canCommit = status == "READY";
        (int x, int y) impact = canCommit
            ? (targetX, targetY)
            : FindTacticalAttackImpact(targetX, targetY, attack.Range);

        var rangeCells = new List<(int x, int y)>();
        int rangeExtent = (int)MathF.Ceiling(attack.Range);
        for (int x = Hero.GridX - rangeExtent; x <= Hero.GridX + rangeExtent; x++)
        {
            for (int y = Hero.GridY - rangeExtent; y <= Hero.GridY + rangeExtent; y++)
            {
                if ((x != Hero.GridX || y != Hero.GridY) && CurrentMaze.IsWalkable(x, y) &&
                    Distance(Hero.X, Hero.Y, x, y) <= attack.Range + 0.01f)
                    rangeCells.Add((x, y));
            }
        }

        float areaRadius = attack.Id == "arcane-blast" ? 1.75f : 0f;
        var affectedCells = new List<(int x, int y)>();
        int affectedEnemies = 0;
        if (targetWalkable && areaRadius > 0f)
        {
            int areaExtent = (int)MathF.Ceiling(areaRadius);
            for (int x = targetX - areaExtent; x <= targetX + areaExtent; x++)
            {
                for (int y = targetY - areaExtent; y <= targetY + areaExtent; y++)
                {
                    if (CurrentMaze.IsWalkable(x, y) &&
                        Distance(targetX, targetY, x, y) <= areaRadius + 0.01f)
                        affectedCells.Add((x, y));
                }
            }
            affectedEnemies = Enemies.Count(enemy => enemy.IsAlive &&
                Distance(targetX, targetY, enemy.X, enemy.Y) <= areaRadius + enemy.Radius);
        }

        return new TacticalAttackPreview(attack.Name, targetX, targetY, impact.x, impact.y,
            attack.Range, areaRadius, targetWalkable, inRange, hasLineOfSight, canAfford,
            canCommit, affectedEnemies, status, rangeCells, affectedCells);
    }

    public bool TryTacticalAttackAt(int targetX, int targetY)
    {
        TacticalAttackPreview preview = GetTacticalAttackPreview(targetX, targetY);
        if (!preview.CanCommit) return false;

        _combatSystem.PerformHeroTacticalAttack(Hero, targetX, targetY, Projectiles);
        TacticalTurn.ActionAvailable = false;
        EndTacticalTurnIfSpent();
        return true;
    }

    /// <summary>Compatibility directional command for non-mouse frontends. Direction is projected
    /// to the furthest cell within the current attack's authored tactical range.</summary>
    public bool TryTacticalAttack(float dirX, float dirY)
    {
        float length = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (length < 0.001f) return false;
        float range = Hero.CurrentAttack?.Range ?? 1f;
        int targetX = (int)MathF.Round(Hero.X + dirX / length * range);
        int targetY = (int)MathF.Round(Hero.Y + dirY / length * range);
        return TryTacticalAttackAt(targetX, targetY);
    }

    private (int x, int y) FindTacticalAttackImpact(int targetX, int targetY, float range)
    {
        var impact = (x: Hero.GridX, y: Hero.GridY);
        foreach (var cell in GridLineCells(Hero.GridX, Hero.GridY, targetX, targetY).Skip(1))
        {
            if (Distance(Hero.X, Hero.Y, cell.x, cell.y) > range + 0.01f ||
                !CurrentMaze.IsWalkable(cell.x, cell.y))
                break;
            impact = cell;
        }
        return impact;
    }

    private static IEnumerable<(int x, int y)> GridLineCells(int startX, int startY,
        int endX, int endY)
    {
        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        int error = dx - dy;
        int x = startX;
        int y = startY;
        while (true)
        {
            yield return (x, y);
            if (x == endX && y == endY) yield break;
            int doubled = error * 2;
            if (doubled > -dy) { error -= dy; x += sx; }
            if (doubled < dx) { error += dx; y += sy; }
        }
    }

    /// <summary>Spend the primary action on a short two-cell cardinal dash. Normal movement is
    /// left untouched, making dash a positioning trade for the attack action.</summary>
    public bool TryTacticalDash(int dx, int dy)
    {
        if (!CanTakeTacticalPlayerAction() || !TacticalTurn.ActionAvailable) return false;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;

        int moved = 0;
        for (int i = 0; i < 2; i++)
        {
            int targetX = Hero.GridX + dx;
            int targetY = Hero.GridY + dy;
            if (!CurrentMaze.IsWalkable(targetX, targetY) || IsEnemyOccupyingCell(targetX, targetY))
                break;
            Hero.X = targetX;
            Hero.Y = targetY;
            CurrentMaze.Explored[targetX, targetY] = true;
            moved++;
        }

        if (moved == 0) return false;
        _lastMoveDirX = dx;
        _lastMoveDirY = dy;
        TacticalTurn.ActionAvailable = false;
        LogMessage("Dashed.", MessageKind.System);
        RefreshTacticalIntentPreview(rememberObserved: true);
        UpdateNearbyInteractable();
        EndTacticalTurnIfSpent();
        return true;
    }

    /// <summary>Consume a small item without spending the primary action.</summary>
    public bool TryUseTacticalBonusItem(Item item)
    {
        if (!CanTakeTacticalPlayerAction() || !TacticalTurn.BonusActionAvailable) return false;
        if (!Hero.Inventory.Contains(item)) return false;
        if (!ApplyConsumableEffect(item)) return false;

        if (item.Consumable)
            Hero.Inventory.Remove(item);
        TacticalTurn.BonusActionAvailable = false;
        EndTacticalTurnIfSpent();
        return true;
    }

    /// <summary>Apply a consumable's effect (rarity-scaled — see RarityScaling.Effect). Returns
    /// false when it would do nothing (e.g. drinking a health potion at full HP), so the item
    /// isn't wasted. Shared by tactical bonus-item use and real-time hotbar use.</summary>
    private bool ApplyConsumableEffect(Item item)
    {
        switch (item.UseEffect)
        {
            case ItemUseEffect.RestoreHealth:
                if (Hero.CurrentHp >= Hero.MaxHp) return false;
                int amount = RarityScaling.ScaleEffect(item.EffectPower, item.Rarity);
                int restored = Math.Min(amount, Hero.MaxHp - Hero.CurrentHp);
                Hero.CurrentHp += restored;
                LogMessage($"Drank {item.Name} (+{restored} HP).", MessageKind.Loot);
                AddFloatingText($"+{restored}", Hero.X, Hero.Y - 0.4f, FloatingTextKind.Heal);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Use a consumable in real time (the hotbar path). No turn-economy gating — just
    /// alive, running, and carrying it.</summary>
    public bool UseConsumable(Item item)
    {
        if (IsHeroDead || !IsRunning) return false;
        if (!Hero.Inventory.Contains(item)) return false;
        if (!ApplyConsumableEffect(item)) return false;
        if (item.Consumable) Hero.Inventory.Remove(item);
        return true;
    }

    /// <summary>Finish the player phase. Pending player effects resolve first, then each engaged
    /// enemy receives exactly one ordered decision.</summary>
    public bool EndTacticalTurn()
    {
        if (SimulationMode != SimulationMode.TurnBased || !TacticalTurn.IsPlayerTurn ||
            !IsRunning || IsHeroDead)
            return false;

        _manualMoveX = 0f;
        _manualMoveY = 0f;
        ResetTacticalResolution();
        TacticalTurn.IsPlayerTurn = false;
        TacticalTurn.Phase = TacticalPhase.PlayerEffects;
        TacticalTurn.ResolutionTicksRemaining = 1;
        TacticalTurn.LastEnemyAction = Projectiles.Any(projectile => projectile.IsActive)
            ? "Resolving player action"
            : "Enemies assess the field";
        return true;
    }

    public bool AdvanceTacticalTurnTick()
    {
        if (SimulationMode != SimulationMode.TurnBased || TacticalTurn.IsPlayerTurn ||
            TacticalTurn.ResolutionTicksRemaining <= 0)
            return false;

        switch (TacticalTurn.Phase)
        {
            case TacticalPhase.PlayerEffects:
                if (Projectiles.Any(projectile => projectile.IsActive) &&
                    _tacticalEffectSafetyTicks < TacticalEffectSafetyLimit)
                {
                    _tacticalEffectSafetyTicks++;
                    AdvanceTacticalEffectsTick();
                    return FinishTacticalDeathIfNeeded();
                }

                PrepareTacticalEnemyPhase();
                if (_tacticalEnemyQueue.Count == 0)
                    FinishTacticalWorldPhase();
                return true;

            case TacticalPhase.EnemyActions:
                if (_tacticalMovingEnemy != null)
                {
                    AdvanceTacticalEnemyMotion();
                    AdvanceTacticalEffectsTick();
                    return FinishTacticalDeathIfNeeded();
                }

                if (Projectiles.Any(projectile => projectile.IsActive) &&
                    _tacticalEffectSafetyTicks < TacticalEffectSafetyLimit)
                {
                    _tacticalEffectSafetyTicks++;
                    AdvanceTacticalEffectsTick();
                    return FinishTacticalDeathIfNeeded();
                }

                if (_tacticalActionDelayTicks > 0)
                {
                    _tacticalActionDelayTicks--;
                    AdvanceTacticalEffectsTick();
                    return FinishTacticalDeathIfNeeded();
                }

                while (_tacticalEnemyQueue.Count > 0)
                {
                    Enemy enemy = _tacticalEnemyQueue.Dequeue();
                    if (!enemy.IsAlive)
                    {
                        TacticalTurn.EnemyActionsRemaining = _tacticalEnemyQueue.Count;
                        continue;
                    }
                    ExecuteTacticalEnemyAction(enemy);
                    return true;
                }

                FinishTacticalWorldPhase();
                return true;

            default:
                FinishTacticalWorldPhase();
                return true;
        }
    }

    private void ResetTacticalResolution()
    {
        if (_tacticalMovingEnemy != null)
        {
            _tacticalMovingEnemy.X = _tacticalMoveTargetX;
            _tacticalMovingEnemy.Y = _tacticalMoveTargetY;
        }
        _tacticalEnemyQueue.Clear();
        _tacticalMovingEnemy = null;
        _tacticalMoveTicksRemaining = 0;
        _tacticalActionDelayTicks = 0;
        _tacticalEffectSafetyTicks = 0;
        TacticalTurn.EnemyActionsTotal = 0;
        TacticalTurn.EnemyActionsRemaining = 0;
        TacticalTurn.ActiveEnemy = "";
        TacticalTurn.LastEnemyAction = "";
        TacticalTurn.ResolutionTicksRemaining = 0;
        TacticalTurn.MutableEnemyIntents.Clear();
    }

    private void PrepareTacticalEnemyPhase()
    {
        List<Enemy> actors = SelectTacticalActors(out HashSet<Enemy> observed,
            out HashSet<Enemy> encounterAlerted);
        RememberTacticalObservation(observed, encounterAlerted);

        foreach (Enemy enemy in actors)
        {
            if (!enemy.InCombat)
            {
                enemy.InCombat = true;
                CodexService.Instance.RecordEncounter(enemy, CurrentFloor);
            }
            _tacticalEnemyQueue.Enqueue(enemy);
        }
        BuildTacticalIntents(actors);

        var activeActors = actors.ToHashSet();
        foreach (Enemy enemy in Enemies.Where(enemy => enemy.IsAlive && !activeActors.Contains(enemy)))
            enemy.InCombat = false;

        Hero.InCombat = actors.Count > 0;
        TacticalTurn.Phase = TacticalPhase.EnemyActions;
        TacticalTurn.EnemyActionsTotal = actors.Count;
        TacticalTurn.EnemyActionsRemaining = actors.Count;
        TacticalTurn.ActiveEnemy = "";
        TacticalTurn.LastEnemyAction = actors.Count == 0
            ? "The dungeon holds"
            : $"{actors.Count} enem{(actors.Count == 1 ? "y" : "ies")} act";
        _tacticalEffectSafetyTicks = 0;
    }

    public void RefreshTacticalIntentPreview() =>
        RefreshTacticalIntentPreview(rememberObserved: false);

    private void RefreshTacticalIntentPreview(bool rememberObserved)
    {
        TacticalTurn.MutableEnemyIntents.Clear();
        if (SimulationMode != SimulationMode.TurnBased || !TacticalTurn.IsPlayerTurn || IsHeroDead)
            return;
        List<Enemy> actors = SelectTacticalActors(out HashSet<Enemy> observed,
            out HashSet<Enemy> encounterAlerted);
        if (rememberObserved) RememberTacticalObservation(observed, encounterAlerted);
        BuildTacticalIntents(actors);
    }

    private void RememberTacticalObservation(IEnumerable<Enemy> observed,
        IEnumerable<Enemy> encounterAlerted)
    {
        foreach (Enemy enemy in observed.Concat(encounterAlerted))
        {
            enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
            _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
        }
    }

    private List<Enemy> SelectTacticalActors(out HashSet<Enemy> observed,
        out HashSet<Enemy> encounterAlerted)
    {
        var actors = new List<Enemy>();
        observed = new HashSet<Enemy>();
        encounterAlerted = new HashSet<Enemy>();
        foreach (Enemy enemy in Enemies.Where(enemy => enemy.IsAlive))
        {
            float distance = Distance(enemy.X, enemy.Y, Hero.X, Hero.Y);
            bool hasLineOfSight = HasLineOfSight(enemy.X, enemy.Y, Hero.X, Hero.Y);
            bool persistent = distance < AgroRadius &&
                enemyPursuitTicks.TryGetValue(enemy, out int pursuit) && pursuit > 0;
            bool immediateThreat = hasLineOfSight &&
                distance <= MathF.Max(VisionRange, enemy.AttackRange + 1f);
            bool seesHero = EnemyCanSeeHero(enemy) || immediateThreat;
            if (persistent || seesHero) actors.Add(enemy);
            if (seesHero) observed.Add(enemy);
        }

        var alertedEncounters = actors.Where(enemy => enemy.EncounterId >= 0)
            .Select(enemy => enemy.EncounterId)
            .ToHashSet();
        foreach (Enemy enemy in Enemies.Where(enemy => enemy.IsAlive &&
                     alertedEncounters.Contains(enemy.EncounterId) && !actors.Contains(enemy)))
        {
            if (Distance(enemy.X, enemy.Y, Hero.X, Hero.Y) > 12f) continue;
            actors.Add(enemy);
            encounterAlerted.Add(enemy);
        }

        var stableOrder = Enemies.Select((enemy, index) => (enemy, index))
            .ToDictionary(pair => pair.enemy, pair => pair.index);
        return actors.Distinct()
            .OrderByDescending(enemy => enemy.Agility)
            .ThenBy(enemy => Distance(enemy.X, enemy.Y, Hero.X, Hero.Y))
            .ThenBy(enemy => stableOrder[enemy])
            .ToList();
    }

    private void BuildTacticalIntents(IReadOnlyList<Enemy> actors)
    {
        TacticalTurn.MutableEnemyIntents.Clear();
        var positions = Enemies.Where(enemy => enemy.IsAlive).ToDictionary(
            enemy => enemy,
            enemy => (x: (int)MathF.Round(enemy.X), y: (int)MathF.Round(enemy.Y)));

        for (int index = 0; index < actors.Count; index++)
        {
            Enemy enemy = actors[index];
            TacticalEnemyIntent intent = PlanTacticalEnemyIntent(enemy, index + 1, positions);
            TacticalTurn.MutableEnemyIntents.Add(intent);
            if (intent.TargetX.HasValue && intent.TargetY.HasValue &&
                intent.Kind is TacticalIntentKind.Advance or TacticalIntentKind.Reposition)
            {
                positions[enemy] = (intent.TargetX.Value, intent.TargetY.Value);
            }
        }
    }

    private void ExecuteTacticalEnemyAction(Enemy enemy)
    {
        TacticalTurn.ActiveEnemy = $"{enemy.Race} {enemy.Class}";
        TacticalTurn.EnemyActionsRemaining = _tacticalEnemyQueue.Count;
        _tacticalEffectSafetyTicks = 0;
        TacticalEnemyIntent? intent = TacticalTurn.EnemyIntents
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Actor, enemy));
        intent ??= PlanTacticalEnemyIntent(enemy,
            TacticalTurn.EnemyActionsTotal - TacticalTurn.EnemyActionsRemaining,
            CurrentEnemyPositions());

        if (intent.Kind == TacticalIntentKind.Attack &&
            _combatSystem.TryPerformTacticalEnemyAttack(Hero, enemy, Projectiles, CurrentMaze))
        {
            TacticalTurn.LastEnemyAction = $"{TacticalTurn.ActiveEnemy}: {intent.Detail}";
            LogMessage($"{TacticalTurn.ActiveEnemy} uses {intent.Detail}.", MessageKind.Combat);
            _tacticalActionDelayTicks = TacticalActionDelayTicks;
            return;
        }

        if (intent.Kind is TacticalIntentKind.Advance or TacticalIntentKind.Reposition &&
            intent.TargetX.HasValue && intent.TargetY.HasValue)
        {
            BeginTacticalEnemyMotion(enemy, intent.TargetX.Value, intent.TargetY.Value);
            TacticalTurn.LastEnemyAction = $"{TacticalTurn.ActiveEnemy}: {intent.Detail}";
            return;
        }

        TacticalTurn.LastEnemyAction = $"{TacticalTurn.ActiveEnemy}: holds";
        _tacticalActionDelayTicks = TacticalActionDelayTicks;
    }

    private TacticalEnemyIntent PlanTacticalEnemyIntent(Enemy enemy, int order,
        IReadOnlyDictionary<Enemy, (int x, int y)> positions)
    {
        (int x, int y) start = positions.TryGetValue(enemy, out var planned)
            ? planned
            : ((int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y));
        string actorName = $"{enemy.Race} {enemy.Class}";
        bool hasMove = TryPlanTacticalEnemyMove(enemy, start.x, start.y, positions,
            out (int x, int y) target, out bool retreating);
        if (hasMove && retreating)
        {
            return new TacticalEnemyIntent(order, enemy, actorName, TacticalIntentKind.Reposition,
                start.x, start.y, target.x, target.y, "repositions");
        }
        if (_combatSystem.CanPerformTacticalEnemyAttack(Hero, enemy, CurrentMaze))
        {
            return new TacticalEnemyIntent(order, enemy, actorName, TacticalIntentKind.Attack,
                start.x, start.y, Hero.GridX, Hero.GridY, enemy.CurrentAttack?.Name ?? "Attack");
        }

        if (hasMove)
        {
            return new TacticalEnemyIntent(order, enemy, actorName, TacticalIntentKind.Advance,
                start.x, start.y, target.x, target.y, "advances");
        }

        return new TacticalEnemyIntent(order, enemy, actorName, TacticalIntentKind.Hold,
            start.x, start.y, null, null, "holds");
    }

    private bool TryPlanTacticalEnemyMove(Enemy enemy, int startX, int startY,
        IReadOnlyDictionary<Enemy, (int x, int y)> positions,
        out (int x, int y) targetCell, out bool retreating)
    {
        targetCell = default;
        retreating = false;
        int heroX = Hero.GridX;
        int heroY = Hero.GridY;
        float distance = Distance(startX, startY, Hero.X, Hero.Y);

        if (enemy.AttackRange > 1.5f && distance < MathF.Max(1.5f, enemy.AttackRange * 0.55f) &&
            HasLineOfSight(startX, startY, Hero.X, Hero.Y))
        {
            var retreats = CardinalCells(startX, startY)
                .Where(cell => CanEnemyOccupyCell(cell.x, cell.y, enemy, positions))
                .OrderByDescending(cell => Distance(cell.x, cell.y, Hero.X, Hero.Y))
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.x)
                .ToList();
            if (retreats.Count > 0 &&
                Distance(retreats[0].x, retreats[0].y, Hero.X, Hero.Y) > distance)
            {
                retreating = true;
                targetCell = retreats[0];
                return true;
            }
        }

        (float x, float y) target = _enemyLastKnownHeroPos.TryGetValue(enemy, out var lastKnown)
            ? lastKnown
            : (Hero.X, Hero.Y);
        (int x, int y)? step = FindTacticalPathStep(startX, startY,
            (int)MathF.Round(target.x), (int)MathF.Round(target.y), enemy, positions);
        if (!step.HasValue || (step.Value.x == heroX && step.Value.y == heroY)) return false;
        targetCell = step.Value;
        return true;
    }

    private Dictionary<Enemy, (int x, int y)> CurrentEnemyPositions() =>
        Enemies.Where(enemy => enemy.IsAlive).ToDictionary(
            enemy => enemy,
            enemy => (x: (int)MathF.Round(enemy.X), y: (int)MathF.Round(enemy.Y)));

    private void BeginTacticalEnemyMotion(Enemy enemy, int targetX, int targetY)
    {
        _tacticalMovingEnemy = enemy;
        _tacticalMoveStartX = enemy.X;
        _tacticalMoveStartY = enemy.Y;
        _tacticalMoveTargetX = targetX;
        _tacticalMoveTargetY = targetY;
        _tacticalMoveTicksRemaining = TacticalMoveAnimationTicks;
        enemy.TargetX = targetX;
        enemy.TargetY = targetY;
    }

    private void AdvanceTacticalEnemyMotion()
    {
        if (_tacticalMovingEnemy == null) return;
        int elapsed = TacticalMoveAnimationTicks - _tacticalMoveTicksRemaining + 1;
        float progress = Math.Clamp(elapsed / (float)TacticalMoveAnimationTicks, 0f, 1f);
        _tacticalMovingEnemy.X = _tacticalMoveStartX + (_tacticalMoveTargetX - _tacticalMoveStartX) * progress;
        _tacticalMovingEnemy.Y = _tacticalMoveStartY + (_tacticalMoveTargetY - _tacticalMoveStartY) * progress;
        _tacticalMoveTicksRemaining--;
        if (_tacticalMoveTicksRemaining > 0) return;

        _tacticalMovingEnemy.X = _tacticalMoveTargetX;
        _tacticalMovingEnemy.Y = _tacticalMoveTargetY;
        _tacticalMovingEnemy = null;
        _tacticalActionDelayTicks = TacticalActionDelayTicks;
    }

    private (int x, int y)? FindTacticalPathStep(int startX, int startY, int targetX, int targetY,
        Enemy actor, IReadOnlyDictionary<Enemy, (int x, int y)> positions)
    {
        var queue = new Queue<(int x, int y)>();
        var previous = new Dictionary<(int x, int y), (int x, int y)>();
        var start = (x: startX, y: startY);
        var target = (x: targetX, y: targetY);
        if (start == target) return null;
        queue.Enqueue(start);
        previous[start] = start;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) break;
            foreach (var next in CardinalCells(current.x, current.y))
            {
                if (previous.ContainsKey(next) || !CurrentMaze.IsTraversable(next.x, next.y)) continue;
                bool isHeroTarget = next == target && next == (Hero.GridX, Hero.GridY);
                // CanEnemyOccupyCell is IsWalkable-based, so closed doors block enemy routes.
                // Deliberate (note 02 v1 ruling): enemies never open doors — a shut door
                // legitimately breaks pursuit, in real-time and tactical mode alike.
                if (!isHeroTarget && !CanEnemyOccupyCell(next.x, next.y, actor, positions)) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(target)) return null;
        var step = target;
        while (previous[step] != start)
            step = previous[step];
        return step;
    }

    private bool CanEnemyOccupyCell(int x, int y, Enemy actor,
        IReadOnlyDictionary<Enemy, (int x, int y)> positions)
    {
        if (!CurrentMaze.IsWalkable(x, y) || (Hero.GridX == x && Hero.GridY == y)) return false;
        return !positions.Any(pair => pair.Key != actor && pair.Key.IsAlive &&
            pair.Value.x == x && pair.Value.y == y);
    }

    private static IEnumerable<(int x, int y)> CardinalCells(int x, int y)
    {
        yield return (x, y - 1);
        yield return (x + 1, y);
        yield return (x, y + 1);
        yield return (x - 1, y);
    }

    private void AdvanceTacticalEffectsTick()
    {
        if (ScreenShake > 0f)
        {
            ScreenShake *= 0.8f;
            if (ScreenShake < 0.1f) ScreenShake = 0f;
        }
        Hero.AnimationOffsetX *= 0.8f;
        Hero.AnimationOffsetY *= 0.8f;
        foreach (Enemy enemy in Enemies)
        {
            enemy.AnimationOffsetX *= 0.8f;
            enemy.AnimationOffsetY *= 0.8f;
        }
        UpdateProjectilesAndEffects();
        RegisterHeroDeathIfNeeded();
        ApplyPendingGuardianVictory();
    }

    private void RegisterHeroDeathIfNeeded()
    {
        if (Hero.IsAlive || IsHeroDead) return;

        Hero.CurrentHp = 0;
        HandleHeroDeath();
    }

    /// <summary>
    /// The single death path, shared by the real-time and turn-based loops (they detect the death
    /// tick differently but must handle it identically). Permadeath destroys the character's save
    /// slot — but the world records that they lived first, because character death never deletes the
    /// world (owner ruling 2026-08-05).
    /// </summary>
    private void HandleHeroDeath()
    {
        IsHeroDead = true;
        DeathTimer = 0;
        CodexService.Instance.RecordDeath(CurrentFloor);
        LogMessage($"{Hero.Name} has died on floor {CurrentFloor}.", MessageKind.Warning);

        WorldService.RecordFallenHero(WorldId, BuildLegacyRecord());
        SaveService.Delete(SaveId);
    }

    /// <summary>The fallen hero as the world will remember them (see LegacyHero).</summary>
    private LegacyHero BuildLegacyRecord() => new()
    {
        Name = Hero.Name,
        Race = Hero.Race,
        Class = Hero.Class,
        Level = Hero.Progression.CharacterLevel,
        // Lifetime levels arrive with PR 1's XP rework; current level is the best stand-in.
        TotalLevelsGained = Hero.Progression.CharacterLevel,
        DaysLived = Math.Max(0, Clock.Day - _characterStartDay),
        CauseOfDeath = string.IsNullOrEmpty(_lastHeroDamageSource)
            ? "died of unknown causes"
            : $"slain by {_lastHeroDamageSource}",
        DeathLocation = IsInOverworld ? "the town"
            : IsInSafeRoom ? $"a safe room below floor {CurrentFloor}"
            : $"the Dungeon, floor {CurrentFloor}",
        Kills = Hero.Kills,
        InnocentKills = 0, // no NPCs to murder yet (PR 8)
        Gold = Hero.Gold,
        DiedAtUtc = DateTime.UtcNow
    };

    private void ApplyPendingGuardianVictory()
    {
        if (!_pendingGuardianVictory) return;

        _pendingGuardianVictory = false;
        IsInSafeRoom = false;
        LogMessage("The Guardian falls! The way deeper opens.", MessageKind.Combat);
        StartNewFloor();
        _tacticalEnemyQueue.Clear();
        _tacticalMovingEnemy = null;
        TacticalTurn.Phase = TacticalPhase.WorldUpkeep;
    }

    private bool FinishTacticalDeathIfNeeded()
    {
        if (IsHeroDead) SetSimulationMode(SimulationMode.RealTime);
        return true;
    }

    private void FinishTacticalWorldPhase()
    {
        TacticalTurn.Phase = TacticalPhase.WorldUpkeep;
        TickCount += _tacticalResolutionTicksPerTurn;
        // World time passes in turn-based mode too — a tactical turn compresses the same
        // simulated ticks the real-time loop would have run, and "a dive costs a slice of the
        // day" only holds if the clock sees every one of them.
        for (int tick = 0; tick < _tacticalResolutionTicksPerTurn; tick++)
            Clock.AdvanceTick(_ticksPerSecond);
        RegenerateTacticalResources(_tacticalResolutionTicksPerTurn);
        for (int tick = 0; tick < _tacticalResolutionTicksPerTurn && CurrentActivity != null; tick++)
            UpdateCurrentActivity();
        if (_heroAlertTicks > 0)
            _heroAlertTicks = Math.Max(0, _heroAlertTicks - _tacticalResolutionTicksPerTurn);
        foreach (Enemy enemy in Enemies)
        {
            if (enemyPursuitTicks.TryGetValue(enemy, out int pursuit) && pursuit > 0)
                enemyPursuitTicks[enemy] = Math.Max(0, pursuit - _tacticalResolutionTicksPerTurn);
        }
        for (int tick = 0; tick < _tacticalResolutionTicksPerTurn; tick++) UpdatePerception();
        // Same per-simulated-tick cadence the real-time loop gives theme features.
        for (int tick = 0; tick < _tacticalResolutionTicksPerTurn; tick++)
            UpdateDungeonThemeFeatures();

        Hero.InCombat = Enemies.Any(enemy => enemy.IsAlive && enemy.InCombat);
        if (!Hero.InCombat) CheckFeatures();
        ApplyPendingGuardianVictory();
        RegisterHeroDeathIfNeeded();

        if (IsHeroDead)
        {
            SetSimulationMode(SimulationMode.RealTime);
            return;
        }
        BeginTacticalPlayerTurn();
    }

    private void RegenerateTacticalResources(int ticks)
    {
        float combatModifier = Hero.InCombat ? 0.7f : 1f;
        if (IsInSafeRoom && Boss == null) combatModifier = 3f;
        _accumulatedStaminaRegen += Hero.EffectiveConstitution / (8f * _ticksPerSecond) * combatModifier * ticks;
        _accumulatedManaRegen += Hero.EffectiveIntelligence / (8f * _ticksPerSecond) * combatModifier * ticks;
        _accumulatedFaithRegen += Hero.EffectiveWisdom / (8f * _ticksPerSecond) * combatModifier * ticks;
        Hero.CurrentStamina = RestoreAccumulated(ref _accumulatedStaminaRegen,
            Hero.CurrentStamina, Hero.MaxStamina);
        Hero.CurrentMana = RestoreAccumulated(ref _accumulatedManaRegen,
            Hero.CurrentMana, Hero.MaxMana);
        Hero.CurrentFaith = RestoreAccumulated(ref _accumulatedFaithRegen,
            Hero.CurrentFaith, Hero.MaxFaith);

        float healthModifier = IsInSafeRoom && Boss == null ? 3f : 1f;
        _accumulatedHealthRegen += Hero.EffectiveConstitution / (16f * _ticksPerSecond) * healthModifier * ticks;
        Hero.CurrentHp = RestoreAccumulated(ref _accumulatedHealthRegen,
            Hero.CurrentHp, Hero.MaxHp);
    }

    private static int RestoreAccumulated(ref float accumulated, int current, int maximum)
    {
        if (accumulated < 1f) return current;
        int amount = (int)accumulated;
        current = Math.Min(maximum, current + amount);
        accumulated -= amount;
        return current;
    }

    private static float Distance(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private bool CanTakeTacticalPlayerAction() =>
        SimulationMode == SimulationMode.TurnBased && TacticalTurn.IsPlayerTurn &&
        IsRunning && !IsHeroDead && CurrentActivity == null;

    private bool CanAffordCurrentAttack()
    {
        var attack = Hero.CurrentAttack;
        return attack == null || !attack.IsHeavyAttack ||
            (Hero.CurrentStamina >= attack.StaminaCost &&
             Hero.CurrentMana >= AffinityService.EffectiveManaCost(Hero.Affinities, MagicElements.For(attack), attack.ManaCost) &&
             Hero.CurrentFaith >= attack.FaithCost);
    }

    private bool IsEnemyOccupyingCell(int x, int y) =>
        Enemies.Any(enemy => enemy.IsAlive &&
            (int)MathF.Round(enemy.X) == x && (int)MathF.Round(enemy.Y) == y);

    private void BeginTacticalPlayerTurn()
    {
        TacticalTurn.TurnNumber++;
        TacticalTurn.MovementAllowance = CalculateTacticalMovementAllowance();
        TacticalTurn.MovementRemaining = TacticalTurn.MovementAllowance;
        TacticalTurn.ActionAvailable = true;
        TacticalTurn.BonusActionAvailable = true;
        TacticalTurn.IsPlayerTurn = true;
        TacticalTurn.Phase = TacticalPhase.Player;
        TacticalTurn.ResolutionTicksRemaining = 0;
        TacticalTurn.ActiveEnemy = "";
        TacticalTurn.LastEnemyAction = "";
        TacticalTurn.EnemyActionsRemaining = 0;
        Hero.AttackCooldown = 0;
        RefreshTacticalIntentPreview(rememberObserved: true);
        UpdateNearbyInteractable();
    }

    private void EndTacticalTurnIfSpent()
    {
        if (TacticalTurn.MovementRemaining == 0 && !TacticalTurn.ActionAvailable &&
            !TacticalTurn.BonusActionAvailable)
            EndTacticalTurn();
    }

    /// <summary>
    /// Fire the hero's current attack toward a world direction (Manual click-to-fire). No-op in
    /// Auto mode, while dead/paused, on cooldown, or when a heavy attack can't be afforded. The
    /// direction need not be normalized.
    /// </summary>
    public void FireManualAttack(float dirX, float dirY)
    {
        if (ControlMode != ControlMode.Manual || IsHeroDead || !IsRunning) return;
        if (Hero.AttackCooldown > 0) return;

        if (!CanAffordCurrentAttack()) return;

        _combatSystem.PerformHeroDirectionalAttack(Hero, dirX, dirY, Projectiles);
    }

    /// <summary>Select the attack assigned to a hotbar slot (0-based). Empty slots no-op, so a
    /// number key never "selects nothing".</summary>
    public void SelectAttack(int slot)
    {
        var attack = HotbarAttackAt(slot);
        if (attack != null) Hero.CurrentAttack = attack;
    }

    /// <summary>Cycle the selected attack through the occupied hotbar slots, wrapping (the
    /// scroll wheel steps by ±1). Empty slots are skipped.</summary>
    public void CycleAttack(int delta)
    {
        var occupied = new List<int>();
        for (int i = 0; i < Hero.HotbarAssignments.Count; i++)
            if (HotbarAttackAt(i) != null) occupied.Add(i);
        if (occupied.Count == 0) return;
        int currentPos = occupied.FindIndex(slot => ReferenceEquals(HotbarAttackAt(slot), Hero.CurrentAttack));
        int next = (((currentPos + delta) % occupied.Count) + occupied.Count) % occupied.Count;
        Hero.CurrentAttack = HotbarAttackAt(occupied[next]);
    }

    public bool EquipFromInventory(Combinable item) => EquipFromInventory(item, out _);

    /// <summary>Equip physical gear into its body slot, or place a spell on the action bar.</summary>
    public bool EquipFromInventory(Combinable item, out string reason)
    {
        reason = "";
        if (!Hero.Inventory.Contains(item))
        {
            reason = "That item is not in your backpack.";
            return false;
        }

        if (item is Spell)
        {
            if (Hero.Loadout.Any(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
            {
                reason = $"{item.Name} is already on the action bar.";
                return false;
            }
            int free = FirstFreeHotbarSlot();
            if (free < 0)
            {
                reason = "The action bar is full — clear a slot from the Hotbar tab first.";
                return false;
            }
            Hero.Inventory.Remove(item);
            Hero.Loadout.Add(item);
            RefreshAttacks();
            AssignAttackToHotbar(item.Id, free, out _);
            LogMessage($"Slotted {item.Name} on the action bar.", MessageKind.System);
            return true;
        }

        EquipmentSlot? slot = item switch
        {
            Armor armor => armor.Slot,
            Item accessory when accessory.EquipSlot.HasValue => accessory.EquipSlot,
            Weapon => FirstFreeHand(item as Weapon),
            _ => null
        };
        if (!slot.HasValue)
        {
            reason = item is Weapon ? "Both hands are occupied." : "That item cannot be equipped.";
            return false;
        }

        if (item is Item ring && ring.EquipSlot is EquipmentSlot.RingLeft or EquipmentSlot.RingRight)
        {
            slot = !Hero.Equipment.ContainsKey(EquipmentSlot.RingLeft) ? EquipmentSlot.RingLeft
                : !Hero.Equipment.ContainsKey(EquipmentSlot.RingRight) ? EquipmentSlot.RingRight
                : null;
            if (!slot.HasValue)
            {
                reason = "Both ring slots are occupied.";
                return false;
            }
        }

        if (slot == EquipmentSlot.OffHand && IsOffHandBlocked)
        {
            reason = "The off hand is reserved by a two-handed weapon.";
            return false;
        }

        if (item is Weapon weapon && weapon.HandsRequired >= 2)
        {
            if (Hero.Equipment.ContainsKey(EquipmentSlot.MainHand) ||
                Hero.Equipment.ContainsKey(EquipmentSlot.OffHand))
            {
                reason = "A two-handed weapon requires both hands to be free.";
                return false;
            }
            slot = EquipmentSlot.MainHand;
        }
        else if (Hero.Equipment.ContainsKey(slot.Value))
        {
            reason = $"{EquipmentSlots.Label(slot.Value)} is already occupied.";
            return false;
        }

        Hero.Inventory.Remove(item);
        Hero.Equipment[slot.Value] = item;
        if (item is Weapon) RefreshAttacks();
        LogMessage($"Equipped {item.Name} in {EquipmentSlots.Label(slot.Value)}.", MessageKind.System);
        if (item is Weapon equippedWeapon && !WeaponProficiencyService.IsTrained(Hero, equippedWeapon))
        {
            LogMessage($"{equippedWeapon.Name} is unfamiliar: " +
                $"{(1f - WeaponProficiencyService.UntrainedDamageMultiplier):P0} damage and " +
                $"{(1f - WeaponProficiencyService.UntrainedAccuracyMultiplier):P0} accuracy penalty.",
                MessageKind.Warning);
        }
        return true;
    }

    private EquipmentSlot? FirstFreeHand(Weapon? weapon)
    {
        if (weapon == null) return null;
        if (Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon main && main.HandsRequired >= 2)
            return null;
        if (!Hero.Equipment.ContainsKey(EquipmentSlot.MainHand)) return EquipmentSlot.MainHand;
        return !Hero.Equipment.ContainsKey(EquipmentSlot.OffHand) ? EquipmentSlot.OffHand : null;
    }

    public bool IsOffHandBlocked =>
        Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon { HandsRequired: >= 2 };

    /// <summary>Return an equipped item or slotted spell to the backpack.</summary>
    public bool UnequipToInventory(Combinable item)
    {
        if (Hero.Loadout.Remove(item))
        {
            Hero.Inventory.Add(item);
            RefreshAttacks();
            LogMessage($"Removed {item.Name} from the action bar.", MessageKind.System);
            return true;
        }

        var equipped = Hero.Equipment.FirstOrDefault(pair => ReferenceEquals(pair.Value, item));
        if (equipped.Value == null || !Hero.Equipment.Remove(equipped.Key)) return false;
        Hero.Inventory.Add(item);
        if (item is Weapon) RefreshAttacks();
        LogMessage($"Unequipped {item.Name}.", MessageKind.System);
        return true;
    }

    /// <summary>Whether saving is allowed right now: in the Overworld, or in a safe room whose
    /// Guardian hasn't been engaged. Never on regular dungeon floors — the dungeon is the Trial;
    /// closing the game mid-dive reverts to the dungeon-entrance auto-save. The pause menu's
    /// Save action keys off this.</summary>
    public bool CanSave => IsInOverworld || (IsInSafeRoom && Boss == null);
    // Set when the Guardian dies mid-enumeration (see HandleEnemyDefeated); applied once Tick()
    // reaches a point where Enemies/Projectiles are safe to clear.
    private bool _pendingGuardianVictory;

    /// <summary>The hero's current multi-tick task (mining, crafting, ...).
    /// Only one at a time — see StartActivity. Advanced once per Tick().</summary>
    public Activity? CurrentActivity { get; private set; }

    /// <summary>Player-facing event feed ("Found a Sword", "The Guardian stirs...") rendered at
    /// the bottom of the game view. Distinct from GameLog (developer/debug output).</summary>
    public MessageLog Messages { get; } = new();

    internal void LogMessage(string text, MessageKind kind) => Messages.Add(text, kind, TickCount);

    public List<Projectile> Projectiles { get; set; } = new();
    public List<HitEffect> HitEffects { get; set; } = new();

    /// <summary>World time-of-day (15-min real days/nights, owner ruling 2026-08-05). Ticks with
    /// the sim, persists in saves, keeps running during dives.</summary>
    public WorldClock Clock { get; } = new();

    /// <summary>Rising/fading world-space combat feedback ("14", "Dodge!") — render-only.</summary>
    public List<FloatingText> FloatingTexts { get; } = new();

    /// <summary>Ambient creatures (dungeon rats/bats, the town dog and cat). Only populated when
    /// EnableAmbience is on; simulated from their own RNG stream so gameplay stays deterministic.</summary>
    public List<Critter> Critters { get; } = new();

    /// <summary>Live-game-only atmosphere: ambient critters + occasional flavor lines in the
    /// message log. Off by default so headless demos keep their exact RNG/log behavior; the
    /// player-facing game turns it on at the ViewModel seam (same pattern as Manual control).</summary>
    public bool EnableAmbience { get; set; }

    private readonly Random _ambienceRandom = new(0x51D);
    private Maze? _ambienceMaze;      // which maze Critters were spawned for
    private int _nextAmbientLineTick = -1;

    private void AddFloatingText(string text, float x, float y, FloatingTextKind kind)
    {
        if (FloatingTexts.Count > 40) FloatingTexts.RemoveAt(0);
        FloatingTexts.Add(new FloatingText { Text = text, X = x, Y = y, Kind = kind });
    }

    // ---- Night & light (town lighting v1 — the dungeon's darkness is its fog; a unified light
    // model incl. dungeon torches lands with the stealth/light work, Planning note 11) ----

    /// <summary>Carrying a torch (anywhere in gear) pushes the hero's light pool out at night.</summary>
    public bool HasTorch => Hero.Loadout.Concat(Hero.Inventory).Any(c => c.Id == "torch");

    /// <summary>The Night Sight skill (owner ruling 2026-08-05): the whole screen brightens at
    /// night, not just a radius. Ownership counts until the ability-slot system lands.</summary>
    public bool HasNightSight => Hero.Loadout.Concat(Hero.Inventory).Any(c => c.Id == "night-sight");

    /// <summary>How far the hero personally sees at night: darkvision races (D&D-mapped) see
    /// their full vision range; a torch buys a decent pool; bare human eyes, barely arm's reach.</summary>
    public float HeroLightRadius => Hero.HasDarkvision ? VisionRange : HasTorch ? 4.5f : 2.2f;

    // Track enemy pursuit persistence
    private Dictionary<Enemy, int> enemyPursuitTicks = new();
    // Where each enemy last saw the hero — pursued toward when line of sight is lost, so enemies
    // chase around corners for the pursuit window instead of instantly giving up.
    private readonly Dictionary<Enemy, (float x, float y)> _enemyLastKnownHeroPos = new();
    private const float AgroRadius = 7.5f; // Extended agro radius for persistence

    // Timing derived from the authoritative tick rate (see GameSettings) so real-time
    // durations stay correct regardless of ticks/sec.
    private readonly int _ticksPerSecond;
    private readonly int _pursuitTimeoutTicks;   // ~3s pursuit persistence
    private readonly int _autoRestartTicks;      // ~5s until auto-restart after death
    private readonly int _combatStartWindupTicks; // ~0.3s wind-up before first attack on engage
    private readonly int _attackSwitchTicks;     // ~2s between smart attack-rotation switches
    private readonly int _tacticalResolutionTicksPerTurn;

    public int Seed { get; set; }
    public int TickCount { get; private set; }
    public int CurrentFloor { get; private set; } = 0;
    public bool IsRunning { get; set; }

    /// <summary>Identifies this character's save slot (Saves/Worlds/{WorldId}/Characters/{SaveId}.json)
    /// — generated fresh for a new character, or restored from the loaded SaveData in LoadFrom so
    /// re-saving overwrites the same file rather than multiplying save slots.</summary>
    public string SaveId { get; private set; } = Guid.NewGuid().ToString();

    private string _worldId = "";

    /// <summary>
    /// The world this character lives in. Resolved lazily — on first read, which is whenever the
    /// character first needs persisting (a save, or the legacy record on death) — rather than at
    /// construction, so merely building a GameState (the Godot client's title-screen preview state,
    /// a diagnostic screen) doesn't conjure a world the player never asked for. Restored from the
    /// save on load; a character never changes worlds.
    /// </summary>
    public string WorldId => string.IsNullOrEmpty(_worldId)
        ? _worldId = WorldService.EnsureActiveWorld()
        : _worldId;
    public CharacterCreationSelection? CreationSelection => _creationSelection?.Clone();

    // Town dimensions. Fields rather than constants so the `towngen` debug command can build the
    // town at the scale a real region needs (one tile ~= one metre), which is how the renderer's
    // camera culling gets exercised before the region generator lands. Session-only, never saved.
    private int _townWidth = OverworldGenerator.Width;
    private int _townHeight = OverworldGenerator.Height;
    private bool _townSizeOverride;

    // What last hurt the hero, as a display string, so their legacy record can say how they died.
    // Recorded at each damage site because the killing blow itself carries no attribution: an
    // enemy projectile outlives its shooter, and hazards have no actor at all.
    private string _lastHeroDamageSource = "";

    // The world-clock day this character's life began (0 for the world's first character; set by
    // RestartGame so a replacement doesn't inherit the fallen hero's days in their legacy record).
    private int _characterStartDay;

    // Playtime accumulated in prior sessions (from a loaded save); TotalPlaytimeSeconds adds the
    // current session's elapsed ticks on top so it stays accurate without a per-tick save write.
    private double _priorPlaytimeSeconds;
    public double TotalPlaytimeSeconds => _priorPlaytimeSeconds + TickCount / (double)_ticksPerSecond;
    
    // Debug flags (can be toggled via env vars)
    public bool DebugDrawHitboxes { get; set; }
    public bool DebugDrawLOS { get; set; }

    /// <summary>Current screen-shake magnitude in pixels (renderer applies as camera jitter, then it decays each tick).</summary>
    public float ScreenShake { get; private set; }
    
    // Death state
    public bool IsHeroDead { get; private set; }
    public int DeathTimer { get; private set; }

    /// <summary>Seconds remaining before auto-restart (for UI display).</summary>
    public float DeathCountdownSeconds =>
        MathF.Max(0f, (_autoRestartTicks - DeathTimer) / (float)_ticksPerSecond);

    // Health regeneration tracking (for fractional HP per tick)
    private float _accumulatedHealthRegen = 0f;
    
    // Resource regeneration tracking (for fractional resource per tick)
    private float _accumulatedStaminaRegen = 0f;
    private float _accumulatedManaRegen = 0f;
    private float _accumulatedFaithRegen = 0f;
    
    // Store character creation info for restart
    private string _characterName = "Hero";
    private string _className = "Wanderer";
    private string _raceName = "Human";
    private CharacterCreationSelection? _creationSelection;

    // Not readonly: RestartGame reseeds these with a fresh seed.
    private MazeGenerator _mazeGenerator;
    private MovementSystem _movementSystem;
    private CombatSystem _combatSystem;
    private readonly CharacterDataService _characterDataService;
    private Random _random;
    
    public GameState(int seed) : this(seed, "Hero", "Wanderer", "Human")
    {
    }
    
    public GameState(int seed, string characterName, string className, string raceName)
    {
        Seed = seed;
        _characterName = characterName;
        _className = className;
        _raceName = raceName;
        // Characters exist inside a world, but WorldId resolves lazily — see the property.
        _worldId = WorldService.ActiveWorldId ?? "";

        // Derive all real-time durations from the authoritative tick rate.
        _ticksPerSecond = Math.Max(1, GameSettings.Current.TickRate);
        _pursuitTimeoutTicks = GameSettings.Current.SecondsToTicks(3f);
        _autoRestartTicks = GameSettings.Current.SecondsToTicks(5f);
        _combatStartWindupTicks = GameSettings.Current.SecondsToTicks(0.3f);
        _attackSwitchTicks = GameSettings.Current.SecondsToTicks(2f);
        _tacticalResolutionTicksPerTurn = GameSettings.Current.SecondsToTicks(0.5f);
        _dashTicks = GameSettings.Current.SecondsToTicks(0.2f);
        _dashCooldownTicks = GameSettings.Current.SecondsToTicks(1f);
        _alertTicks = GameSettings.Current.SecondsToTicks(1.2f);

        _random = new Random(seed);
        _mazeGenerator = new MazeGenerator(seed);
        _movementSystem = new MovementSystem(seed);
        _combatSystem = new CombatSystem(seed);
        _characterDataService = new CharacterDataService();
        
        Hero = new Hero 
        { 
            Name = characterName,
            // Derived stats - reduced base attack for balance
            MaxHp = 100, 
            CurrentHp = 100, 
            Attack = 5, 
            Defense = 5,
            X = 1,
            Y = 1
        };
        
        // Apply class and race stats
        _characterDataService.ApplyClassAndRace(Hero, className, raceName);
        Hero.Progression = ProgressionService.Instance.CreateCompatibilityState(className);
        ProgressionService.Instance.Normalize(Hero);

        // Update resource pools based on attributes
        UpdateHeroResourcePools();

        // Initialize resources to max
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;

        GameLog.Debug($"Character Created: {Hero.Name} - {Hero.Race} {Hero.Class}");
        GameLog.Debug($"Colors - Race: {Hero.RaceColor}, Class: {Hero.ClassColor}");
        GameLog.Debug($"Base Str {Hero.Strength} -> Effective {Hero.EffectiveStrength:0.0}; Con {Hero.Constitution} -> {Hero.EffectiveConstitution:0.0} (MaxStamina {Hero.MaxStamina}); Int {Hero.Intelligence} -> {Hero.EffectiveIntelligence:0.0} (MaxMana {Hero.MaxMana})");

        // Class actions and physical equipment are separate systems.
        Hero.Loadout = new List<Combinable>();
        Hero.Equipment = AttackFactory.GetStartingEquipment(className);
        RefreshAttacks();
        GameLog.Debug($"Attacks assigned: {Hero.Attacks.Count}, Current: {Hero.CurrentAttack?.Name ?? "None"}");

        // Every adventurer starts with a torch in their pack — at night, bare eyes without
        // darkvision see barely past arm's reach (owner ruling 2026-08-05: night means night).
        Hero.Inventory.Add(CombinableCatalog.Torch());

        StartNewFloor();

        // Debug flags from environment
        DebugDrawHitboxes = (Environment.GetEnvironmentVariable("DEBUG_HITBOXES") == "1");
        DebugDrawLOS = (Environment.GetEnvironmentVariable("DEBUG_LOS") == "1");
    }

    public GameState(int seed, CharacterCreationSelection creation)
        : this(seed,
            string.IsNullOrWhiteSpace(creation.Name) ? "Wayfarer" : creation.Name.Trim(),
            "Wanderer",
            creation.RaceName)
    {
        _creationSelection = creation.Clone();
        _characterName = Hero.Name;
        _raceName = creation.RaceName;
        _className = "Classless";

        _characterDataService.ApplyClasslessAndRace(Hero, creation.RaceName);
        Hero.Progression = ProgressionService.Instance.CreateEmptyState();
        ProgressionService.Instance.Normalize(Hero);

        IEnumerable<string> itemIds = creation.ItemIds;
        if (!creation.IsCustom && creation.ItemIds.Count == 0)
            itemIds = StarterLoadoutService.Instance.SelectionFromKit(
                Hero.Name, creation.RaceName, creation.KitId).ItemIds;
        _creationSelection.ItemIds = itemIds.ToList();
        StarterLoadoutService.Instance.ApplyToHero(Hero, itemIds);
        UpdateHeroResourcePools();
        Hero.CurrentHp = Hero.MaxHp;
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;
        RefreshAttacks();

        // The base constructor always opened a floor-1 dive; a town-born character is redirected.
        if (creation.StartLocation == StartLocation.Town) StartInTown();
    }

    public static GameState FromSave(int seed, SaveData data)
    {
        GameState state = data.CreationSelection != null
            ? new GameState(seed, data.CreationSelection)
            : new GameState(seed, data.HeroName, data.ClassName, data.RaceName);
        state.LoadFrom(data);
        return state;
    }
    
    public void Tick()
    {
        if (!IsRunning) return;
        if (SimulationMode == SimulationMode.TurnBased) return;

        // Decay screen shake regardless of death/combat state so it always settles.
        if (ScreenShake > 0f)
        {
            ScreenShake *= 0.8f;
            if (ScreenShake < 0.1f) ScreenShake = 0f;
        }

        // Check if hero died this tick. Permadeath destroys the save slot (after the world records
        // the fallen hero) — see HandleHeroDeath. The game stops but keeps ticking for the timer.
        if (!Hero.IsAlive && !IsHeroDead)
            HandleHeroDeath();
        
        // Handle death timer and auto-restart
        if (IsHeroDead)
        {
            DeathTimer++;
            if (DeathTimer >= _autoRestartTicks)
            {
                RestartGame();
            }
            return; // Don't process game logic while dead
        }
        
        TickCount++;

        // World time advances with the sim (pausing pauses time; a dive costs a slice of the day).
        Clock.AdvanceTick(_ticksPerSecond);

        // Bank any level-up unlocks as real inventory items (see Hero.PendingUnlocks — appending
        // them straight to Hero.Attacks let RefreshAttacks silently wipe them).
        if (Hero.PendingUnlocks.Count > 0) DrainPendingUnlocks();

        // Live-game atmosphere: ambient critters + occasional flavor lines (no-op in headless demos).
        if (EnableAmbience) UpdateAmbience();

        // Regenerate hero resources using fractional accumulation.
        // Per-second rates: Constitution/8 stamina, Intelligence/8 mana, Wisdom/8 faith.
        // Divide by ticks/sec so the real-time rate is correct at any tick rate.
        // Resources regenerate 30% slower during combat; a safe room's restful effect gives a
        // 3x boost instead (while the Guardian hasn't been engaged yet).
        float combatRegenModifier = Hero.InCombat ? 0.7f : 1.0f;
        if (IsInSafeRoom && Boss == null) combatRegenModifier = 3.0f;
        float regenPerSecondDivisor = 8f * _ticksPerSecond;

        float staminaPerTick = (Hero.EffectiveConstitution / regenPerSecondDivisor) * combatRegenModifier;
        float manaPerTick = (Hero.EffectiveIntelligence / regenPerSecondDivisor) * combatRegenModifier;
        float faithPerTick = (Hero.EffectiveWisdom / regenPerSecondDivisor) * combatRegenModifier;
        
        _accumulatedStaminaRegen += staminaPerTick;
        _accumulatedManaRegen += manaPerTick;
        _accumulatedFaithRegen += faithPerTick;
        
        if (_accumulatedStaminaRegen >= 1.0f)
        {
            int staminaToRestore = (int)_accumulatedStaminaRegen;
            Hero.CurrentStamina = Math.Min(Hero.MaxStamina, Hero.CurrentStamina + staminaToRestore);
            _accumulatedStaminaRegen -= staminaToRestore;
        }
        
        if (_accumulatedManaRegen >= 1.0f)
        {
            int manaToRestore = (int)_accumulatedManaRegen;
            Hero.CurrentMana = Math.Min(Hero.MaxMana, Hero.CurrentMana + manaToRestore);
            _accumulatedManaRegen -= manaToRestore;
        }
        
        if (_accumulatedFaithRegen >= 1.0f)
        {
            int faithToRestore = (int)_accumulatedFaithRegen;
            Hero.CurrentFaith = Math.Min(Hero.MaxFaith, Hero.CurrentFaith + faithToRestore);
            _accumulatedFaithRegen -= faithToRestore;
        }
        
        // Health regeneration: Constitution/16 HP per second (e.g. 16 Constitution = 1 HP/sec).
        // Boosted by the same safe-room restful effect as the resource regen above.
        float hpRegenModifier = (IsInSafeRoom && Boss == null) ? 3.0f : 1.0f;
        float hpPerTick = (Hero.EffectiveConstitution / (16f * _ticksPerSecond)) * hpRegenModifier;
        _accumulatedHealthRegen += hpPerTick;
        
        if (_accumulatedHealthRegen >= 1.0f)
        {
            int hpToRestore = (int)_accumulatedHealthRegen;
            Hero.CurrentHp = Math.Min(Hero.MaxHp, Hero.CurrentHp + hpToRestore);
            _accumulatedHealthRegen -= hpToRestore;
        }
        
        // (Dash timers advance at the END of Tick — after the movement and the i-frame check in
        // projectile collisions have consumed this tick — so an N-tick dash yields N moved,
        // invulnerable ticks rather than N-1.)

        // Move hero (unless busy with an activity)
        if (CurrentActivity == null)
        {
            if (_dashTicksRemaining > 0)
            {
                // Mid-dash: fast fixed-direction burst (overrides normal movement).
                _movementSystem.MoveHeroByDirection(Hero, _dashDirX, _dashDirY, CurrentMaze, DashSpeed);
            }
            else if (ControlMode == ControlMode.Manual)
            {
                // Player-driven movement. Auto-explore / goal-walk / combat-approach are all
                // skipped; the player positions the hero and attacks still auto-fire (CheckCombat).
                _movementSystem.MoveHeroByDirection(Hero, _manualMoveX, _manualMoveY, CurrentMaze);
            }
            else if (!Hero.InCombat)
            {
                if (IsInOverworld)
                {
                    // The Overworld is player-driven only (Manual control, handled above) — there is
                    // no auto-movement in town. The player walks with WASD and uses structures via
                    // the Press-E interaction. Nothing to do here.
                }
                else if (IsInSafeRoom)
                {
                    // Safe rooms are a genuine player choice. Never auto-select the Guardian door
                    // or shrine on the player's behalf.
                }
                else if (StairsLocation.HasValue)
                {
                    // Stairs need no key anymore — path straight to them once spotted.
                    _movementSystem.MoveHeroTowardTarget(Hero, StairsLocation.Value.x, StairsLocation.Value.y, CurrentMaze);
                }
                else
                {
                    // Normal exploration movement
                    _movementSystem.MoveHeroTowardUnexplored(Hero, CurrentMaze);
                }
            }
            else
            {
                // During combat, find the enemy we're fighting and move toward them
                var combatEnemy = Enemies.FirstOrDefault(e => e.IsAlive && e.InCombat);
                if (combatEnemy != null)
                {
                    // Move toward enemy to maintain attack range
                    _movementSystem.MoveHeroTowardEnemy(Hero, combatEnemy, CurrentMaze);
                }
            }
        }
        
        // Move enemies
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            if (!enemy.InCombat)
            {
                // Idle wander (BFS waypoint) when not in combat, throttled for a calmer pace
                if (TickCount % 2 == 0)
                {
                    _movementSystem.MoveEnemyIdle(enemy, CurrentMaze);
                }
            }
            else
            {
                // Chase: head for the hero if in sight, otherwise for the last place they were
                // seen (so enemies pursue around corners until the pursuit window expires).
                float tx = Hero.X, ty = Hero.Y;
                if (HasLineOfSight(enemy.X, enemy.Y, Hero.X, Hero.Y))
                {
                    _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
                }
                else if (_enemyLastKnownHeroPos.TryGetValue(enemy, out var lastKnown))
                {
                    tx = lastKnown.x;
                    ty = lastKnown.y;
                }
                _movementSystem.MoveEnemyTowardTarget(enemy, tx, ty, CurrentMaze);
            }
        }

        // Resolve hero/enemy physical interactions (hitbox collision)
        ResolveHeroEnemyCollisions();

        // Perception: roll to notice nearby hidden traps; decay the "!" alert.
        if (_heroAlertTicks > 0) _heroAlertTicks--;
        UpdatePerception();

        // Theme landmarks are visible, fixed-orientation world features. Handle their contact
        // effects before combat discovery so alarms can wake the room's encounter this tick.
        UpdateDungeonThemeFeatures();

        // Check for new combat encounters and process existing combat
        CheckCombat();

        // Advance/prune projectiles and hit effects every tick, even when combat just ended
        UpdateProjectilesAndEffects();

        // Apply a deferred Guardian-victory transition now that it's safe to clear
        // Enemies/Projectiles (see HandleEnemyDefeated for why this can't happen inline).
        if (_pendingGuardianVictory)
        {
            _pendingGuardianVictory = false;
            IsInSafeRoom = false;
            LogMessage("The Guardian falls! The way deeper opens.", MessageKind.Combat);
            StartNewFloor();
        }

        // Advance the hero's current long-running activity (mining, crafting, ...), if any
        UpdateCurrentActivity();

        // Check for features (stairs, chests, shrine, guardian door, traps) - only if not in combat
        if (!Hero.InCombat)
        {
            CheckFeatures();
        }

        // Track a chest or town structure in Press-E range for the contextual prompt.
        UpdateNearbyInteractable();

        // Auto mode auto-collects loot off nearby corpses (the manual player loots deliberately via
        // right-click instead), preserving auto-play's loot collection now that drops stay on bodies.
        if (ControlMode == ControlMode.Auto)
        {
            AutoLootNearbyCorpses();
        }

        // In Manual mode the player selects the attack (hotbar) and fires by clicking; the
        // hero's attack cooldown still ticks down here so click-fire respects it. Auto-mode's
        // cooldown is driven by ProcessCombat instead.
        if (ControlMode == ControlMode.Manual)
        {
            if (Hero.AttackCooldown > 0) Hero.AttackCooldown--;
            // Decay the lunge animation offset (ProcessCombat does this in Auto mode).
            Hero.AnimationOffsetX *= 0.8f;
            Hero.AnimationOffsetY *= 0.8f;
        }

        // Smart attack rotation during combat - prefer heavy attacks when resources are available.
        // Auto mode only: in Manual mode the player's hotbar selection must stick.
        if (ControlMode == ControlMode.Auto && Hero.InCombat && Hero.Attacks.Count > 1)
        {
            // Switch attack every ~2 seconds OR when we can't afford the current attack
            bool timeToSwitch = TickCount % _attackSwitchTicks == 0;
            bool cantAffordCurrent = Hero.CurrentAttack != null && Hero.CurrentAttack.IsHeavyAttack &&
                (Hero.CurrentStamina < Hero.CurrentAttack.StaminaCost ||
                 Hero.CurrentMana < Hero.CurrentAttack.ManaCost ||
                 Hero.CurrentFaith < Hero.CurrentAttack.FaithCost);
            
            if (timeToSwitch || cantAffordCurrent)
            {
                // Get heavy attacks that we have resources for
                var affordableHeavyAttacks = Hero.Attacks.Where(a => 
                    a.IsHeavyAttack && 
                    Hero.CurrentStamina >= a.StaminaCost &&
                    Hero.CurrentMana >= a.ManaCost &&
                    Hero.CurrentFaith >= a.FaithCost).ToList();
                
                // If we have affordable heavy attacks, use one randomly
                if (affordableHeavyAttacks.Count > 0)
                {
                    int idx = _random.Next(affordableHeavyAttacks.Count);
                    Hero.CurrentAttack = affordableHeavyAttacks[idx];
                }
                else
                {
                    // Otherwise fall back to light attacks
                    var lightAttacks = Hero.Attacks.Where(a => !a.IsHeavyAttack).ToList();
                    if (lightAttacks.Count > 0)
                    {
                        int idx = _random.Next(lightAttacks.Count);
                        Hero.CurrentAttack = lightAttacks[idx];
                    }
                }
            }
        }

        // Dash timers: advance an active dash, then run down its cooldown. Last on purpose —
        // every consumer of this tick's dash state (movement above, IsHeroInvulnerable in the
        // projectile pass) has already run, so the configured tick count is honored exactly.
        if (_dashTicksRemaining > 0)
        {
            _dashTicksRemaining--;
            if (_dashTicksRemaining == 0) _dashCooldownRemaining = _dashCooldownTicks;
        }
        else if (_dashCooldownRemaining > 0)
        {
            _dashCooldownRemaining--;
        }
    }

    private void CheckCombat()
    {
        // Gather all enemies that are engaged (either seen by hero, see the hero, or close/persistent)
        List<Enemy> engagedEnemies = new();
        Enemy? primaryTarget = null;
        float closestDistance = float.MaxValue;

        // First, check if hero can see any enemies
        float heroFacing = Hero.InCombat && Hero.AttackCooldown == 0 && Hero.CurrentAttack != null
            ? MathF.Atan2(Hero.AnimationOffsetY, Hero.AnimationOffsetX)
            : 0f;
        float maxEnemyRange = Enemies.Count > 0 ? Enemies.Max(e => e.AttackRange) : 7.5f;
        float heroAttackRange = Hero.CurrentAttack?.Range ?? 1.0f;
        float heroSightRange = MathF.Max(MathF.Max(maxEnemyRange, heroAttackRange), VisionRange);
        var heroVisibleCells = GetDirectionalSightCone(Hero.X, Hero.Y, heroFacing, heroSightRange, VisionConeAngleRad);
        
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            int enemyCellX = (int)MathF.Round(enemy.X);
            int enemyCellY = (int)MathF.Round(enemy.Y);
            // Hitbox-aware visibility: consider enemy circle against hero's cone
            bool heroSeesInConeCells = heroVisibleCells.Contains((enemyCellX, enemyCellY));
            bool heroHitboxCone = HitboxIntersectsCone(Hero.X, Hero.Y, heroFacing, heroSightRange, VisionConeAngleRad,
                                      enemy.X, enemy.Y, enemy.Radius);
            // If detected by hitbox+cone, still require LOS unless overlapping bodies
            bool overlappingBodies = MathF.Sqrt((Hero.X - enemy.X) * (Hero.X - enemy.X) + (Hero.Y - enemy.Y) * (Hero.Y - enemy.Y))
                                      <= (Hero.Radius + enemy.Radius + 0.05f);
            bool heroSeesEnemy = heroSeesInConeCells || (heroHitboxCone && (overlappingBodies || HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y)));
            
            // Also check if enemy can see hero (scanning in all directions)
            bool enemySeesHero = EnemyCanSeeHero(enemy);
            
            if (heroSeesEnemy || enemySeesHero)
            {
                float dx = Hero.X - enemy.X;
                float dy = Hero.Y - enemy.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                engagedEnemies.Add(enemy);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    primaryTarget = enemy;
                }
                // Reset pursuit timer + remember where the hero was, if line of sight is clear.
                if (HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y))
                {
                    enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
                    _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
                }
            }
        }
        // Fallback: include close melee-range enemies or persistent pursuers even if not in cone
        if (engagedEnemies.Count == 0)
        {
            foreach (var enemy in Enemies.Where(e => e.IsAlive))
            {
                float dx = Hero.X - enemy.X;
                float dy = Hero.Y - enemy.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);

                // Close proximity check (melee overlap) - ensure immediate threats are detected
                float meleeThreshold = Math.Max(1.5f, enemy.AttackRange);
                bool isPersistent = enemyPursuitTicks.TryGetValue(enemy, out int t) && t > 0;
                
                // If overlapping hitboxes, target regardless of LOS
                bool overlapping = distance < (Hero.Radius + enemy.Radius + 0.05f);
                // Require line of sight otherwise - prevents targeting through walls
                bool hasLOS = overlapping || HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y);

                // Engage if the hero is in melee reach with LOS, OR this enemy is a recent pursuer
                // still within the agro radius — pursuers stay engaged even without LOS so they
                // chase toward the hero's last-known position instead of instantly giving up.
                if ((hasLOS && distance <= meleeThreshold) || (distance < AgroRadius && isPersistent))
                {
                    engagedEnemies.Add(enemy);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        primaryTarget = enemy;
                    }
                    // If the enemy currently sees the hero, refresh pursuit + last-known position.
                    if (hasLOS && distance < 5.0f)
                    {
                        enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
                        _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
                    }
                }
            }
        }

        // Alert nearby members of the same generated encounter. Packs respond together, but the
        // distance cap prevents a patroller on the far side of the floor from gaining omniscience.
        var alertedEncounterIds = engagedEnemies
            .Where(enemy => enemy.EncounterId >= 0)
            .Select(enemy => enemy.EncounterId)
            .ToHashSet();
        if (alertedEncounterIds.Count > 0)
        {
            foreach (var enemy in Enemies.Where(enemy =>
                         enemy.IsAlive && alertedEncounterIds.Contains(enemy.EncounterId) &&
                         !engagedEnemies.Contains(enemy)))
            {
                float dx = Hero.X - enemy.X;
                float dy = Hero.Y - enemy.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 12f) continue;

                engagedEnemies.Add(enemy);
                enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
                _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    primaryTarget = enemy;
                }
            }
        }
        // Decrement pursuit timers
        foreach (var enemy in Enemies)
        {
            if (enemyPursuitTicks.ContainsKey(enemy) && enemyPursuitTicks[enemy] > 0)
                enemyPursuitTicks[enemy]--;
        }
        if (engagedEnemies.Count == 0)
        {
            // No enemy in sight - end combat if we were fighting
            if (Hero.InCombat)
            {
                Hero.InCombat = false;
                Hero.AnimationOffsetX = 0;
                Hero.AnimationOffsetY = 0;
                foreach (var enemy in Enemies)
                {
                    enemy.InCombat = false;
                }
            }
            return;
        }
        // Engage combat with all found enemies. Capture prior state BEFORE flipping the
        // flag so we can detect the transition into combat.
        bool heroWasInCombat = Hero.InCombat;
        Hero.InCombat = true;
        // Ensure enemies are marked in combat and have initial cooldowns
        foreach (var e in engagedEnemies)
        {
            if (!e.InCombat)
            {
                e.InCombat = true;
                // Give them a small initial delay similar to StartCombat, but don't reset hero
                e.AttackCooldown = Math.Max(e.AttackCooldown, e.AttackSpeed / 2);
                CodexService.Instance.RecordEncounter(e, CurrentFloor);
            }
        }

        // Choose the closest as the primary target for the hero's own attack logic
        var targetEnemy = primaryTarget ?? engagedEnemies[0];

        // On the transition INTO combat, apply a short wind-up so ranged/casters can't
        // land a free instant hit the moment they spot an enemy.
        if (!heroWasInCombat)
        {
            _combatSystem.StartCombat(Hero, targetEnemy);
            Hero.AttackCooldown = Math.Max(Hero.AttackCooldown, _combatStartWindupTicks);
        }

        if (ControlMode == ControlMode.Manual)
        {
            // The player fires the hero's attacks by clicking (FireManualAttack); here we only run
            // the enemies' side so they still fight back while the player positions and aims.
            foreach (var e in engagedEnemies)
            {
                _combatSystem.ProcessEnemyOnlyAttack(Hero, e, Projectiles, CurrentMaze);
            }
        }
        else
        {
            // Auto: process hero vs primary target (includes that enemy's retaliation)...
            _combatSystem.ProcessCombat(Hero, targetEnemy, Projectiles, CurrentMaze);
            // ...and enemy-only attacks for the rest so hero cooldown isn't decremented twice.
            foreach (var e in engagedEnemies)
            {
                if (e == targetEnemy) continue;
                _combatSystem.ProcessEnemyOnlyAttack(Hero, e, Projectiles, CurrentMaze);
            }
        }
    }

    /// <summary>
    /// Advance and prune projectiles and hit effects. Runs every tick (not only during combat),
    /// so the final attack's projectile animates out and is removed even after the enemy dies —
    /// otherwise it freezes in place until the next fight.
    /// </summary>
    private void UpdateProjectilesAndEffects()
    {
        foreach (var projectile in Projectiles)
        {
            projectile.Update(CurrentMaze);
        }
        // Apply contact damage from projectiles/hitboxes before removing expired ones
        ProcessProjectileCollisions();
        Projectiles.RemoveAll(p => !p.IsActive);
        // Update and prune hit effects
        UpdateHitEffects();
        // Age out floating combat texts
        foreach (var ft in FloatingTexts) ft.Age++;
        FloatingTexts.RemoveAll(ft => ft.Expired);
    }

    /// <summary>Convert banked level-up unlocks into real inventory Combinables. Level-up used to
    /// append these directly to Hero.Attacks, where the next RefreshAttacks (any equip/unequip or
    /// load) silently wiped them since they had no backing Loadout item.</summary>
    private void DrainPendingUnlocks()
    {
        while (Hero.PendingUnlocks.Count > 0)
        {
            var id = Hero.PendingUnlocks[0];
            Hero.PendingUnlocks.RemoveAt(0);
            // Already owned (e.g. a re-drain after load) — never grant duplicates.
            if (Hero.Loadout.Concat(Hero.Inventory).Any(c => c.Id == id)) continue;
            var item = CombinableCatalog.BuildUnlock(id);
            if (item == null) continue;
            Hero.Inventory.Add(item); // no-auto-equip rule: the player equips it themselves
            LogMessage($"Unlocked: {item.Name} — added to your inventory.", MessageKind.LevelUp);
        }
    }

    // ---- Ambience: critters + flavor lines (live game only; own RNG stream) ----

    private static readonly string[] DungeonAmbientLines =
    {
        "Water drips somewhere in the dark.",
        "A cold draft moves through the corridor.",
        "Something skitters just beyond the light.",
        "The stones groan overhead.",
        "A distant echo... footsteps? Or your own?",
        "The air tastes of dust and old iron.",
        "Somewhere far below, something shifts its weight.",
    };

    private static readonly string[] TownAmbientLines =
    {
        "A breeze carries the smell of forge-smoke.",
        "Faint hammering rings from the smithy.",
        "The mountain looms silent over the town.",
        "Somewhere, a dog barks.",
        "Wind moves through the grass by the dungeon mouth.",
    };

    private void UpdateAmbience()
    {
        // (Re)populate critters whenever the current map changes (new floor, town, safe room).
        if (!ReferenceEquals(_ambienceMaze, CurrentMaze))
        {
            _ambienceMaze = CurrentMaze;
            SpawnCritters();
        }
        UpdateCritters();

        // Occasional atmosphere line in the message log (out of combat, spaced 45–90s).
        if (!Hero.InCombat && !IsInSafeRoom)
        {
            if (_nextAmbientLineTick < 0)
            {
                _nextAmbientLineTick = TickCount + _ticksPerSecond * (45 + _ambienceRandom.Next(46));
            }
            else if (TickCount >= _nextAmbientLineTick)
            {
                var lines = IsInOverworld ? TownAmbientLines : DungeonAmbientLines;
                LogMessage(lines[_ambienceRandom.Next(lines.Length)], MessageKind.System);
                _nextAmbientLineTick = TickCount + _ticksPerSecond * (45 + _ambienceRandom.Next(46));
            }
        }
    }

    private void SpawnCritters()
    {
        Critters.Clear();
        if (IsInSafeRoom) return; // the one place that should feel truly still

        if (IsInOverworld)
        {
            // The town's resident dog and cat (the first inhabitants — NPCs come later).
            TryPlaceCritter(CritterKind.Dog);
            TryPlaceCritter(CritterKind.Cat);
        }
        else
        {
            int count = 2 + _ambienceRandom.Next(3); // 2-4 vermin per floor
            for (int i = 0; i < count; i++)
                TryPlaceCritter(_ambienceRandom.NextDouble() < 0.65 ? CritterKind.Rat : CritterKind.Bat);
        }
    }

    private void TryPlaceCritter(CritterKind kind)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int x = _ambienceRandom.Next(1, CurrentMaze.Width - 1);
            int y = _ambienceRandom.Next(1, CurrentMaze.Height - 1);
            if (!CurrentMaze.IsWalkable(x, y)) continue;
            // Not right on top of the hero's start.
            float dx = x - Hero.X, dy = y - Hero.Y;
            if (dx * dx + dy * dy < 16f) continue;
            Critters.Add(new Critter { Kind = kind, X = x, Y = y, TargetX = x, TargetY = y });
            return;
        }
    }

    private void UpdateCritters()
    {
        foreach (var c in Critters)
        {
            // Rats bolt when the hero gets close — prey behavior sells "alive" more than wandering.
            if (c.Kind == CritterKind.Rat)
            {
                float hdx = c.X - Hero.X, hdy = c.Y - Hero.Y;
                if (hdx * hdx + hdy * hdy < 2.2f * 2.2f)
                {
                    float len = MathF.Max(0.001f, MathF.Sqrt(hdx * hdx + hdy * hdy));
                    int fx = (int)MathF.Round(c.X + hdx / len * 3f);
                    int fy = (int)MathF.Round(c.Y + hdy / len * 3f);
                    if (CurrentMaze.IsWalkable(fx, fy)) { c.TargetX = fx; c.TargetY = fy; c.PauseTicks = 0; }
                }
            }

            if (c.PauseTicks > 0) { c.PauseTicks--; continue; }

            float tdx = c.TargetX - c.X, tdy = c.TargetY - c.Y;
            float dist = MathF.Sqrt(tdx * tdx + tdy * tdy);
            if (dist < 0.12f)
            {
                // Arrived: idle a while (bats never rest), then pick a new spot nearby.
                c.PauseTicks = c.Kind switch
                {
                    CritterKind.Bat => 0,
                    CritterKind.Rat => 20 + _ambienceRandom.Next(60),
                    _ => 40 + _ambienceRandom.Next(120), // dog/cat linger
                };
                PickCritterTarget(c);
                continue;
            }

            // Step toward the target with per-axis walkability (wall-slide like the hero).
            float step = MathF.Min(c.Speed, dist);
            float nx = c.X + tdx / dist * step;
            float ny = c.Y + tdy / dist * step;
            bool moved = false;
            if (CurrentMaze.IsWalkable((int)MathF.Round(nx), (int)MathF.Round(c.Y))) { c.X = nx; moved = true; }
            if (CurrentMaze.IsWalkable((int)MathF.Round(c.X), (int)MathF.Round(ny))) { c.Y = ny; moved = true; }
            if (!moved) PickCritterTarget(c); // wedged — go somewhere else
        }
    }

    private void PickCritterTarget(Critter c)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int x = (int)MathF.Round(c.X) + _ambienceRandom.Next(-4, 5);
            int y = (int)MathF.Round(c.Y) + _ambienceRandom.Next(-4, 5);
            if (!CurrentMaze.IsWalkable(x, y)) continue;
            // Keep paths sane in corridors: only dart toward spots it can "see".
            if (!HasLineOfSight(c.X, c.Y, x, y)) continue;
            c.TargetX = x;
            c.TargetY = y;
            return;
        }
    }

    /// <summary>
    /// Public debug wrapper for LOS checks from renderer/diagnostics.
    /// </summary>
    public bool CheckLOS(float x1, float y1, float x2, float y2) => HasLineOfSight(x1, y1, x2, y2);

    /// <summary>
    /// Final damage for a directional (manual) hero shot against the enemy it actually hit —
    /// mirrors CombatSystem's target-locked math (defense = Defense + 0.7·Con, +20% magic resist
    /// for magic, then a ±25% variance), so manual and auto attacks hit for comparable amounts.
    /// </summary>
    private int ResolveStatDamage(int statDamage, Enemy enemy, bool magic, MagicElement element)
    {
        int enemyDefense = enemy.Defense + (int)(enemy.Constitution * 0.7f);
        if (magic) enemyDefense += (int)(enemyDefense * 0.2f);
        int baseDamage = Math.Max(1, statDamage - enemyDefense / 2);
        int variance = _random.Next(-baseDamage / 4, baseDamage / 4 + 1);
        int rolled = Math.Max(1, baseDamage + variance);
        // Target's elemental resistance to the shot's element (the caster's power was already
        // baked into statDamage at fire time).
        return AffinityService.ApplyResistance(rolled, enemy.Affinities, element);
    }

    /// <summary>Roll whether a target evades an incoming hit. Chance rises with the target's
    /// Agility and falls with the attacker's accuracy (Dexterity), with a small floor and a cap so
    /// nothing is ever guaranteed to hit or fully un-hittable — this is what makes Agility (dodge)
    /// and Dexterity (accuracy) matter, alongside physically moving out of a projectile's path.</summary>
    private bool RollDodge(float targetAgility, float attackerAccuracy)
    {
        const float baseDodge = 0.03f;
        const float perPoint = 0.02f;
        const float maxDodge = 0.50f;
        float chance = Math.Clamp(baseDodge + (targetAgility - attackerAccuracy) * perPoint, 0f, maxDodge);
        return _random.NextDouble() < chance;
    }

    /// <summary>
    /// Resolve projectile contact damage against hero/enemies.
    /// </summary>
    private void ProcessProjectileCollisions()
    {
        if (Projectiles.Count == 0) return;

        // Compute a dynamic effective radius for certain effects (e.g., expanding rings)
        float GetEffectiveRadius(Projectile p)
        {
            // Base radius from projectile, with special-case growth
            if (p.Visual == VisualStyle.ArcaneRing)
            {
                // Start small and expand with lifetime (tiles)
                return 0.25f + p.LifeTime * 0.05f;
            }
            return p.Radius;
        }

        foreach (var p in Projectiles)
        {
            if (!p.IsActive || p.ConsumedOnHit) continue;

            float pr = GetEffectiveRadius(p);

            if (p.Team == ProjectileTeam.Hero)
            {
                // Check against all living enemies
                foreach (var enemy in Enemies.Where(e => e.IsAlive))
                {
                    // Multi-hit (AoE): each enemy resolves against this projectile at most once
                    // over its whole lifetime — an expanding ring overlapping a target for many
                    // ticks is one hit, not one hit per tick.
                    if (p.CanHitMultiple && p.HitTargets.Contains(enemy)) continue;

                    float dx = p.CurrentX - enemy.X;
                    float dy = p.CurrentY - enemy.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist <= (pr + enemy.Radius))
                    {
                        // Dodge check: an agile enemy may evade the hit entirely. For an AoE,
                        // the dodger alone is exempted — the effect keeps going for everyone
                        // else rather than being consumed by one nimble target.
                        if (RollDodge(enemy.Agility, p.Accuracy))
                        {
                            LogMessage($"The {enemy.Race} {enemy.Class} dodges!", MessageKind.Combat);
                            AddFloatingText("Dodge", enemy.X, enemy.Y - 0.4f, FloatingTextKind.Dodge);
                            if (p.CanHitMultiple)
                            {
                                p.HitTargets.Add(enemy);
                                continue;
                            }
                            p.ConsumedOnHit = true;
                            p.LifeTime = p.MaxLifeTime;
                            break;
                        }

                        // Directional (manual) shots resolve their damage here against the enemy
                        // actually struck; auto-combat shots use the damage pre-baked at spawn.
                        int applied = p.StatDamage > 0
                            ? ResolveStatDamage(p.StatDamage, enemy, p.IsMagic, p.Element)
                            : Math.Max(1, p.Damage);
                        enemy.Hp -= applied;
                        AddFloatingText(applied.ToString(), enemy.X, enemy.Y - 0.4f, FloatingTextKind.EnemyDamage);
                        // Spawn tiny on-hit flash
                        HitEffects.Add(new HitEffect
                        {
                            X = p.CurrentX,
                            Y = p.CurrentY,
                            LifeTime = 0,
                            MaxLifeTime = 8,
                            Type = HitEffectType.Impact,
                            Team = ProjectileTeam.Hero
                        });

                        // Handle the kill — but only stand the hero down when no other engaged
                        // enemy remains. Ending combat on every kill in a pack fight stomped the
                        // hero's attack cooldown and re-imposed the engage wind-up per kill.
                        if (!enemy.IsAlive)
                        {
                            GameLog.Debug("  ✓ Enemy defeated by hit!");
                            HandleEnemyDefeated(enemy);
                            enemy.InCombat = false;
                            if (!Enemies.Any(e => e.IsAlive && e.InCombat))
                            {
                                Hero.InCombat = false;
                                Hero.AnimationOffsetX = 0;
                                Hero.AnimationOffsetY = 0;
                            }
                            // Do not clear other projectiles; allow other combats to continue
                        }

                        if (!p.CanHitMultiple)
                        {
                            p.ConsumedOnHit = true;
                            p.LifeTime = p.MaxLifeTime; // deactivate
                            break;
                        }
                        // Multi-hit: remember this target and keep sweeping — the same tick may
                        // strike several enemies standing inside the effect.
                        p.HitTargets.Add(enemy);
                    }
                }
            }
            else if (p.Team == ProjectileTeam.Enemy)
            {
                // Check collision with hero
                float dx = p.CurrentX - Hero.X;
                float dy = p.CurrentY - Hero.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= (pr + Hero.Radius))
                {
                    // Active dodge (dash i-frames): the hit is avoided outright.
                    if (IsHeroInvulnerable)
                    {
                        p.ConsumedOnHit = true;
                        p.LifeTime = p.MaxLifeTime;
                        continue;
                    }

                    // Dodge check: the hero's Agility (vs the attacker's accuracy) can evade the hit.
                    if (RollDodge(Hero.EffectiveAgility, p.Accuracy))
                    {
                        LogMessage("You dodge the attack!", MessageKind.System);
                        AddFloatingText("Dodge!", Hero.X, Hero.Y - 0.4f, FloatingTextKind.Dodge);
                        p.ConsumedOnHit = true;
                        p.LifeTime = p.MaxLifeTime;
                        continue;
                    }

                    int heroHit = Math.Max(1, p.Damage);
                    Hero.CurrentHp -= heroHit;
                    _lastHeroDamageSource = string.IsNullOrEmpty(p.SourceLabel)
                        ? "an unseen attacker" : $"a {p.SourceLabel}";
                    AddFloatingText(heroHit.ToString(), Hero.X, Hero.Y - 0.4f, FloatingTextKind.HeroDamage);
                    // Screen shake scales with the hit's severity relative to max HP, capped modestly.
                    ScreenShake = MathF.Max(ScreenShake, MathF.Min(5f, (p.Damage / (float)Hero.MaxHp) * 45f));
                    // Spawn tiny on-hit flash
                    HitEffects.Add(new HitEffect
                    {
                        X = p.CurrentX,
                        Y = p.CurrentY,
                        LifeTime = 0,
                        MaxLifeTime = 8,
                        Type = HitEffectType.Impact,
                        Team = ProjectileTeam.Enemy
                    });

                    if (!Hero.IsAlive)
                    {
                        Hero.CurrentHp = 0;
                        Hero.InCombat = false;
                        foreach (var enemy in Enemies) enemy.InCombat = false;
                        Hero.AnimationOffsetX = 0;
                        Hero.AnimationOffsetY = 0;
                        // Do not clear remaining projectiles globally
                    }

                    if (!p.CanHitMultiple)
                    {
                        p.ConsumedOnHit = true;
                        p.LifeTime = p.MaxLifeTime; // deactivate
                    }
                }
            }
        }
    }

    /// <summary>
    /// Awards XP (scaled by the enemy's tier, see Enemy.XpMultiplier), rolls a tier-scaled
    /// chance for a loot drop, and records the kill in the Codex. Single choke point — this is
    /// the only place an enemy's Hp reaches zero.
    /// </summary>
    private void HandleEnemyDefeated(Enemy enemy)
    {
        Hero.Kills++; // this character's own tally, for the legacy record they leave behind
        int xpGain = (int)((10 + enemy.MaxHp / 4) * enemy.XpMultiplier);
        // Merge resolution: remote's shared-XP progression path wins (XP banks for player
        // allocation), with the local level-up feedback kept — it fires whenever allocation
        // (or a legacy path) actually raises Hero.Level during this call.
        int levelBefore = Hero.Level;
        int awarded = ProgressionService.Instance.GrantSharedXp(Hero, xpGain);
        LogMessage($"Slew the {enemy.Race} {enemy.Class} (+{awarded} shared XP)", MessageKind.Combat);
        if (Hero.Level > levelBefore)
        {
            LogMessage($"Level up! Now level {Hero.Level}.", MessageKind.LevelUp);
            AddFloatingText("LEVEL UP!", Hero.X, Hero.Y - 0.6f, FloatingTextKind.LevelUp);
        }

        // Loot now stays on the body — the hero loots the corpse (right-click → Loot) rather than
        // it auto-collecting. Roll the drop into the enemy's inventory + a small gold drop.
        float dropChance = enemy.Tier switch
        {
            EnemyTier.Elite => 0.45f,
            EnemyTier.Boss => 1.0f,
            _ => 0.15f
        };
        if (_random.NextDouble() < dropChance)
        {
            enemy.Inventory.Add(LootService.Roll(CurrentFloor, _random));
        }
        enemy.Gold += _random.Next(0, 3 + CurrentFloor) * (enemy.IsBoss ? 5 : enemy.IsElite ? 2 : 1);

        CodexService.Instance.RecordKill(enemy, CurrentFloor);

        // Defeating the safe room's Guardian proceeds to the next floor group. This is called
        // from inside a live `foreach (var p in Projectiles)` (ProcessProjectileCollisions), so
        // we can't mutate Enemies/Projectiles here (StartNewFloor clears both) — defer it and
        // apply it once that enumeration has finished (see Tick()).
        if (enemy == Boss)
        {
            _pendingGuardianVictory = true;
        }
    }

    private void UpdateHitEffects()
    {
        if (HitEffects.Count == 0) return;
        foreach (var fx in HitEffects)
        {
            fx.LifeTime++;
        }
        HitEffects.RemoveAll(fx => !fx.IsActive);
    }

    /// <summary>
    /// Prevent hero from walking through enemies; gently separate overlapping bodies.
    /// </summary>
    private void ResolveHeroEnemyCollisions()
    {
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            float dx = Hero.X - enemy.X;
            float dy = Hero.Y - enemy.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float minDist = Hero.Radius + enemy.Radius;
            if (dist < minDist && dist > 1e-3f)
            {
                float push = (minDist - dist) + 0.02f; // small extra gap
                float nx = dx / dist;
                float ny = dy / dist;

                // Push the hero out of the overlap, but never into a wall — apply each axis only
                // if its target cell is walkable (so a hero against a wall isn't shoved through it
                // and left blind inside solid rock). Wall-slides along whichever axis is clear.
                float heroPush = push * 0.8f;
                bool movedX = TryNudgeHeroAxis(nx * heroPush, 0f);
                bool movedY = TryNudgeHeroAxis(0f, ny * heroPush);

                // If the hero couldn't move at all (cornered against walls), separate by pushing
                // the enemy back instead (also wall-checked) so the two don't stay overlapped.
                if (!movedX && !movedY)
                {
                    NudgeEnemyAxis(enemy, -nx * push, 0f);
                    NudgeEnemyAxis(enemy, 0f, -ny * push);
                }
                else if (!enemy.InCombat)
                {
                    NudgeEnemyAxis(enemy, -nx * push * 0.2f, 0f);
                    NudgeEnemyAxis(enemy, 0f, -ny * push * 0.2f);
                }
            }
        }
    }

    /// <summary>Move the hero by (dx,dy) only if the destination cell is walkable. Returns whether
    /// it moved. Used to separate combatants without shoving the hero into a wall.</summary>
    private bool TryNudgeHeroAxis(float dx, float dy)
    {
        if (dx == 0f && dy == 0f) return false;
        float newX = Hero.X + dx;
        float newY = Hero.Y + dy;
        if (CurrentMaze.IsWalkable((int)MathF.Round(newX), (int)MathF.Round(newY)))
        {
            Hero.X = newX;
            Hero.Y = newY;
            return true;
        }
        return false;
    }

    private void NudgeEnemyAxis(Enemy enemy, float dx, float dy)
    {
        if (dx == 0f && dy == 0f) return;
        float newX = enemy.X + dx;
        float newY = enemy.Y + dy;
        if (CurrentMaze.IsWalkable((int)MathF.Round(newX), (int)MathF.Round(newY)))
        {
            enemy.X = newX;
            enemy.Y = newY;
        }
    }

    /// <summary>
    /// Returns true if a circle (cx,cy,r) intersects a vision cone from (ox,oy) with facing and range
    /// </summary>
    private bool HitboxIntersectsCone(float ox, float oy, float facingRad, float range, float coneRad,
        float cx, float cy, float radius)
    {
        float dx = cx - ox;
        float dy = cy - oy;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d - radius > range) return false; // completely out of range

        // Angle from origin to target center
        float angle = MathF.Atan2(dy, dx);
        float angleDiff = MathF.Abs(NormalizeAngleRad(angle - facingRad));

        // Expand the cone by the angular radius of the circle
        float expand = 0f;
        if (d > 1e-3f)
        {
            float s = MathF.Min(1f, radius / d);
            expand = MathF.Asin(s);
        }
        float halfCone = coneRad / 2f;
        return angleDiff <= (halfCone + expand);
    }
    
    /// <summary>
    /// Give the hero found loot. Everything goes to the backpack until the player equips physical
    /// gear or slots a spell from the Character screen.
    /// </summary>
    public void AcquireLoot(Combinable loot)
    {
        Hero.Inventory.Add(loot);
        LogMessage($"Found {loot.Name} ({loot.Rarity})", MessageKind.Loot);
    }

    /// <summary>Rebuild the equipment-derived basic attack, class techniques, and slotted spells.</summary>
    private void RefreshAttacks()
    {
        var currentId = Hero.CurrentAttack?.Id;
        Hero.Attacks = new List<Attack> { AttackFactory.GetBasicAttack(Hero) };
        foreach (ProgressionInstance activeClass in Hero.Progression.ClassSlots
                     .Where(slot => !slot.IsLocked && slot.Instance != null)
                     .Select(slot => slot.Instance!))
        {
            foreach (Attack attack in AttackFactory.GetClassAttacks(activeClass.Name, activeClass.Level))
                if (Hero.Attacks.All(existing => existing.Id != attack.Id)) Hero.Attacks.Add(attack);
            foreach (ProgressionLineageEntry lineage in activeClass.Lineage)
                foreach (Attack attack in AttackFactory.GetClassAttacks(
                             lineage.Name, Math.Max(1, lineage.LevelAtConsumption)))
                    if (Hero.Attacks.All(existing => existing.Id != attack.Id)) Hero.Attacks.Add(attack);
        }
        Hero.Attacks.AddRange(AttackFactory.GetSlottedSpellAttacks(Hero.Loadout));
        Hero.CurrentAttack = Hero.Attacks.FirstOrDefault(a => a.Id == currentId) ?? Hero.Attacks.FirstOrDefault();
        ReconcileHotbar();
    }

    /// <summary>Keep the positional hotbar honest after any Attacks rebuild: size to capacity,
    /// drop ids that no longer project, dedupe, then auto-place attacks the hotbar has never
    /// seen into free slots (fresh heroes fill 1..N; a deliberately cleared action stays cleared
    /// because its id is in HotbarKnownIds).</summary>
    private void ReconcileHotbar()
    {
        var slots = Hero.HotbarAssignments;
        int capacity = Math.Max(1, Hero.HotbarCapacity);
        while (slots.Count < capacity) slots.Add(null);
        if (slots.Count > capacity) slots.RemoveRange(capacity, slots.Count - capacity);

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < slots.Count; i++)
        {
            string? id = slots[i];
            if (id == null) continue;
            bool projects = Hero.Attacks.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            bool carriedConsumable = Hero.Inventory.Any(c =>
                c is Item { Consumable: true } && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if ((!projects && !carriedConsumable) || !assigned.Add(id)) slots[i] = null;
        }

        foreach (Attack attack in Hero.Attacks)
        {
            if (assigned.Contains(attack.Id) || Hero.HotbarKnownIds.Contains(attack.Id)) continue;
            int free = slots.IndexOf(null);
            if (free < 0) break;
            slots[free] = attack.Id;
            assigned.Add(attack.Id);
        }
        foreach (Attack attack in Hero.Attacks) Hero.HotbarKnownIds.Add(attack.Id);
    }

    /// <summary>The attack assigned to a hotbar slot (0-based), or null for empty/out-of-range.</summary>
    public Attack? HotbarAttackAt(int slot)
    {
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) return null;
        string? id = Hero.HotbarAssignments[slot];
        if (id == null) return null;
        return Hero.Attacks.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private int FirstFreeHotbarSlot() => Hero.HotbarAssignments.IndexOf(null);

    /// <summary>The carried consumable a hotbar slot points at (null if the slot holds an attack,
    /// is empty, or the last copy was used up).</summary>
    public Item? HotbarConsumableAt(int slot)
    {
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) return null;
        string? id = Hero.HotbarAssignments[slot];
        if (id == null) return null;
        return Hero.Inventory.OfType<Item>().FirstOrDefault(item =>
            item.Consumable && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>How many copies of a consumable slot's item the hero carries (for the slot badge).</summary>
    public int HotbarConsumableCount(int slot)
    {
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) return 0;
        string? id = Hero.HotbarAssignments[slot];
        if (id == null) return 0;
        return Hero.Inventory.Count(c =>
            c is Item { Consumable: true } && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Put a carried consumable on a hotbar slot (it stays in the backpack; the slot is
    /// a quick-use binding, decremented as copies are consumed).</summary>
    public bool AssignConsumableToHotbar(Item item, int slot, out string reason)
    {
        reason = "";
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) { reason = "No such slot."; return false; }
        if (!item.Consumable || !Hero.Inventory.Contains(item))
        {
            reason = "That item can't be quick-slotted.";
            return false;
        }
        int existing = Hero.HotbarAssignments.FindIndex(id =>
            string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        string? displaced = Hero.HotbarAssignments[slot];
        Hero.HotbarAssignments[slot] = item.Id;
        if (existing >= 0 && existing != slot) Hero.HotbarAssignments[existing] = displaced;
        LogMessage($"{item.Name} assigned to slot {slot + 1}.", MessageKind.System);
        return true;
    }

    /// <summary>The one hotbar entry point for slot keys/clicks: an attack slot selects that
    /// attack; a consumable slot uses one copy; an empty slot does nothing.</summary>
    public void ActivateHotbarSlot(int slot)
    {
        var attack = HotbarAttackAt(slot);
        if (attack != null)
        {
            Hero.CurrentAttack = attack;
            return;
        }
        var consumable = HotbarConsumableAt(slot);
        if (consumable != null) UseConsumable(consumable);
    }

    /// <summary>Assign a currently-usable attack (class action, basic, or slotted spell) to a
    /// hotbar slot. If it already sits on another slot the two slots swap; otherwise the target's
    /// previous occupant becomes unassigned (still reachable from the Hotbar tab).</summary>
    public bool AssignAttackToHotbar(string attackId, int slot, out string reason)
    {
        reason = "";
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) { reason = "No such slot."; return false; }
        var attack = Hero.Attacks.FirstOrDefault(a => string.Equals(a.Id, attackId, StringComparison.OrdinalIgnoreCase));
        if (attack == null) { reason = "That action is not usable right now."; return false; }

        int existing = Hero.HotbarAssignments.FindIndex(id =>
            string.Equals(id, attackId, StringComparison.OrdinalIgnoreCase));
        string? displaced = Hero.HotbarAssignments[slot];
        Hero.HotbarAssignments[slot] = attack.Id;
        if (existing >= 0 && existing != slot) Hero.HotbarAssignments[existing] = displaced;
        LogMessage($"{attack.Name} assigned to slot {slot + 1}.", MessageKind.System);
        return true;
    }

    /// <summary>Slot a spell straight onto a specific hotbar slot, moving it from the backpack
    /// onto the action bar first if needed.</summary>
    public bool AssignSpellToHotbar(Combinable spell, int slot, out string reason)
    {
        reason = "";
        if (spell is not Spell) { reason = "Only spells can be slotted."; return false; }
        if (Hero.Inventory.Contains(spell))
        {
            // A copy of this spell is already slotted — treat the assign as a move of that copy.
            if (Hero.Loadout.Any(c => string.Equals(c.Id, spell.Id, StringComparison.OrdinalIgnoreCase)))
                return AssignAttackToHotbar(spell.Id, slot, out reason);
            Hero.Inventory.Remove(spell);
            Hero.Loadout.Add(spell);
            RefreshAttacks();
        }
        else if (!Hero.Loadout.Contains(spell))
        {
            reason = "That spell is not in your backpack.";
            return false;
        }
        return AssignAttackToHotbar(spell.Id, slot, out reason);
    }

    /// <summary>Empty a hotbar slot. A slotted spell no longer assigned anywhere returns to the
    /// backpack; class actions just leave the bar (reassignable from the Hotbar tab).</summary>
    public bool ClearHotbarSlot(int slot)
    {
        if (slot < 0 || slot >= Hero.HotbarAssignments.Count) return false;
        string? id = Hero.HotbarAssignments[slot];
        if (id == null) return false;
        Hero.HotbarAssignments[slot] = null;

        var loadoutSpell = Hero.Loadout.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (loadoutSpell != null)
        {
            Hero.Loadout.Remove(loadoutSpell);
            Hero.Inventory.Add(loadoutSpell);
            RefreshAttacks();
            LogMessage($"Removed {loadoutSpell.Name} from the action bar.", MessageKind.System);
            return true;
        }

        if (string.Equals(Hero.CurrentAttack?.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            Hero.CurrentAttack = Enumerable.Range(0, Hero.HotbarAssignments.Count)
                .Select(HotbarAttackAt).FirstOrDefault(a => a != null) ?? Hero.Attacks.FirstOrDefault();
        }
        LogMessage($"Cleared hotbar slot {slot + 1}.", MessageKind.System);
        return true;
    }

    /// <summary>Begin a new multi-tick activity. Cancels and replaces any activity already
    /// in progress (only one can be active at a time).</summary>
    public void StartActivity(Activity activity)
    {
        CurrentActivity?.OnCancel(this);
        CurrentActivity = activity;
        CurrentActivity.OnStart(this);
    }

    /// <summary>Advance the current activity by one tick; finish and clear it once complete.
    /// Called once per Tick() (see the main loop).</summary>
    private void UpdateCurrentActivity()
    {
        if (CurrentActivity == null) return;

        CurrentActivity.OnTick(this);
        if (CurrentActivity.IsComplete)
        {
            var finished = CurrentActivity;
            CurrentActivity = null;
            finished.OnFinish(this);
        }
    }

    // Merge resolution: GrantChestRewards removed in favor of the remote chest rework — chest
    // XP flows through OpenChest's shared-XP grant, and chest loot now lives in the chest's own
    // Inventory (filled at floor generation), so nothing is lost.

    /// <summary>The single validated write path for Hero.Resources — checks the material id
    /// against MaterialDataService before adding, so a typo'd id in recipes.json is caught here
    /// rather than silently growing a phantom inventory entry that never matches any recipe.</summary>
    internal void AddHeroResource(string materialId, int amount)
    {
        if (!MaterialDataService.Instance.IsValidMaterial(materialId))
        {
            GameLog.Debug($"WARNING: unknown material id '{materialId}' — resource not added.");
            return;
        }
        Hero.Resources[materialId] = Hero.Resources.GetValueOrDefault(materialId, 0) + amount;
    }

    // ---- Player-initiated Overworld actions (Press-E structure menus) ---------------------
    // These replace the old scripted OverworldGoal sequence: the player walks up to a structure
    // and chooses an action, rather than the hero auto-walking a fixed mine->craft->sell path.

    /// <summary>How close (tiles) the hero must be to a town structure to interact with it.</summary>
    private const float InteractRadius = 1.3f;

    /// <summary>Refresh the nearest in-range chest or town structure for the contextual prompt.</summary>
    public void UpdateNearbyInteractable()
    {
        if (CurrentActivity != null || Hero.InCombat) { NearbyInteractable = null; return; }

        MazeFeature? nearest = null;
        float nearestSq = InteractRadius * InteractRadius;
        foreach (var f in CurrentMaze.Features)
        {
            bool interactable = IsInOverworld
                ? IsOverworldInteractable(f.Type)
                : IsInSafeRoom
                    ? f.Type is MazeFeatureType.Shrine or MazeFeatureType.GuardianDoor
                    : f.Type == MazeFeatureType.Chest && !f.IsUsed;
            if (!interactable) continue;
            float dx = f.X - Hero.X, dy = f.Y - Hero.Y;
            float d2 = dx * dx + dy * dy;
            if (d2 <= nearestSq) { nearestSq = d2; nearest = f; }
        }
        NearbyInteractable = nearest;
    }

    /// <summary>Mine ore at the Mine (starts a MineOreActivity). No-op if already busy.</summary>
    public void MineOre()
    {
        if (CurrentActivity != null) return;
        StartActivity(new MineOreActivity("iron-ore", 3, GameSettings.Current.SecondsToTicks(5f)));
    }

    /// <summary>Whether the hero currently holds every input a recipe needs.</summary>
    public bool CanCraft(RecipeDef recipe) =>
        recipe.Inputs.All(kv => Hero.Resources.GetValueOrDefault(kv.Key, 0) >= kv.Value);

    /// <summary>Run a crafting recipe at the Smithy (smelt or forge). Validates inputs first; the
    /// CraftActivity itself consumes the inputs and produces the output (material or Weapon via the
    /// loot pipeline). No-op if busy or missing inputs.</summary>
    public bool Craft(RecipeDef recipe)
    {
        if (CurrentActivity != null || !CanCraft(recipe)) return false;
        StartActivity(new CraftActivity(recipe, GameSettings.Current.SecondsToTicks(recipe.DurationSeconds), _ => { }));
        return true;
    }

    /// <summary>Leave town for a fresh dungeon dive (the Dungeon Entrance action).</summary>
    public void EnterDungeon() => StartFreshDungeonDive();

    /// <summary>
    /// Combine two owned Combinables at the Smithy (Forge-gated). Reuses the CombinationEngine/
    /// RecipeBook system built in Phase 1 rather than duplicating it — the Smithy's new recipe-
    /// crafting (ore -> ingot -> gear) and this pre-existing merge system are two different ways
    /// to make things, both available at the same location. Returns null (with a logged reason)
    /// if the combination isn't allowed.
    /// </summary>
    public Combinable? CombineAtForge(Combinable a, Combinable b)
    {
        if (!CombinationEngine.CanCombine(a, b, CombineLocation.Forge, out var reason))
        {
            GameLog.Debug($"Cannot combine at Forge: {reason}");
            return null;
        }
        return CombinationEngine.Combine(a, b);
    }

    /// <summary>Combine two distinct things the hero owns, consuming both only after every
    /// location and ownership check passes. The result is placed in Inventory; callers may equip
    /// a result through the normal inventory flow.</summary>
    public Combinable? CombineOwned(Combinable a, Combinable b, CombineLocation location, out string reason)
    {
        if (!CombinationEngine.CanCombine(a, b, location, out reason)) return null;

        bool ownsA = Hero.Inventory.Contains(a) || Hero.Loadout.Contains(a);
        bool ownsB = Hero.Inventory.Contains(b) || Hero.Loadout.Contains(b);
        if (!ownsA || !ownsB)
        {
            reason = "Both components must be owned by the hero.";
            return null;
        }

        Combinable result = CombinationEngine.Combine(a, b);
        bool loadoutChanged = Hero.Loadout.Remove(a);
        loadoutChanged |= Hero.Loadout.Remove(b);
        Hero.Inventory.Remove(a);
        Hero.Inventory.Remove(b);
        Hero.Inventory.Add(result);
        if (loadoutChanged) RefreshAttacks();

        reason = "";
        LogMessage($"Combined {a.Name} and {b.Name} into {result.Name}.", MessageKind.Loot);
        return result;
    }

    private const int GoldPerRarityPoint = 10;

    /// <summary>
    /// Sell an owned Combinable for gold at the Stall. Price reuses CombinationEngine's
    /// RarityPoints scale (the same one rarity-averaging already uses) rather than a second,
    /// unrelated formula. Works wherever the item lives — inventory, action-bar Loadout, or a
    /// worn Equipment slot (physical gear equips into Equipment, not Loadout, and used to be
    /// unsellable despite this method's contract); returns 0 if the hero doesn't own it.
    /// </summary>
    public int SellItem(Combinable item)
    {
        bool wasEquipped = Hero.Loadout.Remove(item);
        if (!wasEquipped)
        {
            var slot = Hero.Equipment.FirstOrDefault(kv => ReferenceEquals(kv.Value, item));
            if (slot.Value != null)
            {
                Hero.Equipment.Remove(slot.Key);
                wasEquipped = true;
            }
        }
        bool owned = Hero.Inventory.Remove(item) || wasEquipped;
        if (!owned)
        {
            GameLog.Debug($"SellItem: hero does not own '{item.Name}' — no sale.");
            return 0;
        }

        if (wasEquipped) RefreshAttacks();
        int price = SellPrice(item);
        Hero.Gold += price;
        LogMessage($"Sold {item.Name} for {price} gold", MessageKind.Loot);
        return price;
    }

    /// <summary>The gold a Combinable would sell for (rarity-based). Used by the Stall sell UI to
    /// preview prices before committing a sale.</summary>
    public int SellPrice(Combinable item) => CombinationEngine.RarityPoints(item.Rarity) * GoldPerRarityPoint;

    private void CheckFeatures()
    {
        // While an activity (chest-opening, mining, crafting, ...) is active, don't check other
        // features — the activity itself is advanced from Tick() (see CurrentActivity handling).
        if (CurrentActivity != null)
        {
            return;
        }

        // Use directional sight cone for hero to identify features (for far awareness),
        // but do NOT require cone visibility for close interactions like stairs usage.
        float heroFacing = Hero.InCombat && Hero.AttackCooldown == 0 && Hero.CurrentAttack != null
            ? MathF.Atan2(Hero.AnimationOffsetY, Hero.AnimationOffsetX)
            : 0f; // Default facing (e.g., right)
        float heroSightRange = 7.5f;
        float heroConeRad = MathF.PI / 2; // 90-degree cone
        var heroVisibleCells = GetDirectionalSightCone(Hero.X, Hero.Y, heroFacing, heroSightRange, heroConeRad);

        // Automatically pick up nearby items (larger pickup radius than interaction)
        foreach (var feature in CurrentMaze.Features.Where(f => !f.IsUsed).ToList())
        {
            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // Chests never auto-open. Proximity is handled by UpdateNearbyInteractable and E.
            if (feature.Type == MazeFeatureType.Chest) continue;

            if (feature.Type == MazeFeatureType.Trap && distance < 0.5f)
            {
                feature.IsUsed = true;
                TriggerTrap(feature);
                continue;
            }

            // Overworld structures (DungeonEntrance / MineEntrance / Smithy / Stall) are no longer
            // triggered by walking onto them — they're used via the player's Press-E interaction
            // (see MineOre/Craft/EnterDungeon and GameView's structure menus). Proximity detection
            // for the E prompt is UpdateNearbyInteractable, not this feature-trigger loop.

            // Safe-room exits are deliberate Press-E choices. Proximity only exposes the
            // contextual prompt; walking into either feature does nothing.
            if (feature.Type is MazeFeatureType.Shrine or MazeFeatureType.GuardianDoor) continue;

            // For stairs, allow close interaction regardless of cone visibility. No key needed —
            // reaching the stairs is enough; the challenge is the maze distance to get there.
            if (feature.Type == MazeFeatureType.Stairs)
            {
                if (distance < 0.6f)
                {
                    feature.IsUsed = true;
                    AdvancePastFloor();
                    return; // state just reset (new floor or safe room) — stop this tick
                }
                // Far awareness: remember stairs location if in cone OR clear LOS
                int featureCellX = (int)MathF.Round(feature.X);
                int featureCellY = (int)MathF.Round(feature.Y);
                bool inCone = heroVisibleCells.Contains((featureCellX, featureCellY));
                if (inCone || HasLineOfSight(Hero.X, Hero.Y, feature.X, feature.Y))
                {
                    StairsLocation = (feature.X, feature.Y);
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Called when the hero reaches the stairs on a regular floor. The floor just before each
    /// Guardian floor (4, 9, 14, ... — one shy of a multiple of GuardianFloorInterval) leads to
    /// the interstitial safe room ("floor 4.5") instead of straight to the next floor.
    /// </summary>
    private void AdvancePastFloor()
    {
        if (CurrentFloor % GuardianFloorInterval == GuardianFloorInterval - 1)
        {
            EnterSafeRoom();
        }
        else
        {
            StartNewFloor();
        }
    }

    /// <summary>
    /// One-shot environmental hazard: unmitigated burst damage (bypasses defense, unlike combat)
    /// plus a hit flash and a screen-shake bump for feedback.
    /// </summary>
    private void TriggerTrap(MazeFeature trap)
    {
        int damage = 8 + CurrentFloor * 2;
        Hero.CurrentHp = Math.Max(0, Hero.CurrentHp - damage);
        _lastHeroDamageSource = "a trap";
        ScreenShake = MathF.Max(ScreenShake, 4f);
        HitEffects.Add(new HitEffect
        {
            X = trap.X,
            Y = trap.Y,
            LifeTime = 0,
            MaxLifeTime = 10,
            Type = HitEffectType.Impact,
            Team = ProjectileTeam.Enemy
        });
        GameLog.Debug($"Trap triggered! {damage} damage.");
        LogMessage($"A trap springs! -{damage} HP", MessageKind.Warning);
    }

    private void UpdateDungeonThemeFeatures()
    {
        var features = CurrentMaze?.Dungeon?.ThemeFeatures;
        if (features == null || IsInOverworld || IsInSafeRoom) return;

        foreach (var feature in features)
        {
            if (feature.CooldownTicks > 0) feature.CooldownTicks--;

            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            if (dx * dx + dy * dy > 0.36f) continue;

            switch (feature.Type)
            {
                case DungeonThemeFeatureType.CastleAlarm when !feature.IsTriggered:
                    feature.IsTriggered = true;
                    AlertEncounterInRoom(feature, "An alarm bell rings through the castle!");
                    break;

                case DungeonThemeFeatureType.SewerRunoff when feature.CooldownTicks == 0:
                    feature.IsTriggered = true;
                    feature.CooldownTicks = _ticksPerSecond * 3;
                    ApplyThemeHazard(feature, 3 + CurrentFloor,
                        "Caustic runoff burns through your boots!");
                    break;

                case DungeonThemeFeatureType.RestlessGrave when !feature.IsTriggered:
                    feature.IsTriggered = true;
                    int faithDrained = Math.Min(Hero.CurrentFaith, 8 + CurrentFloor);
                    if (faithDrained > 0)
                    {
                        Hero.CurrentFaith -= faithDrained;
                        LogMessage($"The restless grave chills your spirit. -{faithDrained} Faith",
                            MessageKind.Warning);
                    }
                    else
                    {
                        ApplyThemeHazard(feature, 4 + CurrentFloor,
                            "The restless grave feeds on your life!");
                    }
                    break;

                case DungeonThemeFeatureType.ArcaneWard when !feature.IsTriggered:
                    feature.IsTriggered = true;
                    int manaDrained = Math.Min(Hero.CurrentMana, 10 + CurrentFloor * 2);
                    if (manaDrained > 0)
                    {
                        Hero.CurrentMana -= manaDrained;
                        LogMessage($"The ward consumes {manaDrained} Mana and falls dark.",
                            MessageKind.Warning);
                    }
                    else
                    {
                        ApplyThemeHazard(feature, 5 + CurrentFloor,
                            "The ward lashes out at your empty reserves!");
                    }
                    break;

                case DungeonThemeFeatureType.HeatVent when feature.CooldownTicks == 0:
                    feature.IsTriggered = true;
                    feature.CooldownTicks = _ticksPerSecond * 4;
                    ApplyThemeHazard(feature, 5 + CurrentFloor,
                        "The forge vent erupts beneath you!");
                    break;

                case DungeonThemeFeatureType.HideoutTripwire when !feature.IsTriggered:
                    feature.IsTriggered = true;
                    AlertEncounterInRoom(feature, "A tripwire rattles the hideout's warning cans!");
                    break;
            }
        }
    }

    private void AlertEncounterInRoom(DungeonThemeFeature feature, string message)
    {
        var roomEncounter = CurrentMaze.Dungeon?.Encounters
            .FirstOrDefault(encounter => encounter.HomeRoomId == feature.RoomId);
        if (roomEncounter != null)
        {
            foreach (var enemy in Enemies.Where(enemy =>
                         enemy.IsAlive && enemy.EncounterId == roomEncounter.Id))
            {
                enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
                _enemyLastKnownHeroPos[enemy] = (Hero.X, Hero.Y);
            }
        }

        _heroAlertTicks = _alertTicks;
        LogMessage(message, MessageKind.Warning);
    }

    private void ApplyThemeHazard(DungeonThemeFeature feature, int damage, string message)
    {
        Hero.CurrentHp = Math.Max(0, Hero.CurrentHp - damage);
        _lastHeroDamageSource = ThemeHazardDeathCause(feature.Type);
        ScreenShake = MathF.Max(ScreenShake, 4f);
        HitEffects.Add(new HitEffect
        {
            X = feature.X,
            Y = feature.Y,
            LifeTime = 0,
            MaxLifeTime = 10,
            Type = HitEffectType.Impact,
            Team = ProjectileTeam.Enemy
        });
        LogMessage($"{message} -{damage} HP", MessageKind.Warning);
    }

    /// <summary>How a theme hazard reads in a legacy record's cause of death.</summary>
    private static string ThemeHazardDeathCause(DungeonThemeFeatureType type) => type switch
    {
        DungeonThemeFeatureType.CastleAlarm => "a castle alarm's defences",
        DungeonThemeFeatureType.SewerRunoff => "toxic sewer runoff",
        DungeonThemeFeatureType.RestlessGrave => "a restless grave",
        DungeonThemeFeatureType.ArcaneWard => "an arcane ward",
        DungeonThemeFeatureType.HeatVent => "a forge heat vent",
        DungeonThemeFeatureType.HideoutTripwire => "a hideout tripwire",
        _ => "the dungeon itself"
    };

    /// <summary>
    /// Each tick, give the hero a Wisdom-based chance to notice nearby hidden traps (within
    /// PerceptionRadius, with line of sight). Noticing one reveals it and pops the "!" alert.
    /// </summary>
    private void UpdatePerception()
    {
        if (CurrentMaze == null) return;
        foreach (var feature in CurrentMaze.Features)
        {
            if (feature.IsUsed || !feature.Hidden || feature.Perceived) continue;
            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > PerceptionService.PerceptionRadius) continue;
            if (!HasLineOfSight(Hero.X, Hero.Y, feature.X, feature.Y)) continue;

            float chance = PerceptionService.SpotChancePerTick(Hero.EffectiveWisdom, distance, CurrentFloor);
            if (_random.NextDouble() < chance)
            {
                feature.Perceived = true;
                _heroAlertTicks = _alertTicks;
                LogMessage("You spot a trap!", MessageKind.Warning);
            }
        }
    }

    /// <summary>Right-click "Examine": reveal a hidden feature (mark it perceived) and describe it.</summary>
    public void ExamineFeature(MazeFeature feature)
    {
        if (feature == null || feature.IsUsed) return;
        if (feature.Hidden && !feature.Perceived)
        {
            feature.Perceived = true;
            _heroAlertTicks = _alertTicks;
        }
        string what = feature.Type switch
        {
            MazeFeatureType.Trap => "a pressure-plate trap — step carefully, or disarm it",
            MazeFeatureType.Chest => "a chest",
            _ => feature.Type.ToString()
        };
        LogMessage($"You examine {what}.", MessageKind.System);
    }

    /// <summary>Right-click "Disarm" on a perceived trap: a Dexterity roll; a failure may spring it.</summary>
    public void TryDisarm(MazeFeature feature)
    {
        if (feature == null || feature.IsUsed || feature.Type != MazeFeatureType.Trap || !feature.Perceived) return;

        float chance = PerceptionService.DisarmChance(Hero.EffectiveDexterity, CurrentFloor);
        if (_random.NextDouble() < chance)
        {
            feature.IsUsed = true;
            LogMessage("You disarm the trap.", MessageKind.System);
        }
        else if (_random.NextDouble() < PerceptionService.DisarmFailSpringChance)
        {
            feature.IsUsed = true;
            LogMessage("You fumble the disarm — it springs!", MessageKind.Warning);
            TriggerTrap(feature);
        }
        else
        {
            LogMessage("The disarm slips; the trap holds. Try again.", MessageKind.System);
        }
    }

    /// <summary>Right-click "Examine" on a corpse: flavor/identify.</summary>
    private bool CanReachChest(MazeFeature chest)
    {
        if (chest == null || chest.Type != MazeFeatureType.Chest || chest.IsUsed ||
            Hero.InCombat || CurrentActivity != null || !CurrentMaze.Features.Contains(chest)) return false;
        float dx = chest.X - Hero.X;
        float dy = chest.Y - Hero.Y;
        return dx * dx + dy * dy <= InteractRadius * InteractRadius;
    }

    public bool HasChestKey(MazeFeature chest) => Hero.Inventory.OfType<Item>()
        .Any(item => item.KeyId.Length > 0 && item.KeyId == chest.RequiredKeyId);

    public bool LookForChestTraps(MazeFeature chest)
    {
        if (!CanReachChest(chest) || chest.IsOpened) return false;
        chest.TrapChecked = true;
        if (!chest.IsTrapped)
        {
            LogMessage("You find no sign of a trap.", MessageKind.System);
            return true;
        }

        double chance = Math.Clamp(0.45 + Hero.EffectiveWisdom * 0.04 - CurrentFloor * 0.025, 0.2, 0.95);
        if (_random.NextDouble() < chance)
        {
            chest.ChestTrapDetected = true;
            LogMessage("You find a trap wired into the chest latch.", MessageKind.Warning);
        }
        else
        {
            LogMessage("You cannot make out anything suspicious.", MessageKind.System);
        }
        return true;
    }

    public bool TryDisarmChestTrap(MazeFeature chest)
    {
        if (!CanReachChest(chest) || !chest.ChestTrapDetected || chest.TrapDisarmed) return false;
        double chance = Math.Clamp(0.4 + Hero.EffectiveDexterity * 0.045 - CurrentFloor * 0.025, 0.15, 0.95);
        if (_random.NextDouble() < chance)
        {
            chest.TrapDisarmed = true;
            LogMessage("You disarm the chest trap.", MessageKind.System);
        }
        else
        {
            LogMessage("The mechanism slips and the trap springs!", MessageKind.Warning);
            TriggerChestTrap(chest);
        }
        return true;
    }

    public bool TryLockpickChest(MazeFeature chest)
    {
        if (!CanReachChest(chest) || !chest.IsLocked) return false;
        double chance = Math.Clamp(0.35 + Hero.EffectiveDexterity * 0.045 - CurrentFloor * 0.02, 0.12, 0.9);
        if (_random.NextDouble() < chance)
        {
            chest.IsLocked = false;
            LogMessage("The lock clicks open.", MessageKind.System);
        }
        else
        {
            LogMessage("The lockpick slips.", MessageKind.System);
            if (chest.IsTrapped && !chest.TrapDisarmed && _random.NextDouble() < 0.3)
                TriggerChestTrap(chest);
        }
        return true;
    }

    public bool UseChestKey(MazeFeature chest)
    {
        if (!CanReachChest(chest) || !chest.IsLocked) return false;
        Item? key = Hero.Inventory.OfType<Item>()
            .FirstOrDefault(item => item.KeyId.Length > 0 && item.KeyId == chest.RequiredKeyId);
        if (key == null) return false;
        Hero.Inventory.Remove(key);
        chest.IsLocked = false;
        LogMessage("The key fits. The chest unlocks.", MessageKind.System);
        return true;
    }

    public bool OpenChest(MazeFeature chest)
    {
        if (!CanReachChest(chest) || chest.IsLocked) return false;
        if (chest.IsTrapped && !chest.TrapDisarmed) TriggerChestTrap(chest);
        chest.IsOpened = true;
        if (!chest.OpenRewardGranted)
        {
            chest.OpenRewardGranted = true;
            int awarded = ProgressionService.Instance.GrantSharedXp(Hero, 25);
            LogMessage($"Opened a chest (+{awarded} shared XP).", MessageKind.Loot);
        }
        return true;
    }

    private void TriggerChestTrap(MazeFeature chest)
    {
        if (chest.TrapDisarmed) return;
        chest.TrapDisarmed = true;
        chest.ChestTrapDetected = true;
        int damage = 8 + CurrentFloor * 2;
        Hero.CurrentHp = Math.Max(0, Hero.CurrentHp - damage);
        _lastHeroDamageSource = "a chest trap";
        ScreenShake = MathF.Max(ScreenShake, 4f);
        LogMessage($"The chest trap hits you for {damage} damage!", MessageKind.Warning);
    }

    public bool LootChestItem(MazeFeature chest, Combinable item)
    {
        if (!CanReachChest(chest) || !chest.IsOpened || !chest.Inventory.Remove(item)) return false;
        AcquireLoot(item);
        FinishEmptyChest(chest);
        return true;
    }

    public bool LootChestGold(MazeFeature chest)
    {
        if (!CanReachChest(chest) || !chest.IsOpened || chest.Gold <= 0) return false;
        int amount = chest.Gold;
        chest.Gold = 0;
        Hero.Gold += amount;
        LogMessage($"Looted {amount} gold.", MessageKind.Loot);
        FinishEmptyChest(chest);
        return true;
    }

    public void LootAll(MazeFeature chest)
    {
        if (!CanReachChest(chest) || !chest.IsOpened) return;
        foreach (Combinable item in chest.Inventory.ToList()) LootChestItem(chest, item);
        LootChestGold(chest);
        FinishEmptyChest(chest);
    }

    private void FinishEmptyChest(MazeFeature chest)
    {
        if (chest.Inventory.Count > 0 || chest.Gold > 0) return;
        chest.IsUsed = true;
        if (ReferenceEquals(NearbyInteractable, chest)) NearbyInteractable = null;
    }

    public void ExamineCorpse(Enemy enemy)
    {
        if (enemy == null || enemy.IsAlive) return;
        int items = enemy.Inventory.Count;
        string carry = items == 0 && enemy.Gold == 0 ? "nothing of worth"
            : $"{items} item(s)" + (enemy.Gold > 0 ? $" and {enemy.Gold} gold" : "");
        LogMessage($"The corpse of a level {enemy.Level} {enemy.Race} {enemy.Class}. It carries {carry}.", MessageKind.System);
    }

    /// <summary>Move one item from a corpse to the hero's inventory. Returns true on success.
    /// Corpses only — a living enemy's inventory is theirs (same guard LootGold has).</summary>
    public bool LootItem(Enemy corpse, Combinable item)
    {
        if (corpse == null || corpse.IsAlive || item == null || !corpse.Inventory.Remove(item)) return false;
        AcquireLoot(item);
        return true;
    }

    /// <summary>Take the body's gold as an individual loot entry.</summary>
    public bool LootGold(Enemy corpse)
    {
        if (corpse == null || corpse.IsAlive || corpse.Gold <= 0) return false;
        int amount = corpse.Gold;
        corpse.Gold = 0;
        Hero.Gold += amount;
        LogMessage($"Looted {amount} gold.", MessageKind.Loot);
        return true;
    }

    /// <summary>Auto mode: scoop up loot from any corpse the hero is standing on (Manual mode
    /// loots deliberately via right-click instead).</summary>
    private void AutoLootNearbyCorpses()
    {
        foreach (var corpse in Enemies)
        {
            if (corpse.IsAlive) continue;
            if (corpse.Inventory.Count == 0 && corpse.Gold == 0) continue;
            float dx = Hero.X - corpse.X;
            float dy = Hero.Y - corpse.Y;
            if (dx * dx + dy * dy <= 0.8f * 0.8f) LootAll(corpse);
        }
    }

    /// <summary>Move one item from the hero's inventory back onto a corpse (drag player→body).</summary>
    public bool DepositToCorpse(Enemy corpse, Combinable item)
    {
        if (corpse == null || corpse.IsAlive || item == null || !Hero.Inventory.Remove(item)) return false;
        corpse.Inventory.Add(item);
        return true;
    }

    /// <summary>Take everything (items + gold) from a corpse. Corpses only.</summary>
    public void LootAll(Enemy corpse)
    {
        if (corpse == null || corpse.IsAlive) return;
        foreach (var item in corpse.Inventory.ToList())
        {
            corpse.Inventory.Remove(item);
            AcquireLoot(item);
        }
        LootGold(corpse);
    }

    /// <summary>Spawns the gate Guardian once the hero approaches the safe room's door.
    /// Reuses the same boss-tier generation regular floors used to use — a Guardian is simply
    /// the dungeon's boss concept, now confined to its own room instead of wandering a maze.
    /// The fight itself IS the Guardian floor (5, 10, 15, ...): entering it advances
    /// CurrentFloor from e.g. 4 (safe room "4.5") to 5, so the Guardian's level scales off the
    /// Guardian floor and victory proceeds to floor 6.</summary>
    public void EnterGuardianChamber()
    {
        if (!IsInSafeRoom || NearbyInteractable?.Type != MazeFeatureType.GuardianDoor) return;

        IsInSafeRoom = false;
        CurrentFloor++;
        CurrentMaze = GenerateGuardianChamber();
        Hero.X = 1;
        Hero.Y = CurrentMaze.Height / 2;
        NearbyInteractable = null;
        Enemies.Clear();
        Projectiles.Clear();
        HitEffects.Clear();

        Boss = EnemyFactory.RandomBoss(CurrentFloor, _characterDataService, _random);
        Boss.X = CurrentMaze.Width - 3;
        Boss.Y = CurrentMaze.Height / 2;
        Boss.TargetX = Boss.X;
        Boss.TargetY = Boss.Y;
        Enemies.Add(Boss);
        GameLog.Debug($"Guardian spawned: {Boss.Race} {Boss.Class} (Level {Boss.Level})");
        LogMessage($"Floor {CurrentFloor}: the Guardian chamber. {Boss.Race} {Boss.Class}, level {Boss.Level}, bars the way!", MessageKind.Warning);
    }

    public void UseSafeRoomShrine()
    {
        if (!IsInSafeRoom || NearbyInteractable?.Type != MazeFeatureType.Shrine) return;
        EnterOverworld();
    }

    /// <summary>
    /// Enter the interstitial safe room between floor groups. A small, fully-open, already-lit
    /// room (not another maze) with a shrine and a Guardian door on the far side.
    /// </summary>
    private void EnterSafeRoom()
    {
        IsInSafeRoom = true;
        Boss = null;
        Enemies.Clear();
        Projectiles.Clear();
        HitEffects.Clear();
        StairsLocation = null;

        CurrentMaze = GenerateSafeRoomMaze();
        Hero.X = 1;
        Hero.Y = CurrentMaze.Height / 2;
        ControlMode = ControlMode.Manual;
        LogMessage("A safe room between floors. Approach the door to challenge the next Guardian, or the shrine to return to town. Progress saved.", MessageKind.System);

        // Safe rooms are the only mid-dungeon save points: checkpoint automatically on entry.
        // Continuing this save resumes back in this safe room (SaveData.SafeRoomFloor).
        SaveService.Save(this);
    }

    private Maze GenerateSafeRoomMaze()
    {
        const int width = 15;
        const int height = 9;
        int midY = height / 2;

        var maze = new Maze(width, height) { FloorNumber = CurrentFloor };
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                maze.Tiles[x, y] = isBorder ? TileType.Wall : TileType.Floor;
                if (!isBorder) maze.Explored[x, y] = true; // fully lit; nothing to explore
            }
        }

        maze.Features.Add(new MazeFeature { X = width / 2, Y = midY, Type = MazeFeatureType.Shrine });
        maze.Features.Add(new MazeFeature { X = width - 2, Y = midY, Type = MazeFeatureType.GuardianDoor });
        return maze;
    }

    private Maze GenerateGuardianChamber()
    {
        const int width = 15;
        const int height = 9;
        var maze = new Maze(width, height) { FloorNumber = CurrentFloor };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            bool border = x == 0 || x == width - 1 || y == 0 || y == height - 1;
            maze.Tiles[x, y] = border ? TileType.Wall : TileType.Floor;
            if (!border) maze.Explored[x, y] = true;
        }
        return maze;
    }

    /// <summary>
    /// Leaving the dungeon via a safe-room shrine. Places the hero at the Overworld's dungeon
    /// entrance, preserving the hero completely (level/stats/gear — unlike death, which resets
    /// everything). This is the real Overworld hand-off the old placeholder comment anticipated.
    /// </summary>
    private void EnterOverworld()
    {
        CodexService.Instance.RecordDungeonExit(CurrentFloor);

        // EnterTown clears the dungeon actors/projectiles and safe-room state.
        EnterTown();
        LogMessage("You emerge into the town by the dungeon mouth. Progress saved.", MessageKind.System);

        // Auto-save checkpoint: the hero just survived leaving the dungeon, bank that progress
        // to disk. There's no player-driven "Save" action yet (no input system at all), so this
        // is the natural automatic checkpoint until one exists.
        SaveService.Save(this);
    }

    /// <summary>
    /// Put the hero in the town: the shared body of arriving there, whichever way they came.
    /// Used by the shrine hand-off (EnterOverworld), an Overworld-resume load, and a character who
    /// chose StartLocation.Town at creation.
    /// </summary>
    private void EnterTown()
    {
        // Arriving in town always means leaving the dungeon behind, whichever route led here.
        // The clears live in this shared body because the Overworld-resume load path used to
        // skip them, leaking the constructor's floor-1 enemies into the town map.
        Enemies.Clear();
        Projectiles.Clear();
        HitEffects.Clear();
        Boss = null;
        StairsLocation = null;
        IsInSafeRoom = false;

        IsInOverworld = true;
        NearbyInteractable = null;
        ControlMode = ControlMode.Manual; // the town is player-driven (WASD + Press-E)
        CurrentMaze = BuildTownMap();
        PlaceHeroAtOverworldArrival();
    }

    /// <summary>
    /// A character created with StartLocation.Town begins as a resident rather than a newly-sentient
    /// being in the dungeon. Called right after construction (which always builds a floor-1 dive, the
    /// canonical opening) to redirect them into the town instead — the dungeon is then somewhere they
    /// choose to go, via the entrance's Press-E.
    /// </summary>
    public void StartInTown()
    {
        CurrentFloor = 0; // they've never been down; a fresh dive still starts at floor 1

        // EnterTown clears the constructor's floor-1 dive (actors, projectiles, stairs).
        EnterTown();
        Messages.Clear(); // drop StartNewFloor's "you descend" opener — it never happened
        LogMessage($"{Hero.Name} wakes in the town beside the dungeon mouth.", MessageKind.System);
    }

    /// <summary>
    /// Restore a hero's progress from a save after the appropriate legacy or equipment-first
    /// constructor has established race effectiveness. Saved stats, inventory, progression, and
    /// the checkpoint location then replace the fresh state.
    /// </summary>
    public void LoadFrom(SaveData data)
    {
        SaveId = data.SaveId;
        // Older saves predate the world layout; the active scope they were just loaded from is the
        // correct answer for them, which the lazy resolve supplies.
        if (!string.IsNullOrEmpty(data.WorldId)) _worldId = data.WorldId;
        _priorPlaytimeSeconds = data.PlaytimeSeconds;
        _characterName = data.HeroName;
        _className = data.ClassName;
        _raceName = data.RaceName;
        _creationSelection = data.CreationSelection?.Clone();

        Hero.Name = data.HeroName;
        if (string.Equals(data.ClassName, "Classless", StringComparison.OrdinalIgnoreCase))
        {
            Hero.Class = "Classless";
            Hero.ClassData = null;
            Hero.ClassColor = "#808080";
        }
        else if (!string.Equals(Hero.Class, data.ClassName, StringComparison.OrdinalIgnoreCase) ||
                 Hero.ClassData == null)
            _characterDataService.ApplyClassIdentity(Hero, data.ClassName);

        Hero.Level = data.Level;
        Hero.Experience = data.Experience;
        Hero.ExperienceToNext = data.ExperienceToNext;
        Hero.MaxHp = data.MaxHp;
        Hero.CurrentHp = data.CurrentHp;
        Hero.Strength = data.Strength;
        Hero.Constitution = data.Constitution;
        Hero.Agility = data.Agility;
        Hero.Dexterity = data.Dexterity;
        Hero.Intelligence = data.Intelligence;
        Hero.Wisdom = data.Wisdom;
        Hero.Charisma = data.Charisma;
        Hero.UnspentStatPoints = data.UnspentStatPoints;
        Hero.Progression = data.Progression ?? ProgressionService.Instance.CreateCompatibilityState(
            data.ClassName, data.Level, data.Experience);
        ProgressionService.Instance.Normalize(Hero);
        Hero.Gold = data.Gold;
        Hero.Kills = data.Kills;
        Hero.Resources = new Dictionary<string, int>(data.Resources);
        Hero.Loadout = new List<Combinable>(data.Loadout
            .Where(item => item is Spell && !LegacyClassActionIds.Contains(item.Id)));
        Hero.Inventory = new List<Combinable>(data.Inventory);
        Hero.Equipment = new Dictionary<EquipmentSlot, Combinable>(data.Equipment ?? new());
        Hero.WeaponTraining = new HashSet<WeaponType>(data.WeaponTraining ?? new());
        Hero.HotbarAssignments = data.HotbarAssignments != null
            ? new List<string?>(data.HotbarAssignments) : new List<string?>();
        Hero.HotbarKnownIds = data.HotbarKnownIds != null
            ? new HashSet<string>(data.HotbarKnownIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Grow-with-use elemental progress survives the restart (remote's typed Affinities model
        // supersedes the local string-dict version — one restore path, not two).
        if (data.Affinities != null) Hero.Affinities = data.Affinities.Clone();
        MigrateLegacyLoadout(data);
        RefreshAttacks();

        // The constructor derived stamina/mana/faith maxima from the fresh character's stats;
        // the loaded stats may be far higher. Re-derive, then fill — currents aren't persisted,
        // and every resume point (town, safe room) is restful anyway.
        UpdateHeroResourcePools();
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;

        // World time resumes where it was (0 = a save predating the clock; keep the default).
        if (data.WorldGameMinutes > 0)
            Clock.TotalGameMinutes = data.WorldGameMinutes;

        if (data.ResumePoint == ResumePoint.SafeRoom && data.SafeRoomFloor.HasValue)
        {
            // Resume at the safe-room checkpoint (e.g. floor "4.5"). EnterSafeRoom rebuilds the
            // room, places the hero, and re-checkpoints (a harmless identical re-save).
            CurrentFloor = data.SafeRoomFloor.Value;
            IsInOverworld = false;
            EnterSafeRoom();
        }
        else if (data.ResumePoint == ResumePoint.DungeonStart)
        {
            // A creation-time save: the character never reached a safe room, so there's no
            // Overworld to stand in — the constructor already spawned them into a fresh floor 1,
            // which is exactly where this resume belongs. Nothing further to do.
        }
        else
        {
            EnterTown();
        }
    }

    /// <summary>
    /// The town map for this character's world. Generated from the world's own seed and size, so
    /// every character in a world walks the same town and the same seed always rebuilds it — which
    /// is what "frozen per world" means in practice while the map itself isn't yet serialized (the
    /// generator is deterministic and version-pinned by the world's recorded seed).
    ///
    /// Falls back to the small hand-authored field if the world can't be resolved (headless demos
    /// that never established a world scope) or if the `towngen` debug command asked for an
    /// explicit size.
    /// </summary>
    private Maze BuildTownMap()
    {
        if (_townSizeOverride)
            return OverworldGenerator.Generate(_townWidth, _townHeight);

        try
        {
            var world = WorldService.Load(WorldId);
            if (world != null)
            {
                var region = RegionGenerator.Generate(world.Options, out _);
                // Re-apply what's been dug or broken here since the world was made, so a tunnel
                // outlives the character who cut it.
                ApplyTerrainChanges(region);
                return region;
            }
        }
        catch (Exception ex)
        {
            // A region that can't be generated must not strand the player in a broken town.
            GameLog.Debug($"Region generation failed, falling back to the simple field: {ex.Message}");
        }

        return OverworldGenerator.Generate(_townWidth, _townHeight);
    }

    /// <summary>Arrival spot when entering (or resuming in) the town: one tile off the dungeon
    /// entrance, so the hero isn't standing inside the structure they'd interact with and the
    /// return trip stays an explicit "Enter the Dungeon" action. The single authority for this —
    /// when the generated town lands, only this method changes.</summary>
    private void PlaceHeroAtOverworldArrival()
    {
        // Feature lookup, never coordinates (note 01 §0.3) — but a generated map that somehow
        // lacks the entrance must degrade to a walkable spawn, not crash the load/exit path.
        var entrance = CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.DungeonEntrance);
        if (entrance == null)
        {
            GameLog.Debug("Town map has no DungeonEntrance feature — spawning at a fallback walkable cell.");
            var fallback = CurrentMaze.GetEmptyCells().FirstOrDefault();
            Hero.X = fallback.x;
            Hero.Y = fallback.y;
            return;
        }
        // Prefer the tile one east of the entrance; if that generated blocked, take any
        // walkable neighbour rather than standing inside the structure.
        if (CurrentMaze.IsWalkable(entrance.X + 1, entrance.Y))
        {
            Hero.X = entrance.X + 1;
            Hero.Y = entrance.Y;
            return;
        }
        foreach (var (dx, dy) in new[] { (-1, 0), (0, 1), (0, -1) })
        {
            if (CurrentMaze.IsWalkable(entrance.X + dx, entrance.Y + dy))
            {
                Hero.X = entrance.X + dx;
                Hero.Y = entrance.Y + dy;
                return;
            }
        }
        Hero.X = entrance.X;
        Hero.Y = entrance.Y;
    }

    // --- Mining: rock is terrain you destroy, not a button you press ---

    /// <summary>The rock tile the hero could start working on: the nearest minable neighbour, so
    /// mining is "face the rock and dig" rather than a menu of the whole map.</summary>
    public (int x, int y)? MinableRockNearby()
    {
        int hx = Hero.GridX;
        int hy = Hero.GridY;
        (int x, int y)? best = null;
        float bestDistance = float.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = hx + dx;
                int y = hy + dy;
                if (!MiningService.CanMine(CurrentMaze, x, y)) continue;

                float distance = MathF.Abs(dx) + MathF.Abs(dy);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = (x, y);
            }
        }
        return best;
    }

    /// <summary>
    /// Begin digging out an adjacent tile of rock. Fails quietly if the hero is already busy or
    /// there's nothing workable there — bedrock included, which never yields (the map's rim and the
    /// dungeon's threshold).
    /// </summary>
    public bool StartMining(int x, int y)
    {
        if (CurrentActivity != null || IsHeroDead) return false;
        if (!MiningService.CanMine(CurrentMaze, x, y)) return false;

        int distance = Math.Max(Math.Abs(x - Hero.GridX), Math.Abs(y - Hero.GridY));
        if (distance > 1) return false;

        var tile = CurrentMaze.Tiles[x, y];
        int miningLevel = Hero.Progression.Skills.TryGetValue("mining", out var skill) ? skill.Level : 0;
        int ticks = MiningService.WorkTicks(tile, Hero.EffectiveStrength, miningLevel);
        StartActivity(new MineRockActivity(x, y, tile, ticks));
        return true;
    }

    /// <summary>The rock gives: the tile becomes open ground and its contents go to the pack.</summary>
    public void CompleteRockMining(int x, int y, TileType expected)
    {
        if (!CurrentMaze.InBounds(x, y)) return;
        var tile = CurrentMaze.Tiles[x, y];
        if (tile != expected || !MiningService.CanMine(tile)) return;

        CurrentMaze.Tiles[x, y] = TileType.Floor;
        CurrentMaze.Explored[x, y] = true;
        RecordTerrainChange(x, y, TileType.Floor);

        var yield = MiningService.Roll(tile, _random, MiningDepthAt(x, y));
        foreach (var (materialId, amount) in yield.Materials)
        {
            // Roll the skill-adjusted amount ONCE: SkillAdjustedYield has a probabilistic
            // round-up, so a second call for the log could report a different number than was
            // granted — and the extra draw perturbed the seeded RNG stream the TEST_* demos
            // depend on.
            int granted = SkillAdjustedYield("mining", amount);
            AddHeroResource(materialId, granted);
            var name = MaterialDataService.Instance.Materials.TryGetValue(materialId, out var def)
                ? def.Name : materialId;
            LogMessage($"Mined {granted}x {name}", MessageKind.Loot);
        }
        if (yield.StruckGem)
        {
            LogMessage("A gem glints in the broken rock!", MessageKind.LevelUp);
            AddFloatingText("Gem!", x, y - 0.4f, FloatingTextKind.LevelUp);
        }

        RecordProgressionFact("practice.mining-actions");
        var progress = RecordProfessionAction("miner");
        if (progress.Success && progress.SkillXpApplied > 0)
            LogMessage($"Miner +{progress.XpApplied} XP; Mining +{progress.SkillXpApplied} XP", MessageKind.System);

        RaiseMiningNoise(x, y, tile);
    }

    /// <summary>
    /// Mining is loud. In the dungeon it's far louder — you are hammering on the walls of the thing
    /// you are trespassing inside, and it notices (owner ruling 2026-08-06). Anything within earshot
    /// comes to look, whether or not it can see you.
    /// </summary>
    public void RaiseMiningNoise(int x, int y, TileType tile)
    {
        bool inDungeon = !IsInOverworld && !IsInSafeRoom;
        float radius = MiningService.NoiseRadius(tile, inDungeon);
        float radiusSquared = radius * radius;
        int roused = 0;

        foreach (var enemy in Enemies)
        {
            if (!enemy.IsAlive) continue;
            float dx = enemy.X - x;
            float dy = enemy.Y - y;
            if (dx * dx + dy * dy > radiusSquared) continue;

            // The same pursuit machinery LOS uses, but triggered by sound: they head for the noise
            // rather than for you, which is what makes digging a decision rather than a free action.
            enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
            _enemyLastKnownHeroPos[enemy] = (x, y);
            roused++;
        }

        if (inDungeon && _miningNoiseWarnings < 1)
        {
            _miningNoiseWarnings++;
            LogMessage("The sound of your pick rings through the stone. Something is listening.",
                MessageKind.Warning);
        }
        if (roused > 0)
            LogMessage($"The noise draws attention — {roused} nearby.", MessageKind.Warning);
    }

    /// <summary>How deep into the rock this tile is, for richer seams further in. Measured as the
    /// distance to the nearest open ground, which is exactly "how far from daylight".</summary>
    private int MiningDepthAt(int x, int y)
    {
        for (int radius = 1; radius <= 40; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                    if (CurrentMaze.IsWalkable(x + dx, y + dy)) return radius;
                }
            }
        }
        return 40;
    }

    private int _miningNoiseWarnings;

    /// <summary>
    /// Remember that the world changed shape here. The dungeon is transient per dive, so only the
    /// region's terrain is worth persisting — a tunnel you dug should still be there tomorrow.
    /// </summary>
    private void RecordTerrainChange(int x, int y, TileType tile)
    {
        if (!IsInOverworld || string.IsNullOrEmpty(WorldId)) return;
        var delta = WorldService.LoadDelta(WorldId);
        delta.TileChanges[$"{x},{y}"] = tile.ToString();
        WorldService.SaveDelta(WorldId, delta);
    }

    /// <summary>Re-apply everything previous characters dug, built or broke in this world.</summary>
    private void ApplyTerrainChanges(Maze maze)
    {
        if (string.IsNullOrEmpty(WorldId)) return;
        var delta = WorldService.LoadDelta(WorldId);
        foreach (var (key, value) in delta.TileChanges)
        {
            var parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
                continue;
            if (!maze.InBounds(x, y) || !Enum.TryParse(value, out TileType tile)) continue;
            maze.Tiles[x, y] = tile;
            if (tile.IsPassable()) maze.Explored[x, y] = true;
        }
    }

    /// <summary>The town structures the hero can interact with via Press-E (proximity-detected in
    /// UpdateNearbyInteractable).</summary>
    private static bool IsOverworldInteractable(MazeFeatureType t) => t switch
    {
        MazeFeatureType.MineEntrance or MazeFeatureType.Smithy or
        MazeFeatureType.Stall or MazeFeatureType.DungeonEntrance => true,
        _ => false
    };

    /// <summary>
    /// The return trip: walking onto the Overworld's dungeon entrance starts a fresh dive.
    /// This is the reseed-and-restart logic that used to run immediately on shrine-exit, before
    /// the Overworld existed to place the hero in instead.
    /// </summary>
    private void StartFreshDungeonDive()
    {
        // Entry-time snapshot, taken while still standing in the Overworld so it records an
        // OverworldEntrance resume: closing the game mid-dungeon reverts to the dungeon entrance
        // with the state the hero carried in (regular floors themselves are never saved).
        SaveService.Save(this);

        IsInOverworld = false;
        CurrentFloor = 0; // StartNewFloor increments to 1
        Seed = new Random().Next();
        _random = new Random(Seed);
        _mazeGenerator = new MazeGenerator(Seed);
        _movementSystem = new MovementSystem(Seed);
        _combatSystem = new CombatSystem(Seed);

        StartNewFloor();
    }

    /// <summary>
    /// Returns true if the enemy can see the hero by scanning multiple facing angles
    /// </summary>
    private bool EnemyCanSeeHero(Enemy enemy)
    {
        // Enemies watch all around (this used to scan 8 overlapping 90° sight cones — a full
        // circle), so "can see" reduces to: hero's cell within vision range of the enemy's
        // position, with line of sight to that cell center. Same answer as the cone union, but
        // one Bresenham walk instead of ~8 × range² of them per enemy per tick.
        int heroCellX = (int)MathF.Round(Hero.X);
        int heroCellY = (int)MathF.Round(Hero.Y);
        float dx = heroCellX - enemy.X;
        float dy = heroCellY - enemy.Y;
        if (dx * dx + dy * dy > VisionRange * VisionRange) return false;
        return HasLineOfSight(enemy.X, enemy.Y, heroCellX, heroCellY);
    }
    
    private bool HasLineOfSight(float x1, float y1, float x2, float y2) =>
        SightLine.Clear(CurrentMaze, x1, y1, x2, y2);
    
    /// <summary>Extra MaxHp granted per Constitution point spent, scaled by racial effectiveness —
    /// mirrors the level-up HP formula's Constitution contribution so raising Con pays off in HP
    /// immediately rather than only affecting future level-ups.</summary>
    private const int HpPerConstitutionPoint = 5;

    /// <summary>The seven allocatable core stats, in display order (also the valid names for
    /// SpendStatPoint).</summary>
    public static readonly string[] CoreStatNames =
        { "Strength", "Constitution", "Agility", "Dexterity", "Intelligence", "Wisdom", "Charisma" };

    /// <summary>Spend one banked level-up point on a core stat (manual allocation). Returns false
    /// if there are no unspent points or the stat name isn't recognized. Increments the base stat,
    /// refreshes the derived resource caps, and — for Constitution — bumps MaxHp so the payoff is
    /// immediate. Live combat formulas read Effective* stats, so everything else applies at once.</summary>
    public bool SpendStatPoint(string stat)
    {
        if (Hero.UnspentStatPoints <= 0) return false;

        switch (stat)
        {
            case "Strength": Hero.Strength++; break;
            case "Constitution": Hero.Constitution++; break;
            case "Agility": Hero.Agility++; break;
            case "Dexterity": Hero.Dexterity++; break;
            case "Intelligence": Hero.Intelligence++; break;
            case "Wisdom": Hero.Wisdom++; break;
            case "Charisma": Hero.Charisma++; break;
            default: return false; // unknown stat — spend nothing
        }

        Hero.UnspentStatPoints--;

        // Constitution grants HP directly (scaled by racial effectiveness); heal by the same so
        // current HP rises with the cap. Other stats' effects are live via Effective* values.
        if (stat == "Constitution")
        {
            int hpBump = Math.Max(1, (int)Math.Round(HpPerConstitutionPoint * Hero.ConstitutionEffectiveness));
            Hero.MaxHp += hpBump;
            Hero.CurrentHp = Math.Min(Hero.MaxHp, Hero.CurrentHp + hpBump);
        }

        // Refresh Stamina/Mana/Faith caps + regen from the updated stats.
        UpdateHeroResourcePools();

        LogMessage($"+1 {stat} ({Hero.UnspentStatPoints} point{(Hero.UnspentStatPoints == 1 ? "" : "s")} left)", MessageKind.LevelUp);
        return true;
    }

    /// <summary>Allocate shared XP to one active class slot. Profession slots reject this path.</summary>
    public ProgressionAdvanceResult AllocateClassXp(string slotId, int amount)
    {
        int constitutionBefore = Hero.Constitution;
        ProgressionAdvanceResult result = ProgressionService.Instance.AllocateSharedXp(Hero, slotId, amount);
        if (!result.Success) return result;

        ApplyAttributeRewardSideEffects(constitutionBefore);
        RefreshAttacks();
        if (result.LevelsGained > 0)
        {
            string automatic = result.AutomaticAttributesGranted.Count == 0
                ? "no automatic attributes"
                : string.Join(", ", result.AutomaticAttributesGranted.Select(pair => $"+{pair.Value} {pair.Key}"));
            LogMessage($"Class advanced {result.LevelsGained} level(s): {automatic}; " +
                $"+{result.FreeAttributePointsGranted} free points.", MessageKind.LevelUp);
        }
        return result;
    }

    /// <summary>Foundation for the contextual offer engine: place an accepted profession in an empty slot.</summary>
    public bool TryActivateProfession(string professionId, out string error) =>
        ProgressionService.Instance.TryPlacePath(Hero, ProgressionDomain.Profession, professionId, out error);

    public IReadOnlyList<ProgressionOffer> GenerateProgressionOffers(ProgressionDomain domain, string slotId)
    {
        return ProgressionOfferService.Instance.GenerateOffers(
            Hero, domain, slotId, BuildProgressionContext());
    }

    public IReadOnlyList<ProgressionSpecializationOffer> GenerateSpecializationOffers(string slotId) =>
        ProgressionOfferService.Instance.GenerateSpecializationOffers(
            Hero, slotId, BuildProgressionContext());

    public IReadOnlyList<ProgressionAdvancementOffer> GenerateAdvancementOffers(string slotId) =>
        ProgressionOfferService.Instance.GenerateAdvancementOffers(
            Hero, slotId, BuildProgressionContext());

    public bool AcceptProgressionOffer(string slotId, ProgressionOffer offer, out string error)
    {
        IReadOnlyList<ProgressionOffer> current = GenerateProgressionOffers(offer.Domain, slotId);
        ProgressionOffer? valid = current.FirstOrDefault(candidate =>
            string.Equals(candidate.ResultDefinitionId, offer.ResultDefinitionId,
                StringComparison.OrdinalIgnoreCase) && candidate.ContextHash == offer.ContextHash);
        if (valid == null)
        {
            error = "The character context changed. Review the updated offers.";
            return false;
        }

        bool hadActiveClass = Hero.Progression.ClassSlots.Any(slot => slot.Instance != null);
        if (!ProgressionService.Instance.TryPlacePath(
                Hero, offer.Domain, slotId, offer.ResultDefinitionId, out error)) return false;

        if (offer.Domain == ProgressionDomain.Class && !hadActiveClass)
        {
            _characterDataService.ApplyClassIdentity(Hero, valid.Name);
            _className = valid.Name;
        }
        else if (offer.Domain == ProgressionDomain.Class)
            _characterDataService.ApplyClassAffinities(Hero, valid.Name);
        RefreshAttacks();
        LogMessage($"Accepted {valid.Name} as a level-1 {offer.Domain.ToString().ToLowerInvariant()} path.",
            MessageKind.LevelUp);
        error = "";
        return true;
    }

    public bool AcceptSpecializationOffer(string slotId, ProgressionSpecializationOffer offer,
        out string error)
    {
        ProgressionSpecializationOffer? valid = GenerateSpecializationOffers(slotId)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.SpecializationId, offer.SpecializationId,
                    StringComparison.OrdinalIgnoreCase) && candidate.ContextHash == offer.ContextHash);
        if (valid == null)
        {
            error = "The character context changed. Review the updated specializations.";
            return false;
        }
        if (!ProgressionService.Instance.TrySpecialize(
                Hero, slotId, valid.SpecializationId, out error)) return false;
        RefreshAttacks();
        LogMessage($"Specialized as {valid.Name} without resetting the path's level or XP.",
            MessageKind.LevelUp);
        error = "";
        return true;
    }

    public bool AcceptAdvancementOffer(string slotId, ProgressionAdvancementOffer offer,
        out string error)
    {
        ProgressionAdvancementOffer? valid = GenerateAdvancementOffers(slotId)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.RecipeId, offer.RecipeId, StringComparison.OrdinalIgnoreCase) &&
                candidate.ContextHash == offer.ContextHash);
        if (valid == null)
        {
            error = "The character context changed. Review the updated advancements.";
            return false;
        }
        if (!ProgressionService.Instance.TryAdvance(Hero, valid.RecipeId, valid.TargetSlotId,
                valid.SourceSlotIds, out error)) return false;

        if (valid.Domain == ProgressionDomain.Class)
        {
            ProgressionInstance? primary = Hero.Progression.ClassSlots
                .FirstOrDefault(candidate => !candidate.IsLocked && candidate.Instance != null)?.Instance;
            if (primary != null && string.Equals(primary.Name, valid.Name, StringComparison.OrdinalIgnoreCase))
                _characterDataService.ApplyClassIdentity(Hero, primary.Name);
            else
                _characterDataService.ApplyClassAffinities(Hero, valid.Name);
            if (primary != null) _className = primary.Name;
        }
        RefreshAttacks();
        LogMessage($"Advanced to {valid.Name} at level 1; source paths were preserved in lineage.",
            MessageKind.LevelUp);
        error = "";
        return true;
    }

    public void RecordProgressionFact(string factId, decimal amount = 1m)
    {
        if (string.IsNullOrWhiteSpace(factId) || amount == 0m) return;
        Hero.Progression.Facts[factId] = Hero.Progression.Facts.GetValueOrDefault(factId, 0m) + amount;
    }

    private Dictionary<string, decimal> BuildProgressionContext()
    {
        var context = new Dictionary<string, decimal>
        {
            [IsInOverworld ? "environment.overworld" : "environment.dungeon"] = 1m
        };
        if (!IsInOverworld) context["environment.underground"] = 1m;
        return context;
    }

    /// <summary>Apply direct practice XP to an active profession and its associated skills.</summary>
    public ProgressionAdvanceResult RecordProfessionAction(string professionId)
    {
        int constitutionBefore = Hero.Constitution;
        ProgressionAdvanceResult result = ProgressionService.Instance.RecordProfessionAction(Hero, professionId);
        if (result.Success)
        {
            ApplyAttributeRewardSideEffects(constitutionBefore);
            if (result.LevelsGained > 0)
                LogMessage($"{professionId} advanced {result.LevelsGained} level(s).", MessageKind.LevelUp);
        }
        return result;
    }

    internal int SkillAdjustedYield(string skillId, int baseAmount)
    {
        float multiplier = ProgressionService.Instance.SkillYieldMultiplier(Hero, skillId);
        float adjusted = baseAmount * multiplier;
        int amount = Math.Max(baseAmount, (int)MathF.Floor(adjusted));
        if (_random.NextDouble() < adjusted - MathF.Floor(adjusted)) amount++;
        return amount;
    }

    internal bool RollSkillBonusDrop(string skillId) =>
        _random.NextDouble() < ProgressionService.Instance.SkillBonusDropChance(Hero, skillId);

    private void ApplyAttributeRewardSideEffects(int constitutionBefore)
    {
        int constitutionGain = Hero.Constitution - constitutionBefore;
        if (constitutionGain > 0)
        {
            int hpBump = Math.Max(1, (int)Math.Round(
                HpPerConstitutionPoint * Hero.ConstitutionEffectiveness)) * constitutionGain;
            Hero.MaxHp += hpBump;
            Hero.CurrentHp = Math.Min(Hero.MaxHp, Hero.CurrentHp + hpBump);
        }
        UpdateHeroResourcePools();
    }

    // ---- Debug console --------------------------------------------------------------------
    // Registry of spawnable combinables for /additem and /addspell, keyed by stable id. Fresh
    // instance per call so added copies don't alias a template. Sourced from the same catalogs
    // the loot/craft systems use.
    private static readonly Dictionary<string, Func<Combinable>> _debugCombinables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fireball"] = () => CombinableCatalog.Fireball(),
            ["ice-shard"] = () => CombinableCatalog.IceShard(),
            ["bow"] = () => CombinableCatalog.Bow(),
            ["sword"] = () => CombinableCatalog.Sword(),
            ["dagger"] = () => CombinableCatalog.Dagger(),
            ["shield-generator"] = () => CombinableCatalog.ShieldGenerator(),
            ["dense-musculature"] = () => CombinableCatalog.DenseMusculature(),
            ["mana-circuitry"] = () => CombinableCatalog.ManaCircuitry(),
            ["torch"] = () => CombinableCatalog.Torch(),
            ["night-sight"] = () => CombinableCatalog.NightSight(),
            ["iron-sword"] = () => CraftedItemCatalog.Build("iron-sword")!,
        };

    private static string NormalizeToken(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string AvailableCombinableIds() => string.Join(", ", _debugCombinables.Keys);

    /// <summary>Resolve a combinable by id or display name, tolerant of case/spacing/hyphens
    /// (exact-normalized first, then a contains-match fallback). Null if nothing matches.</summary>
    private static Func<Combinable>? ResolveCombinable(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string norm = NormalizeToken(name);
        foreach (var kv in _debugCombinables)
            if (NormalizeToken(kv.Key) == norm || NormalizeToken(kv.Value().Name) == norm) return kv.Value;
        foreach (var kv in _debugCombinables)
            if (NormalizeToken(kv.Key).Contains(norm) || NormalizeToken(kv.Value().Name).Contains(norm)) return kv.Value;
        return null;
    }

    /// <summary>Parse and run a debug console command (e.g. "/addxp 1000", "additem sword 2",
    /// "moveplayer dungeon 4", "reset health"). A leading '/' is optional. Returns a short result
    /// string for display; also mirrored to the message log. Intended for the in-game debug
    /// console (backtick key) — a cheat/testing affordance, not a normal gameplay path.</summary>
    public string ExecuteDebugCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string text = raw.Trim();
        if (text.StartsWith("/")) text = text[1..];
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";

        string cmd = parts[0].ToLowerInvariant();
        string Arg(int i) => i < parts.Length ? parts[i] : "";
        int IntArg(int i, int def) => int.TryParse(Arg(i), out var v) ? v : def;

        string result;
        switch (cmd)
        {
            case "help": case "?":
                result = "addxp N | addlevel N | addpoints N | addgold N | addprofession <id> | additem <id> [n] | " +
                         "addspell <id> [n] | moveplayer dungeon N|overworld|safe N | settime <hour> | " +
                         "towngen <w> <h> | reveal | mine | gotomine | gotobuilding [n] | " +
                         "reset health|mana|stamina|faith|all | listitems";
                break;

            case "addxp": case "xp":
            {
                int n = IntArg(1, 0);
                int awarded = ProgressionService.Instance.GrantSharedXp(Hero, n);
                result = $"+{awarded} shared XP -> bank {Hero.Progression.UnallocatedXp}";
                break;
            }

            case "addlevel": case "level":
            {
                int n = Math.Max(0, IntArg(1, 1));
                ProgressionSlot? slot = Hero.Progression.ClassSlots
                    .FirstOrDefault(candidate => !candidate.IsLocked && candidate.Instance != null);
                int advanced = 0;
                for (int i = 0; i < n; i++)
                {
                    if (slot?.Instance == null) break;
                    int needed = ProgressionService.Instance.XpNeededForNext(slot.Instance);
                    if (needed <= 0) break;
                    Hero.Progression.UnallocatedXp += needed;
                    advanced += AllocateClassXp(slot.SlotId, needed).LevelsGained;
                }
                result = $"+{advanced} class level(s) -> overall level {Hero.Level}, " +
                    $"{Hero.UnspentStatPoints} free stat point(s)";
                break;
            }

            case "addprofession": case "profession":
            {
                string id = Arg(1);
                bool activated = TryActivateProfession(id, out string error);
                result = activated ? $"Activated profession: {id}" : error;
                break;
            }

            case "addpoints": case "points":
            {
                int n = Math.Max(0, IntArg(1, 1));
                Hero.UnspentStatPoints += n;
                result = $"+{n} stat point(s) -> {Hero.UnspentStatPoints} unspent";
                break;
            }

            case "addgold": case "gold":
            {
                int n = IntArg(1, 0);
                Hero.Gold += n;
                result = $"+{n} gold -> {Hero.Gold}";
                break;
            }

            case "settime": case "time":
            {
                // settime <hour 0-23> — jump the world clock within the current day (night testing).
                int hour = Math.Clamp(IntArg(1, 12), 0, 23);
                Clock.TotalGameMinutes = (Clock.Day - 1) * 1440 + hour * 60;
                result = $"World clock -> {Clock.TimeDisplay} (darkness {Clock.Darkness:0.00})";
                break;
            }

            case "mine":
            {
                // mine — dig out the adjacent rock, wherever you're standing. The player-facing
                // action is Press-E on a rock face; this is the same call for testing.
                var rock = MinableRockNearby();
                if (rock == null)
                {
                    result = "No rock within reach";
                    break;
                }
                var kind = CurrentMaze.Tiles[rock.Value.x, rock.Value.y];
                result = StartMining(rock.Value.x, rock.Value.y)
                    ? $"Working the {kind} at ({rock.Value.x},{rock.Value.y})"
                    : "Can't mine right now";
                break;
            }

            case "gotomine":
            {
                // gotomine — stand at the mine's working face, for testing the dig loop.
                var adit = CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.MineEntrance);
                if (adit == null)
                {
                    result = "This map has no mine";
                    break;
                }
                Hero.X = adit.X;
                Hero.Y = adit.Y;
                result = $"Moved to the mine adit at ({adit.X},{adit.Y})";
                break;
            }

            case "gotobuilding":
            {
                // gotobuilding [n] — stand at the nth building's door. For inspecting generated
                // premises without walking a few hundred tiles to find one.
                var buildings = CurrentMaze.Region?.Buildings;
                if (buildings == null || buildings.Count == 0)
                {
                    result = "This map has no generated buildings";
                    break;
                }
                var target = buildings[Math.Clamp(IntArg(1, 0), 0, buildings.Count - 1)];
                // Stand inside, on the interior anchor — a walkable tile by construction, unlike
                // the door itself (shut) or a guessed offset from it (could be the wall).
                Hero.X = target.InteriorX;
                Hero.Y = target.InteriorY;
                result = $"Moved inside the {target.Kind} at ({target.InteriorX},{target.InteriorY}); " +
                         $"door at ({target.DoorX},{target.DoorY})";
                break;
            }

            case "reveal":
            {
                // Mark the whole floor explored — for inspecting generated layouts (rooms, doors,
                // corridors) without walking them first.
                int revealed = 0;
                for (int x = 0; x < CurrentMaze.Width; x++)
                    for (int y = 0; y < CurrentMaze.Height; y++)
                        if (!CurrentMaze.Walls[x, y] && !CurrentMaze.Explored[x, y])
                        {
                            CurrentMaze.Explored[x, y] = true;
                            revealed++;
                        }
                result = $"Revealed {revealed} tile(s) on floor {CurrentFloor}";
                break;
            }

            case "towngen":
            {
                // towngen <width> <height> — rebuild the town at an arbitrary size. Tiles are
                // person-scaled, so this is how a region's real footprint (hundreds of tiles per
                // axis) gets walked and measured before the region generator exists.
                _townWidth = Math.Clamp(IntArg(1, OverworldGenerator.Width), 9, 4096);
                _townHeight = Math.Clamp(IntArg(2, OverworldGenerator.Height), 9, 4096);
                _townSizeOverride = true; // an explicit size means the plain field, not a region
                if (IsInOverworld)
                {
                    EnterTown();
                    result = $"Town rebuilt at {_townWidth}x{_townHeight} ({_townWidth * _townHeight:N0} tiles)";
                }
                else
                    result = $"Town size set to {_townWidth}x{_townHeight}; takes effect on arrival";
                break;
            }

            case "additem": case "addspell":
            {
                string name = Arg(1);
                int count = Math.Max(1, IntArg(2, 1));
                var factory = ResolveCombinable(name);
                if (factory == null)
                {
                    result = $"Unknown '{name}'. Available: {AvailableCombinableIds()}";
                    break;
                }
                for (int i = 0; i < count; i++) Hero.Inventory.Add(factory());
                var sample = factory();
                result = $"Added {count}x {sample.Name} ({sample.Kind}) to inventory";
                break;
            }

            case "moveplayer": case "move": case "tp":
            {
                string dest = Arg(1).ToLowerInvariant();
                switch (dest)
                {
                    case "dungeon":
                        IsInOverworld = false;
                        CurrentFloor = Math.Max(1, IntArg(2, 1)) - 1; // StartNewFloor increments
                        StartNewFloor();
                        result = $"Moved to dungeon floor {CurrentFloor}";
                        break;
                    case "overworld": case "town":
                        EnterOverworld();
                        result = "Moved to the Overworld";
                        break;
                    case "safe": case "saferoom":
                        IsInOverworld = false;
                        CurrentFloor = Math.Max(1, IntArg(2, CurrentFloor > 0 ? CurrentFloor : 1));
                        EnterSafeRoom();
                        result = $"Moved to safe room after floor {CurrentFloor}";
                        break;
                    default:
                        result = "Usage: moveplayer dungeon N | overworld | safe N";
                        break;
                }
                break;
            }

            case "reset": case "restore":
            {
                string what = Arg(1).ToLowerInvariant();
                switch (what)
                {
                    case "health": case "hp": Hero.CurrentHp = Hero.MaxHp; result = "Health restored"; break;
                    case "mana": case "mp": Hero.CurrentMana = Hero.MaxMana; result = "Mana restored"; break;
                    case "stamina": case "sp": Hero.CurrentStamina = Hero.MaxStamina; result = "Stamina restored"; break;
                    case "faith": Hero.CurrentFaith = Hero.MaxFaith; result = "Faith restored"; break;
                    case "": case "all":
                        Hero.CurrentHp = Hero.MaxHp; Hero.CurrentMana = Hero.MaxMana;
                        Hero.CurrentStamina = Hero.MaxStamina; Hero.CurrentFaith = Hero.MaxFaith;
                        result = "All resources restored";
                        break;
                    default: result = "Usage: reset health|mana|stamina|faith|all"; break;
                }
                break;
            }

            case "listitems": case "items":
                result = AvailableCombinableIds();
                break;

            default:
                result = $"Unknown command '{cmd}'. Type 'help'.";
                break;
        }

        RefreshAttacks(); // in case a move/regen changed floor state or gear
        LogMessage(result, MessageKind.System);
        return result;
    }

    /// <summary>
    /// Update hero's resource pools based on attributes
    /// Constitution → MaxStamina, Intelligence → MaxMana, Wisdom → MaxFaith
    /// </summary>
    private void UpdateHeroResourcePools()
    {
        // Base resource pools + attribute scaling
        Hero.MaxStamina = 100 + (int)(Hero.EffectiveConstitution * 10);
        Hero.MaxMana = 100 + (int)(Hero.EffectiveIntelligence * 10);
        Hero.MaxFaith = 100 + (int)(Hero.EffectiveWisdom * 10);

        // Regen rates scale with attributes (similar to health regen rate)
        // At 10 ticks/sec, these values give: Stat / 4 = resource per second
        // Example: 8 Constitution = 2 Stamina/sec, 12 Intelligence = 3 Mana/sec
        Hero.StaminaRegen = 0; // Will use fractional accumulation
        Hero.ManaRegen = 0;     // Will use fractional accumulation
        Hero.FaithRegen = 0;    // Will use fractional accumulation

        // Health regen: 8 Constitution = 1 HP per second (at 10 ticks/sec = 0.1 HP/tick)
        // We'll use HealthRegen as HP per 10 ticks for display purposes
        Hero.HealthRegen = (int)(Hero.EffectiveConstitution / 8);

        // Testing races (e.g. Debug) can flat-override pools — applied last so they win over
        // the derived values above regardless of stats.
        if (_characterDataService.Races.TryGetValue(_raceName, out var race))
        {
            if (race.HealthOverride.HasValue)
            {
                Hero.MaxHp = race.HealthOverride.Value;
                Hero.CurrentHp = race.HealthOverride.Value;
            }
            if (race.StaminaOverride.HasValue) Hero.MaxStamina = race.StaminaOverride.Value;
            if (race.ManaOverride.HasValue) Hero.MaxMana = race.ManaOverride.Value;
        }
    }
    
    /// <summary>
    /// Returns all grid cells along the line of sight between two points
    /// </summary>
    public List<(int x, int y)> GetSightLine(float x1, float y1, float x2, float y2)
    {
        var cells = new List<(int x, int y)>();
        // Round maps a position to its containing cell (integer coords = cell centers).
        int startX = (int)MathF.Round(x1);
        int startY = (int)MathF.Round(y1);
        int endX = (int)MathF.Round(x2);
        int endY = (int)MathF.Round(y2);
        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        int err = dx - dy;
        int currentX = startX;
        int currentY = startY;
        while (true)
        {
            cells.Add((currentX, currentY));
            if (currentX == endX && currentY == endY)
                break;
            int err2 = 2 * err;
            if (err2 > -dy)
            {
                err -= dy;
                currentX += sx;
            }
            if (err2 < dx)
            {
                err += dx;
                currentY += sy;
            }
        }
        return cells;
    }
    
    /// <summary>
    /// Returns all grid cells within a cone (directional sightline) from a position
    /// </summary>
    public List<(int x, int y)> GetDirectionalSightCone(float originX, float originY, float facingAngleRad, float range, float coneAngleRad)
    {
        var visibleCells = new List<(int x, int y)>();
        // Integer coords are cell centers. Measure the cone from the entity's actual
        // position to each candidate cell's center (its integer coordinate).
        int startX = (int)MathF.Round(originX);
        int startY = (int)MathF.Round(originY);
        int minX = Math.Max(0, startX - (int)range);
        int maxX = Math.Min(CurrentMaze.Width - 1, startX + (int)range);
        int minY = Math.Max(0, startY - (int)range);
        int maxY = Math.Min(CurrentMaze.Height - 1, startY + (int)range);
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                float dx = x - originX;
                float dy = y - originY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > range) continue;
                float cellAngle = MathF.Atan2(dy, dx);
                float angleDiff = MathF.Abs(NormalizeAngleRad(cellAngle - facingAngleRad));
                if (angleDiff <= coneAngleRad / 2)
                {
                    // Check line of sight to the cell center
                    if (HasLineOfSight(originX, originY, x, y))
                        visibleCells.Add((x, y));
                }
            }
        }
        return visibleCells;
    }

    /// <summary>
    /// Normalize angle to [-PI, PI]
    /// </summary>
    private float NormalizeAngleRad(float angle)
    {
        while (angle < -MathF.PI) angle += 2 * MathF.PI;
        while (angle > MathF.PI) angle -= 2 * MathF.PI;
        return angle;
    }
    
    /// <summary>
    /// Restart the game with the same character but a new map seed
    /// </summary>
    public void RestartGame()
    {
        string restartClass = Hero.Class;
        // Generate new seed for different maze
        Seed = new Random().Next();
        
        // Reset death state
        IsHeroDead = false;
        DeathTimer = 0;
        Messages.Clear(); // a fresh run starts a fresh feed (StartNewFloor logs the opener)
        IsInOverworld = false;
        NearbyInteractable = null;
        CurrentActivity = null;
        ScreenShake = 0f;
        SimulationMode = SimulationMode.RealTime;
        ResetTacticalResolution();
        TacticalTurn.IsPlayerTurn = false;
        TacticalTurn.MovementRemaining = 0;
        TacticalTurn.ActionAvailable = false;
        TacticalTurn.BonusActionAvailable = false;
        _manualMoveX = 0f;
        _manualMoveY = 0f;
        _dashTicksRemaining = 0;
        _dashCooldownRemaining = 0;
        _lastHeroDamageSource = ""; // a new hero doesn't inherit the last one's cause of death

        // Reset game state (StartNewFloor increments CurrentFloor to 1 for the first floor)
        TickCount = 0;
        CurrentFloor = 0;
        StairsLocation = null;
        IsInSafeRoom = false;

        // The replacement character's own record starts now: no inherited playtime (the slot is
        // reused but the run is fresh), and days-lived counts from today — the world clock keeps
        // running because the world outlives its characters (owner ruling 2026-08-05).
        _priorPlaytimeSeconds = 0;
        _characterStartDay = Clock.Day;

        // Recreate systems with the new seed
        _random = new Random(Seed);
        _mazeGenerator = new MazeGenerator(Seed);
        _movementSystem = new MovementSystem(Seed);
        _combatSystem = new CombatSystem(Seed);

        // Recreate hero with same class/race
        Hero = new Hero 
        { 
            Name = _characterName,
            MaxHp = 100, 
            CurrentHp = 100, 
            Attack = 5, 
            Defense = 5,
            X = 1,
            Y = 1
        };
        
        if (_creationSelection != null)
        {
            _characterDataService.ApplyClasslessAndRace(Hero, _raceName);
            Hero.Progression = string.Equals(restartClass, "Classless", StringComparison.OrdinalIgnoreCase)
                ? ProgressionService.Instance.CreateEmptyState()
                : ProgressionService.Instance.CreateCompatibilityState(restartClass);
            if (!string.Equals(restartClass, "Classless", StringComparison.OrdinalIgnoreCase))
                _characterDataService.ApplyClassIdentity(Hero, restartClass);
            _className = restartClass;
            StarterLoadoutService.Instance.ApplyToHero(Hero, _creationSelection.ItemIds);
        }
        else
        {
            _characterDataService.ApplyClassAndRace(Hero, _className, _raceName);
            Hero.Progression = ProgressionService.Instance.CreateCompatibilityState(_className);
            Hero.Loadout = new List<Combinable>();
            Hero.Equipment = AttackFactory.GetStartingEquipment(_className);
        }
        // Same starting torch the constructor grants — without it a restarted non-darkvision
        // character is stuck at bare-eyes light radius with no way to get the guaranteed torch.
        if (!Hero.Inventory.Concat(Hero.Loadout).Any(c => c.Id == "torch"))
            Hero.Inventory.Add(CombinableCatalog.Torch());
        ProgressionService.Instance.Normalize(Hero);
        
        // Update resource pools based on attributes
        UpdateHeroResourcePools();
        
        // Initialize resources to max
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;

        RefreshAttacks();

        // Start new floor
        StartNewFloor();

        // A town-born character's replacement is also town-born (same creation choices).
        if (_creationSelection?.StartLocation == StartLocation.Town) StartInTown();

        IsRunning = true;
    }
    
    public void StartNewFloor()
    {
        // Record the floor being left behind (skip on the very first call, where CurrentFloor
        // is still 0 and there's nothing to "clear" yet).
        if (CurrentFloor > 0)
        {
            CodexService.Instance.RecordFloorCleared(CurrentFloor);
        }

        CurrentFloor++;
        CurrentMaze = _mazeGenerator.Generate(41, 31, CurrentFloor);
        LogMessage(CurrentFloor == 1 ? "The dungeon awakens around you." : $"Descended to floor {CurrentFloor}.",
            MessageKind.System);
        
        // Heal hero 25% when advancing to new floor
        int healAmount = Hero.MaxHp / 4;
        Hero.CurrentHp = Math.Min(Hero.MaxHp, Hero.CurrentHp + healAmount);
        
        // Place hero at start
        Hero.X = 1;
        Hero.Y = 1;
        CurrentMaze.Explored[1, 1] = true;
        
        // Get all walkable cells for enemy spawning
        var emptyCells = CurrentMaze.GetEmptyCells();
        
        // Remove cells too close to the hero start position
        emptyCells.RemoveAll(cell => 
            Math.Abs(cell.x - 1) < 5 && Math.Abs(cell.y - 1) < 5);

        // Dressing and theme landmarks are non-blocking, but actors and interactive features
        // should not initially occupy the same tile as either one.
        var reservedCells = CurrentMaze.Dungeon?.Decorations
            .Select(decoration => (decoration.X, decoration.Y))
            .Concat(CurrentMaze.Dungeon.ThemeFeatures.Select(feature => (feature.X, feature.Y)))
            .ToHashSet() ?? new HashSet<(int, int)>();
        emptyCells.RemoveAll(cell => reservedCells.Contains(cell));
        
        // Spawn enemies in random valid locations
        Enemies.Clear();
        enemyPursuitTicks.Clear();       // drop pursuit state tied to the old floor's enemies
        _enemyLastKnownHeroPos.Clear();
        Projectiles.Clear();   // don't let a lingering projectile carry into the new floor
        HitEffects.Clear();
        Boss = null;
        IsInSafeRoom = false;
        StairsLocation = null;
        // Scaled by the active world's hostility profile (Peaceful ×0.7 / Hostile ×1.4); never
        // empties a floor, so even a Peaceful world's shallowest floor still has something in it.
        int enemyCount = Math.Max(1, (int)MathF.Round((3 + CurrentFloor) * WorldService.Profile.EnemyDensity));

        // Place stairs in the generated exit room, then favor its most distant cells. The BFS
        // fallback keeps this compatible with any older/non-semantic maze implementation.
        var distances = CurrentMaze.BfsDistancesFrom(1, 1);
        if (emptyCells.Count > 0)
        {
            var exitRoom = CurrentMaze.Dungeon?.Rooms.FirstOrDefault(room => room.Role == DungeonRoomRole.Exit);
            var exitRoomCells = exitRoom == null
                ? new List<(int x, int y)>()
                : emptyCells.Where(cell => exitRoom.Contains(cell.x, cell.y)).ToList();
            var reachable = (exitRoomCells.Count > 0 ? exitRoomCells : emptyCells)
                .Where(c => distances.ContainsKey(c)).ToList();
            var candidates = reachable.Count > 0 ? reachable : emptyCells;
            int farThreshold = candidates.Count > 0
                ? candidates.Select(c => distances.GetValueOrDefault(c, 0)).OrderByDescending(d => d)
                    .ElementAt(Math.Max(0, candidates.Count / 4 - 1)) // top 25% farthest
                : 0;
            var farCandidates = candidates.Where(c => distances.GetValueOrDefault(c, 0) >= farThreshold).ToList();
            var stairsPool = farCandidates.Count > 0 ? farCandidates : candidates;
            var stairsCell = stairsPool[_random.Next(stairsPool.Count)];
            emptyCells.Remove(stairsCell);
            CurrentMaze.Features.Add(new MazeFeature { X = stairsCell.x, Y = stairsCell.y, Type = MazeFeatureType.Stairs });
        }
        // Place chest in the room reserved for treasure (loot + XP only — no key/gating).
        MazeFeature? floorChest = null;
        if (emptyCells.Count > 0)
        {
            var chestCell = TakeRoomAwareCell(emptyCells, DungeonRoomRole.Treasure);
            bool locked = _random.NextDouble() < Math.Min(0.65, 0.3 + CurrentFloor * 0.025);
            floorChest = new MazeFeature
            {
                X = chestCell.x,
                Y = chestCell.y,
                Type = MazeFeatureType.Chest,
                IsLocked = locked,
                IsTrapped = _random.NextDouble() < 0.3,
                RequiredKeyId = locked ? $"chest-key-{CurrentFloor}-{chestCell.x}-{chestCell.y}" : "",
                Gold = _random.Next(3, 8 + CurrentFloor * 2)
            };
            floorChest.Inventory.Add(LootService.Roll(CurrentFloor, _random));
            if (_random.NextDouble() < 0.25)
                floorChest.Inventory.Add(LootService.Roll(CurrentFloor, _random));
            CurrentMaze.Features.Add(floorChest);
        }
        // Occasionally place a trap (environmental hazard, not every floor). The base 0.4 chance is
        // shifted by the active world's hostility profile (Peaceful -0.1 / Hostile +0.1).
        float trapChance = Math.Clamp(0.4f + WorldService.Profile.TrapChanceDelta, 0f, 1f);
        if (emptyCells.Count > 0 && _random.NextDouble() < trapChance)
        {
            var trapCell = TakeRoomAwareCell(emptyCells, DungeonRoomRole.Hazard);
            // Hidden: nearly invisible until the hero notices it (a Wisdom spot roll) or examines it.
            CurrentMaze.Features.Add(new MazeFeature { X = trapCell.x, Y = trapCell.y, Type = MazeFeatureType.Trap, Hidden = true });
        }
        // No per-floor boss anymore — the significant fight is the Guardian at each safe-room gate.
        PopulateDungeonEncounters(emptyCells, enemyCount);
        if (floorChest is { IsLocked: true } && Enemies.Count > 0)
        {
            Enemy keyBearer = Enemies[_random.Next(Enemies.Count)];
            keyBearer.Inventory.Add(CombinableCatalog.ChestKey(floorChest.RequiredKeyId));
        }
    }

    private static readonly HashSet<string> LegacyClassActionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "quick-slash", "heavy-cleave", "magic-dart", "arcane-blast", "quick-stab",
        "devastating-backstab", "holy-touch", "divine-wrath", "quick-shot", "power-shot",
        "sound-wave", "sonic-boom", "light-punch", "heavy-strike"
    };

    /// <summary>Version-one saves stored class actions and found weapons in the same Loadout.</summary>
    private void MigrateLegacyLoadout(SaveData data)
    {
        foreach (Weapon weapon in data.Loadout.OfType<Weapon>())
        {
            if (LegacyClassActionIds.Contains(weapon.Id)) continue;
            Hero.Inventory.Add(weapon);
            EquipFromInventory(weapon, out _);
        }

        if (Hero.Equipment.Count == 0)
            Hero.Equipment = AttackFactory.GetStartingEquipment(Hero.Class);
    }

    private (int x, int y) TakeRoomAwareCell(List<(int x, int y)> available, DungeonRoomRole role)
    {
        var room = CurrentMaze.Dungeon?.Rooms.FirstOrDefault(candidate => candidate.Role == role);
        return TakeRoomAwareCell(available, room);
    }

    private (int x, int y) TakeRoomAwareCell(List<(int x, int y)> available, DungeonRoom? room)
    {
        var preferred = room == null
            ? available
            : available.Where(cell => room.Contains(cell.x, cell.y)).ToList();
        var pool = preferred.Count > 0 ? preferred : available;
        var selected = pool[_random.Next(pool.Count)];
        available.Remove(selected);
        return selected;
    }

    private void PopulateDungeonEncounters(List<(int x, int y)> available, int enemyCount)
    {
        var layout = CurrentMaze.Dungeon;
        if (layout == null)
        {
            for (int i = 0; i < enemyCount && available.Count > 0; i++)
                SpawnEncounterEnemy(TakeRoomAwareCell(available, null), -1, -1, new List<int>(), null);
            return;
        }

        layout.Encounters.Clear();
        int landmarkRoomId = layout.ThemeFeatures.FirstOrDefault()?.RoomId ?? -1;
        var encounterRooms = layout.Rooms
            .Where(room => EncounterKindFor(room.Archetype).HasValue)
            .OrderBy(room => room.Id == landmarkRoomId ? 0 : 1)
            .ThenBy(_ => _random.Next())
            .ToList();
        int remaining = enemyCount;
        int roomCursor = 0;

        while (remaining > 0 && encounterRooms.Count > 0)
        {
            var room = encounterRooms[roomCursor % encounterRooms.Count];
            roomCursor++;
            var roomCells = available.Where(cell => room.Contains(cell.x, cell.y)).ToList();
            if (roomCells.Count == 0)
            {
                encounterRooms.Remove(room);
                continue;
            }

            var kind = EncounterKindFor(room.Archetype)!.Value;
            int groupSize = Math.Min(remaining, Math.Min(roomCells.Count, GroupSizeFor(kind)));
            string race = EnemyFactory.RandomRaceFrom(
                EncounterRacesFor(layout.Theme), _characterDataService, _random);
            var patrolRoute = BuildPatrolRoute(layout, room);
            var encounter = new DungeonEncounter
            {
                Id = layout.Encounters.Count,
                HomeRoomId = room.Id,
                Kind = kind,
                Race = race,
                MemberCount = groupSize
            };
            encounter.PatrolRoomIds.AddRange(patrolRoute);
            layout.Encounters.Add(encounter);

            for (int member = 0; member < groupSize; member++)
            {
                var spawnCell = roomCells[_random.Next(roomCells.Count)];
                roomCells.Remove(spawnCell);
                available.Remove(spawnCell);
                SpawnEncounterEnemy(spawnCell, encounter.Id, room.Id, patrolRoute, race);
            }

            remaining -= groupSize;
        }
    }

    private void SpawnEncounterEnemy(
        (int x, int y) cell,
        int encounterId,
        int homeRoomId,
        List<int> patrolRoute,
        string? race)
    {
        var enemy = race == null
            ? EnemyFactory.RandomRegular(CurrentFloor, _characterDataService, _random)
            : EnemyFactory.RandomRegularForRace(CurrentFloor, race, _characterDataService, _random);
        enemy.X = cell.x;
        enemy.Y = cell.y;
        enemy.TargetX = cell.x;
        enemy.TargetY = cell.y;
        enemy.EncounterId = encounterId;
        enemy.HomeRoomId = homeRoomId;
        enemy.PatrolRoomIds = new List<int>(patrolRoute);
        enemy.PatrolRouteIndex = patrolRoute.Count > 1 ? 1 : 0;
        Enemies.Add(enemy);
    }

    private List<int> BuildPatrolRoute(DungeonLayout layout, DungeonRoom homeRoom)
    {
        bool canPatrol = homeRoom.Archetype is DungeonRoomArchetype.GuardPost or DungeonRoomArchetype.Lair;
        if (!canPatrol || _random.NextDouble() >= 0.65)
            return new List<int> { homeRoom.Id };

        var neighboringRooms = layout.Connections
            .Where(connection => connection.FromRoomId == homeRoom.Id || connection.ToRoomId == homeRoom.Id)
            .Select(connection => connection.FromRoomId == homeRoom.Id
                ? layout.Rooms[connection.ToRoomId]
                : layout.Rooms[connection.FromRoomId])
            .Where(room => room.Role == DungeonRoomRole.Standard && room.Archetype != DungeonRoomArchetype.StoreRoom)
            .OrderBy(_ => _random.Next())
            .ToList();
        return neighboringRooms.Count > 0
            ? new List<int> { homeRoom.Id, neighboringRooms[0].Id }
            : new List<int> { homeRoom.Id };
    }

    private int GroupSizeFor(DungeonEncounterKind kind) => kind switch
    {
        DungeonEncounterKind.Sentries => _random.Next(1, 3),
        DungeonEncounterKind.Garrison => _random.Next(2, 5),
        DungeonEncounterKind.HuntingPack => _random.Next(2, 4),
        DungeonEncounterKind.Ambush => _random.Next(1, 3),
        DungeonEncounterKind.Gatekeepers => _random.Next(2, 4),
        _ => 1
    };

    private static DungeonEncounterKind? EncounterKindFor(DungeonRoomArchetype archetype) => archetype switch
    {
        DungeonRoomArchetype.GuardPost => DungeonEncounterKind.Sentries,
        DungeonRoomArchetype.Barracks => DungeonEncounterKind.Garrison,
        DungeonRoomArchetype.Lair => DungeonEncounterKind.HuntingPack,
        DungeonRoomArchetype.TrapGallery => DungeonEncounterKind.Ambush,
        DungeonRoomArchetype.ExitChamber => DungeonEncounterKind.Gatekeepers,
        _ => null
    };

    private static string[] EncounterRacesFor(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Castle => new[] { "Human", "Elf", "Dwarf" },
        DungeonTheme.Sewer => new[] { "Kobold", "Goblin", "Orc" },
        DungeonTheme.Cemetery => new[] { "Tiefling", "Orc", "Human" },
        DungeonTheme.Library => new[] { "Elf", "Human", "Tiefling" },
        DungeonTheme.Forge => new[] { "Dwarf", "Dragonborn", "Orc" },
        _ => new[] { "Human", "Halfling", "Goblin", "Orc" }
    };

    /// <summary>
    /// Run a short deterministic simulation for testing (prints events to console)
    /// </summary>
    public void RunSimulationTicks(int ticks)
    {
        // Create a simple floor and a single enemy for deterministic testing
        CurrentMaze = _mazeGenerator.Generate(21, 15, 1);
        Enemies.Clear();
        var enemy = new Enemy { X = Hero.X + 3, Y = Hero.Y, Hp = 20, MaxHp = 20, Attack = 4, Defense = 1, AttackRange = 1.0f };
        Enemies.Add(enemy);
        // Carve a small corridor between hero and enemy so line of sight is clear for the test
        int startGX = (int)MathF.Round(Hero.X);
        int endGX = (int)MathF.Round(enemy.X);
        int gy = (int)MathF.Round(Hero.Y);
        for (int x = Math.Min(startGX, endGX); x <= Math.Max(startGX, endGX); x++)
        {
            if (x >= 0 && x < CurrentMaze.Width && gy >= 0 && gy < CurrentMaze.Height)
                CurrentMaze.Tiles[x, gy] = TileType.Floor;
        }
        Console.WriteLine($"Starting simulation: Hero at ({Hero.X},{Hero.Y}), Enemy at ({enemy.X},{enemy.Y})");

        IsRunning = true;
        for (int i = 0; i < ticks; i++)
        {
            Tick();
            // Log simple status every 10 ticks
            if (i % 10 == 0)
            {
                float distanceToEnemy = MathF.Sqrt((Hero.X - enemy.X) * (Hero.X - enemy.X) + (Hero.Y - enemy.Y) * (Hero.Y - enemy.Y));
                Console.WriteLine($"Tick {i}: HeroHP={Hero.CurrentHp}/{Hero.MaxHp}, EnemyHP={enemy.Hp}/{enemy.MaxHp}, HeroInCombat={Hero.InCombat}, EnemyInCombat={enemy.InCombat}, Distance={distanceToEnemy:0.00}");
                Console.WriteLine($" HeroPos=({Hero.X:0.00},{Hero.Y:0.00}), EnemyPos=({enemy.X:0.00},{enemy.Y:0.00}), HeroCD={Hero.AttackCooldown}, EnemyCD={enemy.AttackCooldown}");
            }
            if (!Hero.IsAlive || !enemy.IsAlive) break;
        }

        Console.WriteLine("Simulation ended.");
        IsRunning = false;
    }
}
}

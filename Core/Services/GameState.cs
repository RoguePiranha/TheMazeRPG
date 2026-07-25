using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Systems;

namespace TheMazeRPG.Core.Services
{

/// <summary>
/// The hero's current scripted objective while in the Overworld. This is an explicit,
/// deliberately-flagged placeholder for real player choice/input (see Implementation Plan
/// section 0a) — a linear scripted sequence, not "AI deciding," proving the first Overworld
/// vertical slice (mine -> smelt/craft -> sell or equip -> return to the dungeon).
/// </summary>
public enum OverworldGoal
{
    ToMine,
    Mining,
    ToSmithy,
    Crafting,
    ToStall,
    Selling,
    ToDungeonEntrance
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

    /// <summary>The hero's current scripted Overworld objective — see OverworldGoal doc comment.</summary>
    public OverworldGoal CurrentOverworldGoal { get; private set; } = OverworldGoal.ToMine;

    /// <summary>Whether saving is allowed right now: in the Overworld, or in a safe room whose
    /// Guardian hasn't been engaged. Never on regular dungeon floors — the dungeon is the Trial;
    /// closing the game mid-dive reverts to the dungeon-entrance auto-save. The pause menu's
    /// Save action keys off this.</summary>
    public bool CanSave => IsInOverworld || (IsInSafeRoom && Boss == null);
    // Set when the Guardian dies mid-enumeration (see HandleEnemyDefeated); applied once Tick()
    // reaches a point where Enemies/Projectiles are safe to clear.
    private bool _pendingGuardianVictory;

    /// <summary>The hero's current multi-tick task (opening a chest, mining, crafting, ...).
    /// Only one at a time — see StartActivity. Advanced once per Tick().</summary>
    public Activity? CurrentActivity { get; private set; }

    public List<Projectile> Projectiles { get; set; } = new();
    public List<HitEffect> HitEffects { get; set; } = new();

    // Track enemy pursuit persistence
    private Dictionary<Enemy, int> enemyPursuitTicks = new();
    private const float AgroRadius = 7.5f; // Extended agro radius for persistence

    // Timing derived from the authoritative tick rate (see GameSettings) so real-time
    // durations stay correct regardless of ticks/sec.
    private readonly int _ticksPerSecond;
    private readonly int _pursuitTimeoutTicks;   // ~3s pursuit persistence
    private readonly int _autoRestartTicks;      // ~5s until auto-restart after death
    private readonly int _chestOpeningTicks;     // ~3s to open a chest
    private readonly int _combatStartWindupTicks; // ~0.3s wind-up before first attack on engage
    private readonly int _attackSwitchTicks;     // ~2s between smart attack-rotation switches

    public int Seed { get; set; }
    public int TickCount { get; private set; }
    public int CurrentFloor { get; private set; } = 0;
    public bool IsRunning { get; set; }

    /// <summary>Identifies this character's save slot (Saves/{SaveId}.json) — generated fresh for
    /// a new character, or restored from the loaded SaveData in LoadFrom so re-saving overwrites
    /// the same file rather than multiplying save slots.</summary>
    public string SaveId { get; private set; } = Guid.NewGuid().ToString();

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

        // Derive all real-time durations from the authoritative tick rate.
        _ticksPerSecond = Math.Max(1, GameSettings.Current.TickRate);
        _pursuitTimeoutTicks = GameSettings.Current.SecondsToTicks(3f);
        _autoRestartTicks = GameSettings.Current.SecondsToTicks(5f);
        _chestOpeningTicks = GameSettings.Current.SecondsToTicks(3f);
        _combatStartWindupTicks = GameSettings.Current.SecondsToTicks(0.3f);
        _attackSwitchTicks = GameSettings.Current.SecondsToTicks(2f);

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

        // Update resource pools based on attributes
        UpdateHeroResourcePools();

        // Initialize resources to max
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;

        Console.WriteLine($"Character Created: {Hero.Name} - {Hero.Race} {Hero.Class}");
        GameLog.Debug($"Colors - Race: {Hero.RaceColor}, Class: {Hero.ClassColor}");
        GameLog.Debug($"Base Str {Hero.Strength} -> Effective {Hero.EffectiveStrength:0.0}; Con {Hero.Constitution} -> {Hero.EffectiveConstitution:0.0} (MaxStamina {Hero.MaxStamina}); Int {Hero.Intelligence} -> {Hero.EffectiveIntelligence:0.0} (MaxMana {Hero.MaxMana})");

        // Equip the class starting loadout and project it into executable attacks
        Hero.Loadout = AttackFactory.GetStartingLoadout(className);
        Hero.Attacks = AttackFactory.ToAttacks(Hero.Loadout);
        Hero.CurrentAttack = Hero.Attacks.Count > 0 ? Hero.Attacks[0] : null;
        GameLog.Debug($"Attacks assigned: {Hero.Attacks.Count}, Current: {Hero.CurrentAttack?.Name ?? "None"}");

        StartNewFloor();

        // Debug flags from environment
        DebugDrawHitboxes = (Environment.GetEnvironmentVariable("DEBUG_HITBOXES") == "1");
        DebugDrawLOS = (Environment.GetEnvironmentVariable("DEBUG_LOS") == "1");
    }
    
    public void Tick()
    {
        if (!IsRunning) return;

        // Decay screen shake regardless of death/combat state so it always settles.
        if (ScreenShake > 0f)
        {
            ScreenShake *= 0.8f;
            if (ScreenShake < 0.1f) ScreenShake = 0f;
        }

        // Check if hero died this tick
        if (!Hero.IsAlive && !IsHeroDead)
        {
            IsHeroDead = true;
            DeathTimer = 0;
            CodexService.Instance.RecordDeath(CurrentFloor);
            // Permadeath: death destroys this hero's save slot — no reloading out of it.
            SaveService.Delete(SaveId);
            // Stop the game but keep ticking for the timer
        }
        
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
        
        // Move hero (unless busy with an activity)
        if (CurrentActivity == null)
        {
            if (!Hero.InCombat)
            {
                if (IsInOverworld)
                {
                    // Placeholder scripted sequence standing in for real player choice/input,
                    // exactly like the safe room's shrine-vs-Guardian default below — see
                    // Implementation Plan section 0a. Walks the hero toward whatever feature the
                    // current OverworldGoal points at; goals with no walk target (the hero is mid-
                    // Activity) leave movement alone.
                    var targetType = OverworldGoalTarget(CurrentOverworldGoal);
                    if (targetType.HasValue)
                    {
                        var target = CurrentMaze.Features.FirstOrDefault(f => f.Type == targetType.Value);
                        if (target != null)
                        {
                            _movementSystem.MoveHeroTowardTarget(Hero, target.X, target.Y, CurrentMaze);
                        }
                    }
                }
                else if (IsInSafeRoom)
                {
                    // Default auto-play behavior in a safe room: push toward the Guardian gate
                    // rather than retreat to the shrine. This is a stand-in for a real player
                    // choice (shrine vs. guardian) until a pause/manual-control system exists —
                    // see Implementation Plan section 0a.
                    var guardianDoor = CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.GuardianDoor);
                    if (guardianDoor != null)
                    {
                        _movementSystem.MoveHeroTowardTarget(Hero, guardianDoor.X, guardianDoor.Y, CurrentMaze);
                    }
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
                    _movementSystem.MoveEnemySmoothRandom(enemy, CurrentMaze);
                }
            }
            else
            {
                // Move toward hero during combat
                _movementSystem.MoveEnemyTowardTarget(enemy, Hero.X, Hero.Y, CurrentMaze);
            }
        }

        // Resolve hero/enemy physical interactions (hitbox collision)
        ResolveHeroEnemyCollisions();
        
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
            StartNewFloor();
        }

        // Advance the hero's current activity (chest-opening, mining, crafting, ...), if any
        UpdateCurrentActivity();

        // Check for features (stairs, chests, shrine, guardian door, traps) - only if not in combat
        if (!Hero.InCombat)
        {
            CheckFeatures();
        }
        
        // Smart attack rotation during combat - prefer heavy attacks when resources are available
        if (Hero.InCombat && Hero.Attacks.Count > 1)
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
                // Reset pursuit timer if either can see the other
                if (HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y))
                    enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
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

                if (hasLOS && (distance <= meleeThreshold || (distance < AgroRadius && isPersistent)))
                {
                    engagedEnemies.Add(enemy);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        primaryTarget = enemy;
                    }
                    // If enemy in sight, reset pursuit
                    if (distance < 5.0f)
                        enemyPursuitTicks[enemy] = _pursuitTimeoutTicks;
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

        // Process hero vs primary target (includes enemy retaliation for that target)
        _combatSystem.ProcessCombat(Hero, targetEnemy, Projectiles, CurrentMaze);

        // Process enemy-only attacks for the rest so hero cooldown isn't decremented multiple times
        foreach (var e in engagedEnemies)
        {
            if (e == targetEnemy) continue;
            _combatSystem.ProcessEnemyOnlyAttack(Hero, e, Projectiles, CurrentMaze);
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
    }

    /// <summary>
    /// Public debug wrapper for LOS checks from renderer/diagnostics.
    /// </summary>
    public bool CheckLOS(float x1, float y1, float x2, float y2) => HasLineOfSight(x1, y1, x2, y2);

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
                    float dx = p.CurrentX - enemy.X;
                    float dy = p.CurrentY - enemy.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist <= (pr + enemy.Radius))
                    {
                        enemy.Hp -= Math.Max(1, p.Damage);
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

                        // End combat if enemy dies
                        if (!enemy.IsAlive)
                        {
                            GameLog.Debug("  ✓ Enemy defeated by hit!");
                            HandleEnemyDefeated(enemy);
                            Hero.InCombat = false;
                            enemy.InCombat = false;
                            Hero.AnimationOffsetX = 0;
                            Hero.AnimationOffsetY = 0;
                            // Do not clear other projectiles; allow other combats to continue
                        }

                        if (!p.CanHitMultiple)
                        {
                            p.ConsumedOnHit = true;
                            p.LifeTime = p.MaxLifeTime; // deactivate
                        }
                        // If multi-hit, keep active for others this tick
                        break;
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
                    Hero.CurrentHp -= Math.Max(1, p.Damage);
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
        int xpGain = (int)((10 + enemy.MaxHp / 4) * enemy.XpMultiplier);
        Hero.GainExperience(xpGain);

        float dropChance = enemy.Tier switch
        {
            EnemyTier.Elite => 0.45f,
            EnemyTier.Boss => 1.0f,
            _ => 0.15f
        };
        if (_random.NextDouble() < dropChance)
        {
            AcquireLoot(LootService.Roll(CurrentFloor, _random));
        }

        CodexService.Instance.RecordKill(enemy, CurrentFloor);

        // Defeating the safe room's Guardian proceeds to the next floor group. This is called
        // from inside a live `foreach (var p in Projectiles)` (ProcessProjectileCollisions), so
        // we can't mutate Enemies/Projectiles here (StartNewFloor clears both) — defer it and
        // apply it once that enumeration has finished (see Tick()).
        if (IsInSafeRoom && enemy == Boss)
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
                // Push hero out; if enemy not in combat, nudge both a bit
                Hero.X += nx * push * 0.8f;
                Hero.Y += ny * push * 0.8f;
                if (!enemy.InCombat)
                {
                    enemy.X -= nx * push * 0.2f;
                    enemy.Y -= ny * push * 0.2f;
                }
            }
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
    /// Give the hero found loot. Attack-producing gear (weapons/spells) auto-equips into a free
    /// hotbar slot and updates the hero's attacks; anything else (or an overflow) goes to inventory.
    /// Auto-equip stands in for the manual "swap / combine / send to inventory" choice while the
    /// game is auto-played.
    /// </summary>
    public void AcquireLoot(Combinable loot)
    {
        bool isAttackGear = loot is Weapon || loot is Spell;
        int equippedAttackGear = Hero.Loadout.Count(c => c is Weapon || c is Spell);

        if (isAttackGear && equippedAttackGear < Hero.HotbarCapacity)
        {
            Hero.Loadout.Add(loot);
            RefreshAttacks();
            GameLog.Debug($"Equipped {loot.Name} ({loot.Rarity})");
        }
        else
        {
            Hero.Inventory.Add(loot);
            GameLog.Debug($"Stored {loot.Name} ({loot.Rarity}) in inventory");
        }
    }

    /// <summary>Re-project attacks from the current loadout, keeping the current attack if it survives.</summary>
    private void RefreshAttacks()
    {
        var currentId = Hero.CurrentAttack?.Id;
        Hero.Attacks = AttackFactory.ToAttacks(Hero.Loadout);
        Hero.CurrentAttack = Hero.Attacks.FirstOrDefault(a => a.Id == currentId) ?? Hero.Attacks.FirstOrDefault();
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

    /// <summary>XP + loot for a fully-opened chest. Kept on GameState (not ChestOpenActivity)
    /// so the shared RNG (_random) stays private to this class.</summary>
    internal void GrantChestRewards()
    {
        Hero.GainExperience(25);
        AcquireLoot(LootService.Roll(CurrentFloor, _random));
    }

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

    /// <summary>Advance the scripted Overworld goal sequence. Called by activities on completion
    /// (e.g. MineOreActivity finishing moves to ToSmithy).</summary>
    internal void AdvanceOverworldGoal(OverworldGoal next) => CurrentOverworldGoal = next;

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

    private const int GoldPerRarityPoint = 10;

    /// <summary>
    /// Sell an owned Combinable for gold at the Stall. Price reuses CombinationEngine's
    /// RarityPoints scale (the same one rarity-averaging already uses) rather than a second,
    /// unrelated formula. Works whether the item is equipped (Loadout) or not (Inventory);
    /// returns 0 (no gold) if the hero doesn't actually own the item.
    /// </summary>
    public int SellItem(Combinable item)
    {
        bool wasEquipped = Hero.Loadout.Remove(item);
        bool owned = Hero.Inventory.Remove(item) || wasEquipped;
        if (!owned)
        {
            GameLog.Debug($"SellItem: hero does not own '{item.Name}' — no sale.");
            return 0;
        }

        if (wasEquipped) RefreshAttacks();
        int price = CombinationEngine.RarityPoints(item.Rarity) * GoldPerRarityPoint;
        Hero.Gold += price;
        return price;
    }

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
        foreach (var feature in CurrentMaze.Features.Where(f => !f.IsUsed && !f.IsOpening).ToList())
        {
            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // Auto-pickup radius of 0.7 tiles
            if (distance < 0.7f && feature.Type == MazeFeatureType.Chest)
            {
                StartActivity(new ChestOpenActivity(feature, _chestOpeningTicks));
                continue; // Skip to next feature
            }

            if (feature.Type == MazeFeatureType.Trap && distance < 0.5f)
            {
                feature.IsUsed = true;
                TriggerTrap(feature);
                continue;
            }

            // Overworld: reaching the dungeon entrance starts a fresh dive (the return trip).
            if (feature.Type == MazeFeatureType.DungeonEntrance && distance < 0.6f && IsInOverworld)
            {
                StartFreshDungeonDive();
                return; // state just reset — stop processing this tick's features
            }

            // Overworld: mining at the MineEntrance.
            if (feature.Type == MazeFeatureType.MineEntrance && distance < 0.6f
                && CurrentOverworldGoal == OverworldGoal.ToMine)
            {
                CurrentOverworldGoal = OverworldGoal.Mining;
                StartActivity(new MineOreActivity("iron-ore", 3, GameSettings.Current.SecondsToTicks(5f),
                    gs => gs.AdvanceOverworldGoal(OverworldGoal.ToSmithy)));
                continue;
            }

            // Overworld: smelt then craft at the Smithy — two chained recipes, proving the
            // recipe system generalizes rather than being a single hardcoded transformation.
            if (feature.Type == MazeFeatureType.Smithy && distance < 0.6f
                && CurrentOverworldGoal == OverworldGoal.ToSmithy)
            {
                CurrentOverworldGoal = OverworldGoal.Crafting;
                var smelt = RecipeDataService.Instance.Get("smelt-iron")!;
                StartActivity(new CraftActivity(smelt, GameSettings.Current.SecondsToTicks(smelt.DurationSeconds), gs =>
                {
                    var craft = RecipeDataService.Instance.Get("craft-iron-sword")!;
                    gs.StartActivity(new CraftActivity(craft, GameSettings.Current.SecondsToTicks(craft.DurationSeconds),
                        gs2 => gs2.AdvanceOverworldGoal(OverworldGoal.ToStall)));
                }));
                continue;
            }

            // Overworld: sell the crafted item at the Stall (instant — unlike mining/crafting,
            // "handing an item to a merchant" doesn't need a multi-tick Activity for v1).
            if (feature.Type == MazeFeatureType.Stall && distance < 0.6f
                && CurrentOverworldGoal == OverworldGoal.ToStall)
            {
                var sword = Hero.Inventory.Concat(Hero.Loadout).OfType<Weapon>()
                    .FirstOrDefault(w => w.Id == "iron-sword");
                if (sword != null)
                {
                    SellItem(sword);
                }
                CurrentOverworldGoal = OverworldGoal.ToDungeonEntrance;
                continue;
            }

            if (feature.Type == MazeFeatureType.Shrine)
            {
                // Only meaningful in a safe room; touching it exits the dungeon.
                if (distance < 0.6f && IsInSafeRoom)
                {
                    feature.IsUsed = true;
                    EnterOverworld();
                    return; // state just reset — stop processing this tick's features
                }
                continue;
            }

            if (feature.Type == MazeFeatureType.GuardianDoor)
            {
                if (distance < 0.9f)
                {
                    feature.IsUsed = true;
                    SpawnGuardian(feature.X, feature.Y);
                }
                continue;
            }

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
    }

    /// <summary>Spawns the gate Guardian once the hero approaches the safe room's door.
    /// Reuses the same boss-tier generation regular floors used to use — a Guardian is simply
    /// the dungeon's boss concept, now confined to its own room instead of wandering a maze.
    /// The fight itself IS the Guardian floor (5, 10, 15, ...): entering it advances
    /// CurrentFloor from e.g. 4 (safe room "4.5") to 5, so the Guardian's level scales off the
    /// Guardian floor and victory proceeds to floor 6.</summary>
    private void SpawnGuardian(float x, float y)
    {
        CurrentFloor++;
        Boss = EnemyFactory.RandomBoss(CurrentFloor, _characterDataService, _random);
        Boss.X = x;
        Boss.Y = y;
        Boss.TargetX = x;
        Boss.TargetY = y;
        Enemies.Add(Boss);
        GameLog.Debug($"Guardian spawned: {Boss.Race} {Boss.Class} (Level {Boss.Level})");
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
                maze.Walls[x, y] = isBorder;
                if (!isBorder) maze.Explored[x, y] = true; // fully lit; nothing to explore
            }
        }

        maze.Features.Add(new MazeFeature { X = width / 2, Y = midY, Type = MazeFeatureType.Shrine });
        maze.Features.Add(new MazeFeature { X = width - 2, Y = midY, Type = MazeFeatureType.GuardianDoor });
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

        IsInSafeRoom = false;
        Boss = null;
        Enemies.Clear();
        Projectiles.Clear();
        HitEffects.Clear();
        StairsLocation = null;

        IsInOverworld = true;
        CurrentOverworldGoal = OverworldGoal.ToMine;
        CurrentMaze = OverworldGenerator.Generate();
        var entrance = CurrentMaze.Features.First(f => f.Type == MazeFeatureType.DungeonEntrance);
        // Placed one tile away, not exactly on the entrance — it's also the return-trip trigger
        // (see CheckFeatures), so arriving directly on top of it would immediately re-trigger a
        // new dive before the Overworld goal logic gets a turn.
        Hero.X = entrance.X + 1;
        Hero.Y = entrance.Y;

        // Auto-save checkpoint: the hero just survived leaving the dungeon, bank that progress
        // to disk. There's no player-driven "Save" action yet (no input system at all), so this
        // is the natural automatic checkpoint until one exists.
        SaveService.Save(this);
    }

    /// <summary>
    /// Restore a hero's progress from a save. Call after constructing a GameState normally (with
    /// the saved class/race, so class-derived setup like effectiveness/starting loadout is
    /// computed correctly), then this overwrites the fresh stats/inventory with the saved ones
    /// and resumes where the save was made: a safe-room checkpoint (SaveData.SafeRoomFloor), or
    /// otherwise the Overworld's dungeon entrance. Regular dungeon floors are never saved, so a
    /// mid-dive quit resumes from the entry-time snapshot at the entrance.
    /// </summary>
    public void LoadFrom(SaveData data)
    {
        SaveId = data.SaveId;
        _priorPlaytimeSeconds = data.PlaytimeSeconds;

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
        Hero.Gold = data.Gold;
        Hero.Resources = new Dictionary<string, int>(data.Resources);
        Hero.Loadout = new List<Combinable>(data.Loadout);
        Hero.Inventory = new List<Combinable>(data.Inventory);
        RefreshAttacks();

        if (data.SafeRoomFloor.HasValue)
        {
            // Resume at the safe-room checkpoint (e.g. floor "4.5"). EnterSafeRoom rebuilds the
            // room, places the hero, and re-checkpoints (a harmless identical re-save).
            CurrentFloor = data.SafeRoomFloor.Value;
            IsInOverworld = false;
            EnterSafeRoom();
        }
        else
        {
            IsInOverworld = true;
            CurrentOverworldGoal = OverworldGoal.ToMine;
            CurrentMaze = OverworldGenerator.Generate();
            var entrance = CurrentMaze.Features.First(f => f.Type == MazeFeatureType.DungeonEntrance);
            Hero.X = entrance.X + 1;
            Hero.Y = entrance.Y;
        }
    }

    /// <summary>The map feature (if any) the hero should walk toward for a given Overworld goal.
    /// Goals with no target (Mining/Crafting/Selling) mean the hero is mid-Activity and should
    /// stay put — movement is left alone in that case.</summary>
    private static MazeFeatureType? OverworldGoalTarget(OverworldGoal goal) => goal switch
    {
        OverworldGoal.ToMine => MazeFeatureType.MineEntrance,
        OverworldGoal.ToSmithy => MazeFeatureType.Smithy,
        OverworldGoal.ToStall => MazeFeatureType.Stall,
        OverworldGoal.ToDungeonEntrance => MazeFeatureType.DungeonEntrance,
        _ => null
    };

    /// <summary>
    /// The return trip: walking onto the Overworld's dungeon entrance starts a fresh dive.
    /// This is the reseed-and-restart logic that used to run immediately on shrine-exit, before
    /// the Overworld existed to place the hero in instead.
    /// </summary>
    private void StartFreshDungeonDive()
    {
        IsInOverworld = false;
        CurrentFloor = 0; // StartNewFloor increments to 1
        Seed = new Random().Next();
        _random = new Random(Seed);
        _mazeGenerator = new MazeGenerator(Seed);
        _movementSystem = new MovementSystem(Seed);
        _combatSystem = new CombatSystem(Seed);

        StartNewFloor();

        // Entry-time snapshot: closing the game mid-dungeon reverts to the dungeon entrance
        // with the state the hero carried in (regular floors themselves are never saved).
        SaveService.Save(this);
    }

    /// <summary>
    /// Returns true if the enemy can see the hero by scanning multiple facing angles
    /// </summary>
    private bool EnemyCanSeeHero(Enemy enemy)
    {
        int scanSteps = 8; // 8 directions (every 45 degrees)
        float scanStepRad = 2 * MathF.PI / scanSteps;
        for (int i = 0; i < scanSteps; i++)
        {
            float facing = i * scanStepRad;
            var visibleCells = GetDirectionalSightCone(enemy.X, enemy.Y, facing, VisionRange, VisionConeAngleRad);
            int heroCellX = (int)MathF.Round(Hero.X);
            int heroCellY = (int)MathF.Round(Hero.Y);
            if (visibleCells.Contains((heroCellX, heroCellY)))
                return true;
        }
        return false;
    }
    
    private bool HasLineOfSight(float x1, float y1, float x2, float y2)
    {
        // Use Bresenham's line algorithm to check if there's a wall between two points.
        // Round maps a position to its containing cell (integer coords = cell centers),
        // consistent with entity GridX/GridY, movement, and spawns.
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
            // Check if current position is a wall
            if (currentX >= 0 && currentX < CurrentMaze.Width && 
                currentY >= 0 && currentY < CurrentMaze.Height)
            {
                if (CurrentMaze.Walls[currentX, currentY])
                {
                    return false; // Wall blocks line of sight
                }
            }
            
            // Reached the end point
            if (currentX == endX && currentY == endY)
            {
                break;
            }
            
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
        
        return true; // No walls in the way
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
        // Generate new seed for different maze
        Seed = new Random().Next();
        
        // Reset death state
        IsHeroDead = false;
        DeathTimer = 0;
        
        // Reset game state (StartNewFloor increments CurrentFloor to 1 for the first floor)
        TickCount = 0;
        CurrentFloor = 0;
        StairsLocation = null;
        IsInSafeRoom = false;

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
        
        // Apply class and race stats
        _characterDataService.ApplyClassAndRace(Hero, _className, _raceName);
        
        // Update resource pools based on attributes
        UpdateHeroResourcePools();
        
        // Initialize resources to max
        Hero.CurrentStamina = Hero.MaxStamina;
        Hero.CurrentMana = Hero.MaxMana;
        Hero.CurrentFaith = Hero.MaxFaith;

        // Equip the class starting loadout and project it into executable attacks
        Hero.Loadout = AttackFactory.GetStartingLoadout(_className);
        Hero.Attacks = AttackFactory.ToAttacks(Hero.Loadout);
        Hero.CurrentAttack = Hero.Attacks.Count > 0 ? Hero.Attacks[0] : null;

        // Start new floor
        StartNewFloor();
        
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
        
        // Spawn enemies in random valid locations
        Enemies.Clear();
        Projectiles.Clear();   // don't let a lingering projectile carry into the new floor
        HitEffects.Clear();
        Boss = null;
        IsInSafeRoom = false;
        StairsLocation = null;
        int enemyCount = 3 + CurrentFloor;

        // Place stairs genuinely far from the entrance (real maze-solving distance via BFS, not
        // straight-line) so the floor is an actual maze-solving challenge rather than a coin flip.
        var distances = CurrentMaze.BfsDistancesFrom(1, 1);
        if (emptyCells.Count > 0)
        {
            var reachable = emptyCells.Where(c => distances.ContainsKey(c)).ToList();
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
        // Place chest (loot + XP only — no key/gating)
        if (emptyCells.Count > 0)
        {
            int chestIdx = _random.Next(emptyCells.Count);
            var chestCell = emptyCells[chestIdx];
            emptyCells.RemoveAt(chestIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = chestCell.x, Y = chestCell.y, Type = MazeFeatureType.Chest });
        }
        // Occasionally place a trap (environmental hazard, not every floor)
        if (emptyCells.Count > 0 && _random.NextDouble() < 0.4)
        {
            int trapIdx = _random.Next(emptyCells.Count);
            var trapCell = emptyCells[trapIdx];
            emptyCells.RemoveAt(trapIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = trapCell.x, Y = trapCell.y, Type = MazeFeatureType.Trap });
        }
        // No per-floor boss anymore — the significant fight is the Guardian at each safe-room gate.
        // Spawn regular enemies (weighted class, random race, random level in the floor's range)
        for (int i = 0; i < enemyCount && emptyCells.Count > 0; i++)
        {
            int idx = _random.Next(emptyCells.Count);
            var (x, y) = emptyCells[idx];
            emptyCells.RemoveAt(idx);
            var enemy = EnemyFactory.RandomRegular(CurrentFloor, _characterDataService, _random);
            enemy.X = x;
            enemy.Y = y;
            enemy.TargetX = x;
            enemy.TargetY = y;
            Enemies.Add(enemy);
        }
    }

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
                CurrentMaze.Walls[x, gy] = false;
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

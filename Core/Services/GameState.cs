using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Systems;

namespace TheMazeRPG.Core.Services
{

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
    public Enemy? Boss { get; set; }
    public bool HasKey { get; set; }
    public (int x, int y)? StairsLocation { get; set; }
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
    
    // Debug flags (can be toggled via env vars)
    public bool DebugDrawHitboxes { get; set; }
    public bool DebugDrawLOS { get; set; }
    
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
        Hero.ChestOpeningDuration = _chestOpeningTicks;

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
        
        // Check if hero died this tick
        if (!Hero.IsAlive && !IsHeroDead)
        {
            IsHeroDead = true;
            DeathTimer = 0;
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
        // Resources regenerate 30% slower during combat.
        float combatRegenModifier = Hero.InCombat ? 0.7f : 1.0f;
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
        float hpPerTick = Hero.EffectiveConstitution / (16f * _ticksPerSecond);
        _accumulatedHealthRegen += hpPerTick;
        
        if (_accumulatedHealthRegen >= 1.0f)
        {
            int hpToRestore = (int)_accumulatedHealthRegen;
            Hero.CurrentHp = Math.Min(Hero.MaxHp, Hero.CurrentHp + hpToRestore);
            _accumulatedHealthRegen -= hpToRestore;
        }
        
        // Move hero (unless opening chest)
        if (!Hero.IsOpeningChest)
        {
            if (!Hero.InCombat)
            {
                // If hero has key and knows where stairs are, path directly to them
                if (HasKey && StairsLocation.HasValue)
                {
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

        // Check for features (stairs, chests, key logic) - only if not in combat
        if (!Hero.InCombat)
        {
            CheckFeaturesWithKeyLogic();
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
                            int xpGain = 10 + enemy.MaxHp / 4;
                            Hero.GainExperience(xpGain);
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
    
    private void CheckFeaturesWithKeyLogic()
    {
        // Update chest opening animation
        foreach (var feature in CurrentMaze.Features.Where(f => f.IsOpening))
        {
            feature.OpeningTicks++;
            // Normalized 0..1 open progress (renderer uses this instead of a hardcoded duration)
            feature.OpenProgress = Math.Min(1f, feature.OpeningTicks / (float)Hero.ChestOpeningDuration);
            // Expand light radius during opening (0 to 2.0)
            feature.LightRadius = feature.OpenProgress * 2.0f;

            if (feature.OpeningTicks >= Hero.ChestOpeningDuration)
            {
                // Chest fully opened
                feature.IsOpening = false;
                feature.IsUsed = true;
                HasKey = true;
                Hero.IsOpeningChest = false;
                Hero.GainExperience(25);
            }
        }
        
        // If hero is opening a chest, don't check for other features
        if (Hero.IsOpeningChest)
        {
            Hero.ChestOpeningTicks++;
            if (Hero.ChestOpeningTicks >= Hero.ChestOpeningDuration)
            {
                Hero.IsOpeningChest = false;
                Hero.ChestOpeningTicks = 0;
            }
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
                // Start chest opening animation
                Hero.IsOpeningChest = true;
                Hero.ChestOpeningTicks = 0;
                feature.IsOpening = true;
                feature.OpeningTicks = 0;
                feature.LightRadius = 0f;
                continue; // Skip to next feature
            }
            
            // For stairs, allow close interaction regardless of cone visibility
            if (feature.Type == MazeFeatureType.Stairs)
            {
                // Close proximity usage (no cone requirement)
                if (distance < 0.6f)
                {
                    if (HasKey)
                    {
                        feature.IsUsed = true;
                        StartNewFloor();
                    }
                    else
                    {
                        StairsLocation = (feature.X, feature.Y);
                    }
                    continue;
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
        // If hero has key and remembers stairs, auto move to stairs
        if (HasKey && StairsLocation.HasValue)
        {
            float dx = Hero.X - StairsLocation.Value.x;
            float dy = Hero.Y - StairsLocation.Value.y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.6f)
            {
                var stairsFeature = CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.Stairs && !f.IsUsed);
                if (stairsFeature != null)
                {
                    stairsFeature.IsUsed = true;
                    StartNewFloor();
                }
            }
        }
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
        HasKey = false;
        StairsLocation = null;

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
        Hero.ChestOpeningDuration = _chestOpeningTicks;

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
        HasKey = false;
        StairsLocation = null;
        int enemyCount = 3 + CurrentFloor;
        // Place stairs
        if (emptyCells.Count > 0)
        {
            int stairsIdx = _random.Next(emptyCells.Count);
            var stairsCell = emptyCells[stairsIdx];
            emptyCells.RemoveAt(stairsIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = stairsCell.x, Y = stairsCell.y, Type = MazeFeatureType.Stairs });
        }
        // Place chest
        if (emptyCells.Count > 0)
        {
            int chestIdx = _random.Next(emptyCells.Count);
            var chestCell = emptyCells[chestIdx];
            emptyCells.RemoveAt(chestIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = chestCell.x, Y = chestCell.y, Type = MazeFeatureType.Chest });
        }
        // Place boss
        if (emptyCells.Count > 0)
        {
            int bossIdx = _random.Next(emptyCells.Count);
            var bossCell = emptyCells[bossIdx];
            emptyCells.RemoveAt(bossIdx);
            // Boss stats: stronger than regular enemies but beatable
            // Roughly 2-3x stronger than regular enemies
            Boss = new Enemy
            {
                X = bossCell.x,
                Y = bossCell.y,
                Level = CurrentFloor + 1,
                MaxHp = 80 + CurrentFloor * 25,  // Reduced from 300 + 50*floor
                Hp = 80 + CurrentFloor * 25,
                Attack = 8 + CurrentFloor * 2,   // Reduced from 20 + 3*floor
                Defense = 5 + CurrentFloor,       // Reduced from 10 + 2*floor
                Strength = 3 + CurrentFloor,      // Reduced from 5 + floor
                Constitution = 3 + CurrentFloor,  // Reduced from 5 + floor
                Agility = 2 + CurrentFloor,       // Reduced from 4 + floor
                Dexterity = 2 + CurrentFloor,     // Reduced from 3 + floor
                NoiseOffsetX = _random.NextDouble() * 100,
                NoiseOffsetY = _random.NextDouble() * 100,
                Type = "Boss",
                Class = "Boss",
                AttackSpeed = 25,                 // Slightly slower (was 20)
                AttackRange = 1.5f,
                TargetX = bossCell.x,
                TargetY = bossCell.y
            };
            Enemies.Add(Boss);
        }
        // Spawn regular enemies
        for (int i = 0; i < enemyCount && emptyCells.Count > 0; i++)
        {
            int idx = _random.Next(emptyCells.Count);
            var (x, y) = emptyCells[idx];
            emptyCells.RemoveAt(idx);
            string enemyType = GetRandomEnemyType();
            string enemyClass = GetRandomEnemyClass();
            int enemyLevel = CurrentFloor;
            int baseHp = 50 + enemyLevel * 15;
            int baseAtk = 3 + enemyLevel;
            int baseDef = 2 + enemyLevel / 2;
            float atkMod = 1.0f;
            float defMod = 1.0f;
            float hpMod = 1.0f;
            float range = 1.0f;
            int speed = 40;
            switch (enemyClass)
            {
                case "Brute": hpMod = 1.5f; defMod = 1.3f; atkMod = 1.1f; speed = 50; range = 1.0f; break;
                case "Striker": atkMod = 1.3f; speed = 25; range = 1.0f; break;
                case "Archer": atkMod = 1.2f; speed = 35; range = 2.5f; break;
                case "Caster": atkMod = 1.4f; hpMod = 0.8f; speed = 40; range = 3.0f; break;
            }
            var enemy = new Enemy
            {
                X = x,
                Y = y,
                Level = enemyLevel,
                MaxHp = (int)(baseHp * hpMod),
                Hp = (int)(baseHp * hpMod),
                Attack = (int)(baseAtk * atkMod),
                Defense = (int)(baseDef * defMod),
                Strength = enemyLevel + (enemyClass == "Brute" ? 3 : 0),
                Constitution = enemyLevel + (enemyClass == "Brute" ? 2 : 0),
                Agility = enemyLevel + (enemyClass == "Striker" ? 3 : 1),
                Dexterity = enemyLevel + (enemyClass == "Striker" ? 2 : 0),
                NoiseOffsetX = _random.NextDouble() * 100,
                NoiseOffsetY = _random.NextDouble() * 100,
                Type = enemyType,
                Class = enemyClass,
                AttackSpeed = speed,
                AttackRange = range,
                TargetX = x,
                TargetY = y
            };
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
    
    private string GetRandomEnemyType()
    {
        var types = new[] { "Slime", "Goblin", "Bat", "Skeleton" };
        return types[_random.Next(types.Length)];
    }
    
    private string GetRandomEnemyClass()
    {
        var classes = new[] { "Brute", "Striker", "Archer", "Caster" };
        return classes[_random.Next(classes.Length)];
    }
}
}

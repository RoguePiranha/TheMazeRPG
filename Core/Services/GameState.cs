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

    // Track enemy pursuit persistence
    private Dictionary<Enemy, int> enemyPursuitTicks = new();
    private const int PursuitTimeoutTicks = 30; // 3 seconds at 10 ticks/sec
    private const float AgroRadius = 7.5f; // Extended agro radius for persistence

    public int Seed { get; set; }
    public int TickCount { get; private set; }
    public int CurrentFloor { get; private set; } = 1;
    public bool IsRunning { get; set; }

    private readonly MazeGenerator _mazeGenerator;
    private readonly MovementSystem _movementSystem;
    private readonly CombatSystem _combatSystem;
    private readonly CharacterDataService _characterDataService;
    private readonly Random _random;
    
    public GameState(int seed) : this(seed, "Hero", "Wanderer", "Human")
    {
    }
    
    public GameState(int seed, string characterName, string className, string raceName)
    {
        Seed = seed;
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
        
        // Debug output
        Console.WriteLine($"Character Created: {Hero.Name} - {Hero.Race} {Hero.Class}");
        Console.WriteLine($"Colors - Race: {Hero.RaceColor}, Class: {Hero.ClassColor}");
        
        // Assign starting attacks based on class
        Hero.Attacks = AttackFactory.GetStartingAttacks(className);
        Hero.CurrentAttack = Hero.Attacks.Count > 0 ? Hero.Attacks[0] : null;
        Console.WriteLine($"Attacks assigned: {Hero.Attacks.Count}, Current: {Hero.CurrentAttack?.Name ?? "None"}");
        
        StartNewFloor();
    }
    
    public void Tick()
    {
        if (!IsRunning || !Hero.IsAlive) return;
        
        TickCount++;
        
        // Move hero
        if (!Hero.InCombat)
        {
            // Normal exploration movement
            _movementSystem.MoveHeroTowardUnexplored(Hero, CurrentMaze);
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
        
        // Move enemies
        double time = TickCount / 10.0;
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            if (!enemy.InCombat)
            {
                // Wander with Perlin noise when not in combat
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
        
        // Check for new combat encounters and process existing combat
        CheckCombat();
        
        // Check for features (stairs, chests, key logic) - only if not in combat
        if (!Hero.InCombat)
        {
            CheckFeaturesWithKeyLogic();
        }
        
        // Auto-switch hero attack randomly during combat
        if (Hero.InCombat && Hero.Attacks.Count > 1)
        {
            // Switch attack every 2 seconds (20 ticks at 10 ticks/sec)
            if (TickCount % 20 == 0)
            {
                int idx = _random.Next(Hero.Attacks.Count);
                Hero.CurrentAttack = Hero.Attacks[idx];
            }
        }
    }
    
    private void CheckCombat()
    {
        // Use directional sight cone for hero to identify enemies
        float heroFacing = Hero.InCombat && Hero.AttackCooldown == 0 && Hero.CurrentAttack != null
            ? MathF.Atan2(Hero.AnimationOffsetY, Hero.AnimationOffsetX)
            : 0f; // Default facing (e.g., right)
    // Set hero vision range to at least the farthest relevant attack range (enemy or hero), with a tunable minimum
    float maxEnemyRange = Enemies.Count > 0 ? Enemies.Max(e => e.AttackRange) : 7.5f;
    float heroAttackRange = Hero.CurrentAttack?.Range ?? 1.0f;
    float heroSightRange = MathF.Max(MathF.Max(maxEnemyRange, heroAttackRange), VisionRange);
        var heroVisibleCells = GetDirectionalSightCone(Hero.X, Hero.Y, heroFacing, heroSightRange, VisionConeAngleRad);
        Enemy? targetEnemy = null;
        float closestDistance = float.MaxValue;
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            int enemyCellX = (int)MathF.Round(enemy.X);
            int enemyCellY = (int)MathF.Round(enemy.Y);
            if (heroVisibleCells.Contains((enemyCellX, enemyCellY)))
            {
                float dx = Hero.X - enemy.X;
                float dy = Hero.Y - enemy.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    targetEnemy = enemy;
                }
            }
        }
        // Fallback: if nothing found in cone, allow close melee-range enemies or persistent pursuers to be targetable
        if (targetEnemy == null)
        {
            foreach (var enemy in Enemies.Where(e => e.IsAlive))
            {
                float dx = Hero.X - enemy.X;
                float dy = Hero.Y - enemy.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);

                // Close proximity check (melee overlap) - ensure immediate threats are detected
                float meleeThreshold = Math.Max(1.5f, enemy.AttackRange);
                bool isPersistent = enemyPursuitTicks.TryGetValue(enemy, out int t) && t > 0;

                if (distance <= meleeThreshold || (distance < AgroRadius && isPersistent))
                {
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        targetEnemy = enemy;
                    }
                    // If enemy in sight, reset pursuit
                    if (HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y) && distance < 5.0f)
                        enemyPursuitTicks[enemy] = PursuitTimeoutTicks;
                }
            }
        }
        // Decrement pursuit timers
        foreach (var enemy in Enemies)
        {
            if (enemyPursuitTicks.ContainsKey(enemy) && enemyPursuitTicks[enemy] > 0)
                enemyPursuitTicks[enemy]--;
        }
    if (targetEnemy == null)
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
        else
        {
            // Debug: optionally log hero sight info when a target is found
            // (kept minimal in production)
            // Console.WriteLine($"HeroSightRange={heroSightRange}, VisibleCells={heroVisibleCells.Count}, Target={targetEnemy?.Type}");
        }
        // Enemy found in sight cone
        if (!Hero.InCombat && !targetEnemy.InCombat)
        {
            _combatSystem.StartCombat(Hero, targetEnemy);
        }
        if (Hero.InCombat && targetEnemy.InCombat)
        {
            _combatSystem.ProcessCombat(Hero, targetEnemy, Projectiles);
        }
        foreach (var projectile in Projectiles)
        {
            projectile.Update(CurrentMaze);
        }
        Projectiles.RemoveAll(p => !p.IsActive);
    }
    
    private void CheckFeaturesWithKeyLogic()
    {
        // Use directional sight cone for hero to identify features
        float heroFacing = Hero.InCombat && Hero.AttackCooldown == 0 && Hero.CurrentAttack != null
            ? MathF.Atan2(Hero.AnimationOffsetY, Hero.AnimationOffsetX)
            : 0f; // Default facing (e.g., right)
        float heroSightRange = 7.5f;
        float heroConeRad = MathF.PI / 2; // 90-degree cone
        var heroVisibleCells = GetDirectionalSightCone(Hero.X, Hero.Y, heroFacing, heroSightRange, heroConeRad);
        foreach (var feature in CurrentMaze.Features.Where(f => !f.IsUsed))
        {
            int featureCellX = (int)MathF.Round(feature.X);
            int featureCellY = (int)MathF.Round(feature.Y);
            if (!heroVisibleCells.Contains((featureCellX, featureCellY))) continue;
            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.5f)
            {
                if (feature.Type == MazeFeatureType.Chest)
                {
                    feature.IsUsed = true;
                    HasKey = true;
                    Hero.GainExperience(25);
                }
                else if (feature.Type == MazeFeatureType.Stairs)
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
                }
            }
        }
        // If hero has key and remembers stairs, auto move to stairs
        if (HasKey && StairsLocation.HasValue)
        {
            float dx = Hero.X - StairsLocation.Value.x;
            float dy = Hero.Y - StairsLocation.Value.y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.5f)
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
    
    private bool HasLineOfSight(float x1, float y1, float x2, float y2)
    {
        // Use Bresenham's line algorithm to check if there's a wall between two points
        // Use Floor to map positions to the containing grid cell (consistent with tile centers)
        int startX = (int)MathF.Floor(x1);
        int startY = (int)MathF.Floor(y1);
        int endX = (int)MathF.Floor(x2);
        int endY = (int)MathF.Floor(y2);
        
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
    /// Returns all grid cells along the line of sight between two points
    /// </summary>
    public List<(int x, int y)> GetSightLine(float x1, float y1, float x2, float y2)
    {
        var cells = new List<(int x, int y)>();
        // Map positions to grid cells using Floor to match other systems that use Floor + 0.5 for centers
        int startX = (int)MathF.Floor(x1);
        int startY = (int)MathF.Floor(y1);
        int endX = (int)MathF.Floor(x2);
        int endY = (int)MathF.Floor(y2);
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
        // Use tile centers for origin so calculations match other systems (which use Floor(x)+0.5)
        float originCenterX = MathF.Floor(originX) + 0.5f;
        float originCenterY = MathF.Floor(originY) + 0.5f;
        int startX = (int)MathF.Floor(originX);
        int startY = (int)MathF.Floor(originY);
        int minX = Math.Max(0, startX - (int)range);
        int maxX = Math.Min(CurrentMaze.Width - 1, startX + (int)range);
        int minY = Math.Max(0, startY - (int)range);
        int maxY = Math.Min(CurrentMaze.Height - 1, startY + (int)range);
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                float dx = (x + 0.5f) - originCenterX;
                float dy = (y + 0.5f) - originCenterY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > range) continue;
                float cellAngle = MathF.Atan2(dy, dx);
                float angleDiff = MathF.Abs(NormalizeAngleRad(cellAngle - facingAngleRad));
                if (angleDiff <= coneAngleRad / 2)
                {
                    // Check line of sight to cell
                    if (HasLineOfSight(originCenterX, originCenterY, x + 0.5f, y + 0.5f))
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
        Boss = null;
        HasKey = false;
        StairsLocation = null;
        int enemyCount = 3 + CurrentFloor;
        // Reserve one cell for boss, one for chest, one for stairs
        var reservedCells = new List<(int x, int y)>();
        // Place stairs
        if (emptyCells.Count > 0)
        {
            int stairsIdx = _random.Next(emptyCells.Count);
            var stairsCell = emptyCells[stairsIdx];
            reservedCells.Add(stairsCell);
            emptyCells.RemoveAt(stairsIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = stairsCell.x, Y = stairsCell.y, Type = MazeFeatureType.Stairs });
        }
        // Place chest
        if (emptyCells.Count > 0)
        {
            int chestIdx = _random.Next(emptyCells.Count);
            var chestCell = emptyCells[chestIdx];
            reservedCells.Add(chestCell);
            emptyCells.RemoveAt(chestIdx);
            CurrentMaze.Features.Add(new MazeFeature { X = chestCell.x, Y = chestCell.y, Type = MazeFeatureType.Chest });
        }
        // Place boss
        if (emptyCells.Count > 0)
        {
            int bossIdx = _random.Next(emptyCells.Count);
            var bossCell = emptyCells[bossIdx];
            reservedCells.Add(bossCell);
            emptyCells.RemoveAt(bossIdx);
            // Boss stats: much higher, hero-like movement
            Boss = new Enemy
            {
                X = bossCell.x,
                Y = bossCell.y,
                Level = CurrentFloor + 2,
                MaxHp = 300 + CurrentFloor * 50,
                Hp = 300 + CurrentFloor * 50,
                Attack = 20 + CurrentFloor * 3,
                Defense = 10 + CurrentFloor * 2,
                Strength = 5 + CurrentFloor,
                Constitution = 5 + CurrentFloor,
                Agility = 4 + CurrentFloor,
                Dexterity = 3 + CurrentFloor,
                NoiseOffsetX = _random.NextDouble() * 100,
                NoiseOffsetY = _random.NextDouble() * 100,
                Type = "Boss",
                Class = "Boss",
                AttackSpeed = 20,
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
                Console.WriteLine($"Tick {i}: HeroHP={Hero.CurrentHp}/{Hero.MaxHp}, EnemyHP={enemy.Hp}/{enemy.MaxHp}, HeroInCombat={Hero.InCombat}, EnemyInCombat={enemy.InCombat}");
                // Also print hero sight and current attack for debugging
                float heroFacing = Hero.InCombat && Hero.AttackCooldown == 0 && Hero.CurrentAttack != null
                    ? MathF.Atan2(Hero.AnimationOffsetY, Hero.AnimationOffsetX)
                    : 0f;
                float maxEnemyRange = Enemies.Count > 0 ? Enemies.Max(e => e.AttackRange) : 7.5f;
                float heroAttackRange = Hero.CurrentAttack?.Range ?? 1.0f;
                float heroSightRange = MathF.Max(MathF.Max(maxEnemyRange, heroAttackRange), 3.0f);
                var visible = GetDirectionalSightCone(Hero.X, Hero.Y, heroFacing, heroSightRange, VisionConeAngleRad);
                Console.WriteLine($" Debug: heroSightRange={heroSightRange}, visionConeDeg={VisionConeAngleRad * 180.0f / MathF.PI:0.0}, visionRange={VisionRange}, visibleCells={visible.Count}, currentAttack={Hero.CurrentAttack?.Name}");
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
    
    private int GetAttackSpeedForType(string type)
    {
        return type switch
        {
            "Bat" => 20,      // Fast attacks
            "Goblin" => 35,   // Medium attacks
            "Slime" => 50,    // Slow attacks
            "Skeleton" => 30, // Medium-fast attacks
            _ => 40
        };
    }
    
    public void Reset()
    {
        TickCount = 0;
        CurrentFloor = 0;
        Hero = new Hero 
        { 
            Name = "Hero",
            Class = "Wanderer",
            Strength = 2,
            Constitution = 2,
            Agility = 2,
            Dexterity = 1,
            Intelligence = 1,
            Wisdom = 1,
            Charisma = 1,
            MaxHp = 100, 
            CurrentHp = 100, 
            Attack = 10, 
            Defense = 5 
        };
        Enemies.Clear();
        IsRunning = false;
        StartNewFloor();
    }
}
}

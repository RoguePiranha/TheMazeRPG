using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Systems;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Manages the current game state and simulation
/// </summary>
public class GameState
{
    public Hero Hero { get; set; }
    public Maze CurrentMaze { get; set; } = null!;
    public List<Enemy> Enemies { get; set; } = new();
    public Enemy? Boss { get; set; }
    public bool HasKey { get; set; }
    public (int x, int y)? StairsLocation { get; set; }
    public List<Projectile> Projectiles { get; set; } = new();
    
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
                    _movementSystem.MoveEnemyWithNoise(enemy, CurrentMaze, time);
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
    }
    
    private void CheckCombat()
    {
        // Find closest enemy for combat that has line of sight
        Enemy? targetEnemy = null;
        float closestDistance = float.MaxValue;
        
        foreach (var enemy in Enemies.Where(e => e.IsAlive))
        {
            float dx = Hero.X - enemy.X;
            float dy = Hero.Y - enemy.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            
            // Only consider enemies within detection range and with line of sight
            if (distance < 5.0f && HasLineOfSight(Hero.X, Hero.Y, enemy.X, enemy.Y))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    targetEnemy = enemy;
                }
            }
        }
        
        if (targetEnemy == null)
        {
            // No enemy in sight - end combat if we were fighting
            if (Hero.InCombat)
            {
                Hero.InCombat = false;
                Hero.AnimationOffsetX = 0;
                Hero.AnimationOffsetY = 0;
                
                // End combat for all enemies
                foreach (var enemy in Enemies)
                {
                    enemy.InCombat = false;
                }
            }
            return;
        }
        
        // Enemy found with line of sight
        if (!Hero.InCombat && !targetEnemy.InCombat)
        {
            // Start new combat
            _combatSystem.StartCombat(Hero, targetEnemy);
        }
        
        if (Hero.InCombat && targetEnemy.InCombat)
        {
            // Process ongoing combat (handles attacking and positioning)
            _combatSystem.ProcessCombat(Hero, targetEnemy, Projectiles);
        }
        
        // Update all projectiles with wall collision checking
        foreach (var projectile in Projectiles)
        {
            projectile.Update(CurrentMaze);
        }
        
        // Remove expired projectiles (including those that hit walls)
        Projectiles.RemoveAll(p => !p.IsActive);
    }
    
    private void CheckFeaturesWithKeyLogic()
    {
        foreach (var feature in CurrentMaze.Features.Where(f => !f.IsUsed))
        {
            float dx = Hero.X - feature.X;
            float dy = Hero.Y - feature.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 0.5f)
            {
                if (feature.Type == MazeFeatureType.Chest)
                {
                    feature.IsUsed = true;
                    // Chest contains key on each floor
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
                        // Remember stairs location for later
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
                // Use stairs
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

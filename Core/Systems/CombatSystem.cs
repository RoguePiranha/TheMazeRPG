using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Handles combat between hero and enemies
/// </summary>
public class CombatSystem
{
    private readonly Random _random;
    
    public CombatSystem(int seed)
    {
        _random = new Random(seed);
    }
    
    /// <summary>
    /// Update combat cooldowns and execute attacks when ready
    /// </summary>
    public bool ProcessCombat(Hero hero, Enemy enemy, List<Projectile> projectiles, Maze maze)
    {
        if (!hero.IsAlive || !enemy.IsAlive) 
        {
            hero.InCombat = false;
            enemy.InCombat = false;
            hero.AnimationOffsetX = 0;
            hero.AnimationOffsetY = 0;
            return false;
        }
        
        // Decay animation offsets back to normal
        hero.AnimationOffsetX *= 0.8f;
        hero.AnimationOffsetY *= 0.8f;
        if (MathF.Abs(hero.AnimationOffsetX) < 0.01f) hero.AnimationOffsetX = 0;
        if (MathF.Abs(hero.AnimationOffsetY) < 0.01f) hero.AnimationOffsetY = 0;
        
        // Update hero attack cooldown
        if (hero.AttackCooldown > 0)
        {
            hero.AttackCooldown--;
        }
        else
        {
            // Check if hero is in range to attack
            var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8, Range = 1.0f, Cooldown = 20, CritChance = 0.05f, Animation = AttackAnimation.Melee };
            float dx = (float)enemy.X - hero.X;
            float dy = (float)enemy.Y - hero.Y;
            float distanceToEnemy = MathF.Sqrt(dx * dx + dy * dy);

            // Only attack if within range
            // Note: Vision system already ensures LOS for combat initiation
            // For ranged attacks, require clear line of sight UNLESS enemy is very close (melee overlap)
            // This allows ranged attackers to shoot at point-blank range even around corners
            // Melee attacks don't need LOS check - allows fighting around corners
            bool isMeleeRange = distanceToEnemy <= 1.5f;
            bool hasLOS = attack.Range <= 1.5f || isMeleeRange || HasLineOfSight(hero.X, hero.Y, enemy.X, enemy.Y, maze);
            
            // Add small epsilon for floating point comparison to handle edge cases (e.g., exactly 3.0 distance with 3.0 range)
            if (distanceToEnemy <= attack.Range + 0.01f && hasLOS)
            {
                // Check if we have enough resources for this attack
                bool canAfford = !attack.IsHeavyAttack ||
                    (hero.CurrentStamina >= attack.StaminaCost &&
                     hero.CurrentMana >= attack.ManaCost &&
                     hero.CurrentFaith >= attack.FaithCost);
                
                if (canAfford)
                {
                    // Hero attacks!
                    Console.WriteLine($"  → Hero attacks with {attack.Name}! (Distance: {distanceToEnemy:0.2f}, Range: {attack.Range})");
                    PerformHeroAttack(hero, enemy, projectiles);

                    // Set cooldown based on attack, Dexterity, and minimum threshold
                    int minCooldown = 8; // Lower minimum for snappier combat
                    int statCooldown = attack.Cooldown - (int)(hero.Dexterity * 0.7f);
                    hero.AttackCooldown = Math.Max(minCooldown, statCooldown);

                    // Check if enemy died
                    if (!enemy.IsAlive)
                    {
                        Console.WriteLine($"  ✓ Enemy defeated!");
                        int xpGain = 10 + enemy.MaxHp / 4;
                        hero.GainExperience(xpGain);
                        hero.InCombat = false;
                        enemy.InCombat = false;
                        hero.AnimationOffsetX = 0;
                        hero.AnimationOffsetY = 0;

                        // Clear any projectiles targeting this enemy to prevent lingering animations
                        projectiles.Clear();

                        return false;
                    }
                }
                else
                {
                    // Can't afford attack - reset cooldown to try again soon
                    Console.WriteLine($"  ✗ Hero can't afford {attack.Name} (Stamina: {hero.CurrentStamina}/{attack.StaminaCost}, Mana: {hero.CurrentMana}/{attack.ManaCost})");
                    hero.AttackCooldown = 5;
                }
            }
            else if (distanceToEnemy <= attack.Range + 0.01f)
            {
                // In range but no LOS (ranged attack blocked by wall) - retry soon
                Console.WriteLine($"  ✗ Hero attack blocked - no line of sight (Distance: {distanceToEnemy:0.2f}, Range: {attack.Range})");
                hero.AttackCooldown = 5;
            }
            else
            {
                // Not in range - log for debugging melee issues
                Console.WriteLine($"  ✗ Hero out of range (Distance: {distanceToEnemy:0.2f}, Range: {attack.Range})");
            }
        }
        
        // Update enemy attack cooldown
        if (enemy.AttackCooldown > 0)
        {
            enemy.AttackCooldown--;
        }
        else
        {
            // Check if enemy is in their attack range (use actual enemy position, not target)
            float enemyDx = hero.X - enemy.X;
            float enemyDy = hero.Y - enemy.Y;
            float distanceToHero = MathF.Sqrt(enemyDx * enemyDx + enemyDy * enemyDy);

            // Enemy only attacks if within their attack range
            // Note: Vision system already ensures LOS for combat initiation
            // For ranged enemies, require clear line of sight UNLESS hero is very close (melee overlap)
            // This allows ranged enemies to shoot at point-blank range even around corners
            // Melee enemies don't need LOS check - allows fighting around corners
            bool isEnemyMeleeRange = distanceToHero <= 1.5f;
            bool enemyHasLOS = enemy.AttackRange <= 1.5f || isEnemyMeleeRange || HasLineOfSight(enemy.X, enemy.Y, hero.X, hero.Y, maze);
            
            // Add small epsilon for floating point comparison
            if (distanceToHero <= enemy.AttackRange + 0.01f && enemyHasLOS)
            {
                // Enemy attacks!
                Console.WriteLine($"  → Enemy attacks! (Distance: {distanceToHero:0.2f}, Range: {enemy.AttackRange})");
                // Stat-driven enemy damage
                int statEnemyDamage = enemy.Attack
                    + (int)(enemy.Strength * 1.1f)
                    + (int)(enemy.Dexterity * 0.4f);

                // Apply damage, factoring hero defense and Constitution
                int finalEnemyDamage = CalculateDamage(statEnemyDamage, hero.Defense + (int)(hero.Constitution * 0.7f));
                hero.CurrentHp -= finalEnemyDamage;

                // Cooldown based on enemy attack speed, Dexterity, and Agility
                int minEnemyCooldown = 10;
                int statEnemyCooldown = enemy.AttackSpeed
                    - (int)(enemy.Dexterity * 0.5f)
                    - (int)(enemy.Agility * 0.2f);
                enemy.AttackCooldown = Math.Max(minEnemyCooldown, statEnemyCooldown);

                // Enemy attack animation (stronger lunge)
                float dx = hero.X - enemy.X;
                float dy = hero.Y - enemy.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 0)
                {
                    dx /= dist;
                    dy /= dist;
                }
                enemy.AnimationOffsetX = dx * 0.6f; // More impactful
                enemy.AnimationOffsetY = dy * 0.6f;

                // Spawn enemy attack projectile (melee for now)
                projectiles.Add(new Projectile
                {
                    StartX = enemy.X,
                    StartY = enemy.Y,
                    CurrentX = enemy.X,
                    CurrentY = enemy.Y,
                    TargetX = hero.X,
                    TargetY = hero.Y,
                    Speed = 0.45f,
                    Type = AttackAnimation.Melee,
                    AttackName = "Enemy Attack",
                    MaxLifeTime = 18
                });

                // Check if hero died
                if (!hero.IsAlive)
                {
                    hero.CurrentHp = 0;
                    hero.InCombat = false;
                    enemy.InCombat = false;
                    hero.AnimationOffsetX = 0;
                    hero.AnimationOffsetY = 0;
                    enemy.AnimationOffsetX = 0;
                    enemy.AnimationOffsetY = 0;
                    // Clear projectiles when combat ends
                    projectiles.Clear();
                    return false;
                }
            }
            else if (distanceToHero <= enemy.AttackRange)
            {
                // In range but no LOS (ranged attack blocked by wall) - retry soon
                enemy.AttackCooldown = 10;
            }
        }
        
        return true; // Combat is still active
    }
    
    private void PerformHeroAttack(Hero hero, Enemy enemy, List<Projectile> projectiles)
    {
        var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8 };
        
        // Deduct resources for heavy attacks
        if (attack.IsHeavyAttack)
        {
            hero.CurrentStamina -= attack.StaminaCost;
            hero.CurrentMana -= attack.ManaCost;
            hero.CurrentFaith -= attack.FaithCost;
        }
        
        // Calculate direction to enemy
        float dx = (float)enemy.X - hero.X;
        float dy = (float)enemy.Y - hero.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        
        if (distance > 0)
        {
            dx /= distance;
            dy /= distance;
        }
        
        // Apply attack animation movement and spawn projectiles
        switch (attack.Animation)
        {
            case AttackAnimation.Melee:
                // Lunge forward toward enemy - spawn dagger/blade projectile
                hero.AnimationOffsetX = dx * 0.4f;
                hero.AnimationOffsetY = dy * 0.4f;
                
                // Create weapon trail effect
                projectiles.Add(new Projectile
                {
                    StartX = hero.X,
                    StartY = hero.Y,
                    CurrentX = hero.X,
                    CurrentY = hero.Y,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.4f,
                    Type = AttackAnimation.Melee,
                    AttackName = attack.Name,
                    MaxLifeTime = 15
                });
                break;
                
            case AttackAnimation.Ranged:
                // Stay steady and shoot
                hero.AnimationOffsetX = -dx * 0.05f; // Minimal recoil
                hero.AnimationOffsetY = -dy * 0.05f;
                
                // Spawn arrow/dart projectile
                projectiles.Add(new Projectile
                {
                    StartX = hero.X,
                    StartY = hero.Y,
                    CurrentX = hero.X,
                    CurrentY = hero.Y,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.5f,
                    Type = AttackAnimation.Ranged,
                    AttackName = attack.Name,
                    MaxLifeTime = 25
                });
                break;
                
            case AttackAnimation.Heavy:
                // Big lunge forward
                hero.AnimationOffsetX = dx * 0.5f;
                hero.AnimationOffsetY = dy * 0.5f;
                
                // Heavy weapon arc
                projectiles.Add(new Projectile
                {
                    StartX = hero.X,
                    StartY = hero.Y,
                    CurrentX = hero.X,
                    CurrentY = hero.Y,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.3f,
                    Type = AttackAnimation.Heavy,
                    AttackName = attack.Name,
                    MaxLifeTime = 20
                });
                break;
                
            case AttackAnimation.Quick:
                // Quick dart in and out for rogue
                hero.AnimationOffsetX = dx * 0.5f;
                hero.AnimationOffsetY = dy * 0.5f;
                
                // Quick strike flash
                projectiles.Add(new Projectile
                {
                    StartX = hero.X,
                    StartY = hero.Y,
                    CurrentX = hero.X,
                    CurrentY = hero.Y,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.6f,
                    Type = AttackAnimation.Quick,
                    AttackName = attack.Name,
                    MaxLifeTime = 10
                });
                break;
                
            case AttackAnimation.Magic:
                // Stay still for magic - minimal movement
                hero.AnimationOffsetX = 0;
                hero.AnimationOffsetY = 0;
                
                // Spawn magic missile
                projectiles.Add(new Projectile
                {
                    StartX = hero.X,
                    StartY = hero.Y,
                    CurrentX = hero.X,
                    CurrentY = hero.Y,
                    AttackName = attack.Name,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.35f,
                    Type = AttackAnimation.Magic,
                    MaxLifeTime = 30
                });
                break;
        }
        
        // Stat-driven damage calculation
        // Physical attacks scale with Strength + Dexterity
        // Magic attacks scale with Intelligence + Wisdom
        // Faith attacks scale with Wisdom + Charisma
        int statDamage = attack.Damage + hero.Attack;
        
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0)
        {
            // Magic damage: Intelligence primary, Wisdom secondary
            statDamage += (int)(hero.Intelligence * 1.5f) + (int)(hero.Wisdom * 0.5f);
        }
        else if (attack.FaithCost > 0)
        {
            // Faith damage: Wisdom primary, Charisma secondary
            statDamage += (int)(hero.Wisdom * 1.5f) + (int)(hero.Charisma * 0.5f);
        }
        else
        {
            // Physical damage: Strength primary, Dexterity secondary
            statDamage += (int)(hero.Strength * 1.2f) + (int)(hero.Dexterity * 0.5f);
        }

        // Check for critical hit (Dexterity boosts crit chance)
        float critChance = attack.CritChance + hero.Dexterity * 0.005f;
        float critRoll = (float)_random.NextDouble();
        if (critRoll < critChance)
        {
            statDamage = (int)(statDamage * 1.5f);
        }

        // Apply damage, factoring enemy defense
        // Physical defense: Constitution
        // Magic defense: Constitution + bonus magic resist
        int enemyDefense = enemy.Defense + (int)(enemy.Constitution * 0.7f);
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0 || attack.FaithCost > 0)
        {
            // Add magic resistance (enemies have base 20% magic resist)
            enemyDefense += (int)(enemyDefense * 0.2f);
        }
        int finalDamage = CalculateDamage(statDamage, enemyDefense);
        enemy.Hp -= finalDamage;

        // Set cooldown based on attack, Dexterity, Agility, and Charisma
        int minCooldown = 8;
        int statCooldown = attack.Cooldown
            - (int)(hero.Dexterity * 0.7f)
            - (int)(hero.Agility * 0.3f)
            - (int)(hero.Charisma * 0.2f); // Charisma provides slight cooldown reduction
        hero.AttackCooldown = Math.Max(minCooldown, statCooldown);
    }
    
    /// <summary>
    /// Initialize combat between hero and enemy
    /// </summary>
    public void StartCombat(Hero hero, Enemy enemy)
    {
        hero.InCombat = true;
        enemy.InCombat = true;
        
        // Set initial cooldowns - give both combatants a small delay before first attack
        // This prevents instant attacks on combat start
        hero.AttackCooldown = 3; // Small delay before hero can attack
        enemy.AttackCooldown = enemy.AttackSpeed / 2;
    }
    
    private int CalculateDamage(int attack, int defense)
    {
        // Base damage with some randomness
        int baseDamage = Math.Max(1, attack - defense / 2);
        int variance = _random.Next(-baseDamage / 4, baseDamage / 4 + 1);
        return Math.Max(1, baseDamage + variance);
    }
    
    /// <summary>
    /// Check if there's a clear line of sight between two points (no walls blocking)
    /// </summary>
    private bool HasLineOfSight(float x1, float y1, float x2, float y2, Maze maze)
    {
        // Use Bresenham's line algorithm to check if there's a wall between two points
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
            if (currentX >= 0 && currentX < maze.Width && 
                currentY >= 0 && currentY < maze.Height)
            {
                if (maze.Walls[currentX, currentY])
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
}

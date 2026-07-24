using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

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
            // Require LOS for all attacks, except when bodies overlap (corner cases)
            bool overlappingBodies = distanceToEnemy <= (hero.Radius + enemy.Radius + 0.05f);
            bool hasLOS = overlappingBodies || HasLineOfSight(hero.X, hero.Y, enemy.X, enemy.Y, maze);
            
            // Hitbox-aware range check: shrink required distance by enemy radius for contact
            float effectiveDistance = MathF.Max(0f, distanceToEnemy - enemy.Radius);
            // Add small epsilon for floating point comparison to handle edge cases (e.g., exactly 3.0 distance with 3.0 range)
            if (effectiveDistance <= attack.Range + 0.01f && hasLOS)
            {
                // Check if we have enough resources for this attack
                bool canAfford = !attack.IsHeavyAttack ||
                    (hero.CurrentStamina >= attack.StaminaCost &&
                     hero.CurrentMana >= attack.ManaCost &&
                     hero.CurrentFaith >= attack.FaithCost);
                
                if (canAfford)
                {
                    GameLog.Debug($"  → Hero attacks with {attack.Name}! (Distance: {distanceToEnemy:F2}, Range: {attack.Range})");
                    PerformHeroAttack(hero, enemy, projectiles);

                    // Set cooldown based on attack, Dexterity, and minimum threshold
                    int minCooldown = 8; // Lower minimum for snappier combat
                    int statCooldown = attack.Cooldown - (int)(hero.EffectiveDexterity * 0.7f);
                    hero.AttackCooldown = Math.Max(minCooldown, statCooldown);

                    // Damage is applied on projectile contact in GameState.ProcessProjectileCollisions,
                    // which also awards XP and ends combat on a kill. No synchronous kill check here.
                }
                else
                {
                    // Can't afford attack - reset cooldown to try again soon
                    GameLog.Debug($"  ✗ Hero can't afford {attack.Name} (Stamina: {hero.CurrentStamina}/{attack.StaminaCost}, Mana: {hero.CurrentMana}/{attack.ManaCost})");
                    hero.AttackCooldown = 5;
                }
            }
            else if (effectiveDistance <= attack.Range + 0.01f)
            {
                // In range but no LOS (ranged attack blocked by wall) - retry soon
                GameLog.Debug($"  ✗ Hero attack blocked - no line of sight (Distance: {distanceToEnemy:F2}, Range: {attack.Range})");
                hero.AttackCooldown = 5;
            }
            else
            {
                GameLog.Debug($"  ✗ Hero out of range (Distance: {distanceToEnemy:F2}, Range: {attack.Range})");
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
            // Require LOS for all attacks, except when bodies overlap (corner cases)
            bool overlappingVsHero = distanceToHero <= (hero.Radius + enemy.Radius + 0.05f);
            bool enemyHasLOS = overlappingVsHero || HasLineOfSight(enemy.X, enemy.Y, hero.X, hero.Y, maze);
            
            // Hitbox-aware range check for enemy attacks
            float enemyEffectiveDistance = MathF.Max(0f, distanceToHero - hero.Radius);
            // Add small epsilon for floating point comparison
            if (enemyEffectiveDistance <= enemy.AttackRange + 0.01f && enemyHasLOS)
            {
                // Enemy attacks!
                GameLog.Debug($"  → Enemy attacks! (Distance: {distanceToHero:F2}, Range: {enemy.AttackRange})");
                // Stat-driven enemy damage
                int statEnemyDamage = enemy.Attack
                    + (int)(enemy.Strength * 1.1f)
                    + (int)(enemy.Dexterity * 0.4f);
                // Calculate final damage, factoring hero defense and Constitution
                int finalEnemyDamage = CalculateDamage(statEnemyDamage, hero.Defense + (int)(hero.EffectiveConstitution * 0.7f));

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

                // Spawn enemy attack projectile (melee hitbox)
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
                    MaxLifeTime = 12,
                    Team = ProjectileTeam.Enemy,
                    Damage = finalEnemyDamage,
                    Radius = 0.45f,
                    CanHitMultiple = false
                });

                // Hero death will be handled when the projectile hits (if lethal)
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
        // Compute damage now, but apply it on projectile contact
        int statDamage = attack.Damage + hero.Attack;
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0)
        {
            // Magic damage scaling
            statDamage += (int)(hero.EffectiveIntelligence * 1.5f) + (int)(hero.EffectiveWisdom * 0.5f);
        }
        else if (attack.FaithCost > 0)
        {
            // Faith damage scaling
            statDamage += (int)(hero.EffectiveWisdom * 1.5f) + (int)(hero.EffectiveCharisma * 0.5f);
        }
        else
        {
            // Physical damage scaling
            statDamage += (int)(hero.EffectiveStrength * 1.2f) + (int)(hero.EffectiveDexterity * 0.5f);
        }

        // Critical hit chance
        float critChance = attack.CritChance + hero.EffectiveDexterity * 0.005f;
        float critRoll = (float)_random.NextDouble();
        if (critRoll < critChance)
        {
            statDamage = (int)(statDamage * 1.5f);
        }

        // Compute target's defense (apply magic resist for spells)
        int enemyDefense = enemy.Defense + (int)(enemy.Constitution * 0.7f);
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0 || attack.FaithCost > 0)
        {
            enemyDefense += (int)(enemyDefense * 0.2f); // base 20% magic resist
        }
        int finalDamage = CalculateDamage(statDamage, enemyDefense);

        // Choose the visual style from the attack's stable id (not its display name).
        var visual = AttackVisuals.For(attack);

        // Apply attack animation movement and spawn damage-carrying projectiles/hitboxes
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
                    Visual = visual,
                    MaxLifeTime = 12,
                    Team = ProjectileTeam.Hero,
                    Damage = Math.Max(1, finalDamage),
                    Radius = 0.45f,
                    CanHitMultiple = false
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
                    Visual = visual,
                    MaxLifeTime = 25,
                    Team = ProjectileTeam.Hero,
                    Damage = Math.Max(1, finalDamage),
                    Radius = 0.22f,
                    CanHitMultiple = false
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
                    Visual = visual,
                    MaxLifeTime = 14,
                    Team = ProjectileTeam.Hero,
                    Damage = Math.Max(1, (int)(finalDamage * 1.15f)),
                    Radius = 0.55f,
                    CanHitMultiple = false
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
                    Visual = visual,
                    MaxLifeTime = 10,
                    Team = ProjectileTeam.Hero,
                    Damage = Math.Max(1, finalDamage),
                    Radius = 0.4f,
                    CanHitMultiple = false
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
                    Visual = visual,
                    TargetX = (float)enemy.X,
                    TargetY = (float)enemy.Y,
                    Speed = 0.35f,
                    Type = AttackAnimation.Magic,
                    MaxLifeTime = 30,
                    Team = ProjectileTeam.Hero,
                    Damage = Math.Max(1, finalDamage),
                    Radius = attack.Id == "arcane-blast" ? 0.35f : 0.25f,
                    CanHitMultiple = attack.Id == "arcane-blast" // treat blast as AoE ring that can hit multiple once
                });
                break;
        }
        
        // Set cooldown based on attack, Dexterity, Agility, and Charisma
        int minCooldown = 8;
        int statCooldown = attack.Cooldown
            - (int)(hero.EffectiveDexterity * 0.7f)
            - (int)(hero.EffectiveAgility * 0.3f)
            - (int)(hero.EffectiveCharisma * 0.2f); // Charisma provides slight cooldown reduction
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
    /// Process only the enemy's side of combat (cooldown + attack), leaving hero's cooldown untouched.
    /// </summary>
    public void ProcessEnemyOnlyAttack(Hero hero, Enemy enemy, List<Projectile> projectiles, Maze maze)
    {
        if (!hero.IsAlive || !enemy.IsAlive) return;
        if (!enemy.InCombat) return;

        if (enemy.AttackCooldown > 0)
        {
            enemy.AttackCooldown--;
            return;
        }

        float enemyDx = hero.X - enemy.X;
        float enemyDy = hero.Y - enemy.Y;
        float distanceToHero = MathF.Sqrt(enemyDx * enemyDx + enemyDy * enemyDy);

    // Require LOS for all attacks, except when bodies overlap (corner cases)
    bool overlappingVsHero = distanceToHero <= (hero.Radius + enemy.Radius + 0.05f);
    bool enemyHasLOS = overlappingVsHero || HasLineOfSight(enemy.X, enemy.Y, hero.X, hero.Y, maze);
        float enemyEffectiveDistance = MathF.Max(0f, distanceToHero - hero.Radius);
        if (enemyEffectiveDistance <= enemy.AttackRange + 0.01f && enemyHasLOS)
        {
            int statEnemyDamage = enemy.Attack
                + (int)(enemy.Strength * 1.1f)
                + (int)(enemy.Dexterity * 0.4f);
            int finalEnemyDamage = CalculateDamage(statEnemyDamage, hero.Defense + (int)(hero.EffectiveConstitution * 0.7f));

            // Enemy attack animation (lunge)
            float dx = hero.X - enemy.X;
            float dy = hero.Y - enemy.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > 0) { dx /= dist; dy /= dist; }
            enemy.AnimationOffsetX = dx * 0.6f;
            enemy.AnimationOffsetY = dy * 0.6f;

            // Spawn enemy melee hitbox projectile
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
                MaxLifeTime = 12,
                Team = ProjectileTeam.Enemy,
                Damage = finalEnemyDamage,
                Radius = 0.45f,
                CanHitMultiple = false
            });

            int minEnemyCooldown = 10;
            int statEnemyCooldown = enemy.AttackSpeed
                - (int)(enemy.Dexterity * 0.5f)
                - (int)(enemy.Agility * 0.2f);
            enemy.AttackCooldown = Math.Max(minEnemyCooldown, statEnemyCooldown);
        }
        else if (enemyEffectiveDistance <= enemy.AttackRange + 0.01f)
        {
            enemy.AttackCooldown = 10; // retry soon when blocked by LOS
        }
    }
    
    /// <summary>
    /// Check if there's a clear line of sight between two points (no walls blocking)
    /// </summary>
    private bool HasLineOfSight(float x1, float y1, float x2, float y2, Maze maze)
    {
        // Use Bresenham's line algorithm. Round maps a position to its containing cell
        // (integer coords = cell centers), consistent with entity GridX/movement.
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

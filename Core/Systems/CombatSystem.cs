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
    public bool ProcessCombat(Hero hero, Enemy enemy, List<Projectile> projectiles)
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
            var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8, Range = 1.0f };
            float dx = (float)enemy.X - hero.X;
            float dy = (float)enemy.Y - hero.Y;
            float distanceToEnemy = MathF.Sqrt(dx * dx + dy * dy);
            
            // Only attack if within range
            if (distanceToEnemy <= attack.Range)
            {
                // Hero attacks!
                PerformHeroAttack(hero, enemy, projectiles);
                
                // Check if enemy died
                if (!enemy.IsAlive)
                {
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
        }
        
        // Update enemy attack cooldown
        if (enemy.AttackCooldown > 0)
        {
            enemy.AttackCooldown--;
        }
        else
        {
            // Check if enemy is in their attack range
            float enemyDx = hero.X - enemy.TargetX;
            float enemyDy = hero.Y - enemy.TargetY;
            float distanceToHero = MathF.Sqrt(enemyDx * enemyDx + enemyDy * enemyDy);
            
            // Enemy only attacks if within their attack range
            if (distanceToHero <= enemy.AttackRange)
            {
                // Enemy attacks!
                int enemyDamage = CalculateDamage(enemy.Attack, hero.Defense);
                hero.CurrentHp -= enemyDamage;
                enemy.AttackCooldown = enemy.AttackSpeed;

                // Enemy attack animation (simple lunge)
                float dx = hero.X - enemy.X;
                float dy = hero.Y - enemy.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > 0)
                {
                    dx /= dist;
                    dy /= dist;
                }
                enemy.AnimationOffsetX = dx * 0.4f;
                enemy.AnimationOffsetY = dy * 0.4f;

                // Spawn enemy attack projectile (melee for now)
                projectiles.Add(new Projectile
                {
                    StartX = enemy.X,
                    StartY = enemy.Y,
                    CurrentX = enemy.X,
                    CurrentY = enemy.Y,
                    TargetX = hero.X,
                    TargetY = hero.Y,
                    Speed = 0.4f,
                    Type = AttackAnimation.Melee,
                    AttackName = "Enemy Attack",
                    MaxLifeTime = 15
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
        }
        
        return true; // Combat is still active
    }
    
    private void PerformHeroAttack(Hero hero, Enemy enemy, List<Projectile> projectiles)
    {
        var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8 };
        
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
        
        // Calculate damage based on attack and hero stats
        int baseDamage = attack.Damage + hero.Attack;
        
        // Check for critical hit
        float critRoll = (float)_random.NextDouble();
        if (critRoll < attack.CritChance)
        {
            baseDamage = (int)(baseDamage * 1.5f);
        }
        
        // Apply damage
        int finalDamage = CalculateDamage(baseDamage, enemy.Defense);
        enemy.Hp -= finalDamage;
        
        // Set cooldown based on attack and dexterity
        int attackCooldown = attack.Cooldown - hero.Dexterity;
        hero.AttackCooldown = Math.Max(10, attackCooldown);
    }
    
    /// <summary>
    /// Initialize combat between hero and enemy
    /// </summary>
    public void StartCombat(Hero hero, Enemy enemy)
    {
        hero.InCombat = true;
        enemy.InCombat = true;
        
        // Set initial cooldowns (hero attacks first)
        hero.AttackCooldown = 0;
        enemy.AttackCooldown = enemy.AttackSpeed / 2;
    }
    
    private int CalculateDamage(int attack, int defense)
    {
        // Base damage with some randomness
        int baseDamage = Math.Max(1, attack - defense / 2);
        int variance = _random.Next(-baseDamage / 4, baseDamage / 4 + 1);
        return Math.Max(1, baseDamage + variance);
    }
}

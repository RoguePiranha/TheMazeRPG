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
                     hero.CurrentMana >= AffinityService.EffectiveManaCost(hero.Affinities, MagicElements.For(attack), attack.ManaCost) &&
                     hero.CurrentFaith >= attack.FaithCost);
                
                if (canAfford)
                {
                    GameLog.Debug($"  → Hero attacks with {attack.Name}! (Distance: {distanceToEnemy:F2}, Range: {attack.Range})");
                    PerformHeroAttack(hero, enemy, projectiles);
                    // Cooldown is set inside SpawnHeroProjectile (attack + Dex/Agi/Cha
                    // reductions) — one formula for every fire path. A Dex-only copy here used
                    // to overwrite it, making Auto combat slower for agile/charismatic heroes
                    // than the identical manual attack.

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
                // Enemy attacks! (damage/visual come from its equipped attack + effective stats)
                GameLog.Debug($"  → Enemy attacks! (Distance: {distanceToHero:F2}, Range: {enemy.AttackRange})");
                PerformEnemyAttack(hero, enemy, projectiles);
            }
            else if (enemyEffectiveDistance <= enemy.AttackRange + 0.01f)
            {
                // In range but no LOS (ranged attack blocked by wall) - retry soon. Same
                // hitbox-aware distance as the attack branch above (they had drifted apart,
                // leaving a band where a blocked enemy re-evaluated every tick with no delay
                // and fired the instant LOS opened).
                enemy.AttackCooldown = 10;
            }
        }
        
        return true; // Combat is still active
    }
    
    private void PerformHeroAttack(Hero hero, Enemy enemy, List<Projectile> projectiles)
    {
        var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8 };
        DeductAttackCost(hero, attack);

        // Direction to the target enemy.
        float dx = enemy.X - hero.X;
        float dy = enemy.Y - hero.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > 0) { dx /= distance; dy /= distance; }

        // Target-locked: resolve final damage against this enemy's defense now (baked into Damage).
        int statDamage = ComputeHeroStatDamage(hero, attack);
        int enemyDefense = enemy.Defense + (int)(enemy.Constitution * 0.7f);
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0 || attack.FaithCost > 0)
            enemyDefense += (int)(enemyDefense * 0.2f); // base 20% magic resist
        int finalDamage = CalculateDamage(statDamage, enemyDefense);
        // Target's elemental resistance to this attack's element.
        finalDamage = AffinityService.ApplyResistance(finalDamage, enemy.Affinities, MagicElements.For(attack));

        SpawnHeroProjectile(hero, attack, dx, dy, enemy.X, enemy.Y, finalDamage, isStatDamage: false, projectiles);

        // Practice develops the hero's affinity to the element cast (and shifts opposed/harmonious).
        AffinityService.OnElementCast(hero.Affinities, MagicElements.For(attack));
    }

    /// <summary>
    /// Fire the hero's current attack in a free direction (Manual click-to-fire). The projectile
    /// travels along the aim and its damage is resolved against whatever enemy it strikes (it
    /// carries the pre-defense stat damage — see Projectile.StatDamage), rather than being locked
    /// to a specific target like <see cref="PerformHeroAttack"/>.
    /// </summary>
    public void PerformHeroDirectionalAttack(Hero hero, float dirX, float dirY, List<Projectile> projectiles)
    {
        var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Damage = 8, Range = 1.0f, Cooldown = 20, CritChance = 0.05f, Animation = AttackAnimation.Melee };

        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 0.001f) return;
        dirX /= len;
        dirY /= len;

        DeductAttackCost(hero, attack);
        int statDamage = ComputeHeroStatDamage(hero, attack);

        // Ranged/magic fly a long way (capped by projectile lifetime); melee-ish are a short
        // lunge hitbox thrown just in front of the hero along the aim.
        bool flies = attack.Animation == AttackAnimation.Ranged || attack.Animation == AttackAnimation.Magic;
        float travel = flies ? 14f : MathF.Max(attack.Range, 1.0f);
        float targetX = hero.X + dirX * travel;
        float targetY = hero.Y + dirY * travel;

        SpawnHeroProjectile(hero, attack, dirX, dirY, targetX, targetY, statDamage, isStatDamage: true, projectiles);

        // Practice develops the hero's affinity to the element cast (and shifts opposed/harmonious).
        AffinityService.OnElementCast(hero.Affinities, MagicElements.For(attack));
    }

    /// <summary>Perform a cell-targeted tactical attack. Validation remains GameState's
    /// responsibility; unlike real-time directional fire, the projectile stops at the selected
    /// cell so authored range and the targeting preview describe the same action.</summary>
    public void PerformHeroTacticalAttack(Hero hero, float targetX, float targetY,
        List<Projectile> projectiles)
    {
        var attack = hero.CurrentAttack ?? new Attack
        {
            Name = "Unarmed Strike",
            Damage = 8,
            Range = 1.0f,
            Cooldown = 20,
            CritChance = 0.05f,
            Animation = AttackAnimation.Melee
        };
        float dirX = targetX - hero.X;
        float dirY = targetY - hero.Y;
        float length = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (length < 0.001f) return;
        dirX /= length;
        dirY /= length;

        DeductAttackCost(hero, attack);
        int statDamage = ComputeHeroStatDamage(hero, attack);
        SpawnHeroProjectile(hero, attack, dirX, dirY, targetX, targetY,
            statDamage, isStatDamage: true, projectiles,
            spawnAtTarget: attack.Id == "arcane-blast");
        AffinityService.OnElementCast(hero.Affinities, MagicElements.For(attack));
    }

    private static void DeductAttackCost(Hero hero, Attack attack)
    {
        if (!attack.IsHeavyAttack) return;
        hero.CurrentStamina -= attack.StaminaCost;
        // Only the mana portion is affinity-discounted (elemental spellcasting); stamina/faith aren't.
        hero.CurrentMana -= AffinityService.EffectiveManaCost(hero.Affinities, MagicElements.For(attack), attack.ManaCost);
        hero.CurrentFaith -= attack.FaithCost;
    }

    /// <summary>Offensive stat-scaled damage for a hero attack, before target defense; includes
    /// a crit roll. Shared by target-locked and directional attacks.</summary>
    // Damage pipeline (owner ruling 2026-08-05): damage flows from the systems, never from flat
    // attack stats or hardcoded floors — weapon × stats × proficiency × elemental affinity ×
    // creature scale. Stats AMPLIFY the weapon multiplicatively, so gear quality and character
    // build both genuinely matter. Coefficients are the two tuning knobs for overall pacing.
    private const float PrimaryStatCoeff = 0.15f;   // +15% weapon damage per primary-stat point
    private const float SecondaryStatCoeff = 0.06f; // +6% per secondary-stat point

    private static int ComputeWeaponDamage(int weaponDamage, float primaryStat, float secondaryStat,
        float proficiencyMult, float affinityMult, float sizeScale)
    {
        float statMult = 1f + primaryStat * PrimaryStatCoeff + secondaryStat * SecondaryStatCoeff;
        return Math.Max(1, (int)MathF.Round(weaponDamage * statMult * proficiencyMult * affinityMult * sizeScale));
    }

    private int ComputeHeroStatDamage(Hero hero, Attack attack)
    {
        WeaponUseProfile weaponUse = WeaponProficiencyService.Evaluate(hero, attack);
        var element = MagicElements.For(attack);
        float affinityMult = AffinityService.PowerMultiplier(hero.Affinities.Get(element));

        // The weapon term: the technique/spell's own power, plus held-weapon damage for
        // physical strikes (a Quick Slash through an iron sword cuts with the sword).
        int weaponBase;
        float primary, secondary;
        if (attack.Animation == AttackAnimation.Magic || attack.ManaCost > 0)
        {
            weaponBase = attack.Damage;
            primary = hero.EffectiveIntelligence;
            secondary = hero.EffectiveWisdom;
        }
        else if (attack.FaithCost > 0)
        {
            weaponBase = attack.Damage;
            primary = hero.EffectiveWisdom;
            secondary = hero.EffectiveCharisma;
        }
        else
        {
            weaponBase = attack.Damage + hero.EquippedWeaponDamage;
            primary = hero.EffectiveStrength;
            secondary = hero.EffectiveDexterity;
        }

        int statDamage = ComputeWeaponDamage(weaponBase, primary, secondary,
            weaponUse.DamageMultiplier, affinityMult, sizeScale: 1f);

        float critChance = attack.CritChance + hero.EffectiveDexterity * 0.005f;
        if ((float)_random.NextDouble() < critChance)
            statDamage = (int)(statDamage * 1.5f);
        return statDamage;
    }

    /// <summary>
    /// Spawn the hero's attack projectile (shared by target-locked and directional fire) with the
    /// per-animation visual/speed/lifetime/hitbox and lunge animation. <paramref name="damage"/>
    /// is the final damage when <paramref name="isStatDamage"/> is false (baked into Projectile.Damage),
    /// or the pre-defense stat damage when true (into Projectile.StatDamage, resolved on hit). Also
    /// sets the hero's post-attack cooldown.
    /// </summary>
    private static void SpawnHeroProjectile(Hero hero, Attack attack, float dirX, float dirY,
        float targetX, float targetY, int damage, bool isStatDamage, List<Projectile> projectiles,
        bool spawnAtTarget = false)
    {
        var visual = AttackVisuals.For(attack);
        var element = MagicElements.For(attack);
        WeaponUseProfile weaponUse = WeaponProficiencyService.Evaluate(hero, attack);

        void Spawn(float speed, AttackAnimation type, int maxLife, float radius, float dmgMul, bool multi)
        {
            int d = Math.Max(1, (int)(damage * dmgMul));
            projectiles.Add(new Projectile
            {
                StartX = hero.X,
                StartY = hero.Y,
                CurrentX = spawnAtTarget ? targetX : hero.X,
                CurrentY = spawnAtTarget ? targetY : hero.Y,
                TargetX = targetX,
                TargetY = targetY,
                Speed = speed,
                Type = type,
                AttackName = attack.Name,
                Visual = visual,
                Element = element,
                Accuracy = hero.EffectiveDexterity * weaponUse.AccuracyMultiplier,
                MaxLifeTime = maxLife,
                Team = ProjectileTeam.Hero,
                Damage = isStatDamage ? 0 : d,
                StatDamage = isStatDamage ? d : 0,
                Radius = radius,
                CanHitMultiple = multi,
                // The same "counts as magic" rule the target-locked path applies at spawn time
                // (animation OR a mana/faith cost), carried on the projectile so directional
                // shots resolve magic resist identically on contact.
                IsMagic = attack.Animation == AttackAnimation.Magic ||
                          attack.ManaCost > 0 || attack.FaithCost > 0
            });
        }

        switch (attack.Animation)
        {
            case AttackAnimation.Melee:
                hero.AnimationOffsetX = dirX * 0.4f;
                hero.AnimationOffsetY = dirY * 0.4f;
                Spawn(0.4f, AttackAnimation.Melee, 12, 0.45f, 1f, false);
                break;

            case AttackAnimation.Ranged:
                hero.AnimationOffsetX = -dirX * 0.05f; // minimal recoil
                hero.AnimationOffsetY = -dirY * 0.05f;
                Spawn(0.5f, AttackAnimation.Ranged, 25, 0.22f, 1f, false);
                break;

            case AttackAnimation.Heavy:
                hero.AnimationOffsetX = dirX * 0.5f;
                hero.AnimationOffsetY = dirY * 0.5f;
                Spawn(0.3f, AttackAnimation.Heavy, 14, 0.55f, 1.15f, false);
                break;

            case AttackAnimation.Quick:
                hero.AnimationOffsetX = dirX * 0.5f;
                hero.AnimationOffsetY = dirY * 0.5f;
                Spawn(0.6f, AttackAnimation.Quick, 10, 0.4f, 1f, false);
                break;

            case AttackAnimation.Magic:
                hero.AnimationOffsetX = 0;
                hero.AnimationOffsetY = 0;
                bool aoe = attack.Id == "arcane-blast"; // AoE ring that can hit multiple once
                Spawn(0.35f, AttackAnimation.Magic, 30, aoe ? 0.35f : 0.25f, 1f, aoe);
                break;
        }

        // Cooldown from attack + Dexterity/Agility/Charisma reductions.
        int minCooldown = 8;
        int statCooldown = attack.Cooldown
            - (int)(hero.EffectiveDexterity * 0.7f)
            - (int)(hero.EffectiveAgility * 0.3f)
            - (int)(hero.EffectiveCharisma * 0.2f);
        hero.AttackCooldown = Math.Max(minCooldown, statCooldown);
    }

    /// <summary>
    /// Execute an enemy's attack: damage and visual come from its equipped attack plus its
    /// (race-effective) stats, mirroring how the hero's attacks scale. Spawns the damage-carrying
    /// projectile and sets the enemy's cooldown.
    /// </summary>
    private void PerformEnemyAttack(Hero hero, Enemy enemy, List<Projectile> projectiles)
    {
        var atk = enemy.CurrentAttack;
        var animation = atk?.Animation ?? AttackAnimation.Melee;

        // Weapon × stats × affinity × creature scale, mirroring the hero pipeline (owner ruling
        // 2026-08-05). No flat attack stat, no boss multiplier/floor — a big creature hits harder
        // because SizeScale says so, and a support-class boss hits like the support it is.
        bool magic = atk != null && (atk.Animation == AttackAnimation.Magic || atk.ManaCost > 0);
        bool faith = atk != null && atk.FaithCost > 0;
        int weaponBase = atk?.Damage ?? 3; // bare-handed strike when a kit somehow has no attack
        float primary = magic ? enemy.Intelligence : faith ? enemy.Wisdom : enemy.Strength;
        float secondary = magic ? enemy.Wisdom : faith ? enemy.Charisma : enemy.Dexterity;

        var element = atk != null ? MagicElements.For(atk) : MagicElement.None;
        float affinityMult = AffinityService.PowerMultiplier(enemy.Affinities.Get(element));

        int statDamage = ComputeWeaponDamage(weaponBase, primary, secondary,
            proficiencyMult: 1f /* enemies are trained in their own kit */,
            affinityMult, enemy.SizeScale);

        int finalDamage = CalculateDamage(statDamage,
            hero.Defense + hero.EquipmentDefenseBonus + (int)(hero.EffectiveConstitution * 0.7f));

        // Hero's elemental resistance to this attack's element (baked in; the enemy projectile's
        // Damage is applied directly on hit).
        finalDamage = AffinityService.ApplyResistance(finalDamage, hero.Affinities, element);

        // Lunge animation toward the hero.
        float dx = hero.X - enemy.X;
        float dy = hero.Y - enemy.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 0) { dx /= dist; dy /= dist; }
        enemy.AnimationOffsetX = dx * 0.6f;
        enemy.AnimationOffsetY = dy * 0.6f;

        // Projectile shape by animation type (ranged/magic travel; melee is a short hitbox).
        float speed;
        int maxLife;
        float radius;
        switch (animation)
        {
            case AttackAnimation.Ranged: speed = 0.5f; maxLife = 25; radius = 0.25f; break;
            case AttackAnimation.Magic: speed = 0.4f; maxLife = 30; radius = 0.3f; break;
            default: speed = 0.45f; maxLife = 12; radius = 0.45f; break; // melee/heavy/quick
        }

        projectiles.Add(new Projectile
        {
            StartX = enemy.X,
            StartY = enemy.Y,
            CurrentX = enemy.X,
            CurrentY = enemy.Y,
            TargetX = hero.X,
            TargetY = hero.Y,
            Speed = speed,
            Type = animation,
            AttackName = atk?.Name ?? "Enemy Attack",
            // Same "{Race} {Class}" identity the Codex bestiary uses, so a death reads
            // "slain by a Goblin Mage".
            SourceLabel = $"{enemy.Race} {enemy.Class}".Trim(),
            Visual = atk != null ? AttackVisuals.For(atk) : VisualStyle.Blade,
            Element = atk != null ? MagicElements.For(atk) : MagicElement.None,
            Accuracy = enemy.Dexterity,
            MaxLifeTime = maxLife,
            Team = ProjectileTeam.Enemy,
            Damage = finalDamage,
            Radius = radius,
            CanHitMultiple = false
        });

        // Cooldown from attack speed, reduced slightly by Dexterity/Agility.
        int minEnemyCooldown = 10;
        int statCooldown = enemy.AttackSpeed - (int)(enemy.Dexterity * 0.5f) - (int)(enemy.Agility * 0.2f);
        enemy.AttackCooldown = Math.Max(minEnemyCooldown, statCooldown);
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
            PerformEnemyAttack(hero, enemy, projectiles);
        }
        else if (enemyEffectiveDistance <= enemy.AttackRange + 0.01f)
        {
            enemy.AttackCooldown = 10; // retry soon when blocked by LOS
        }
    }

    /// <summary>Attempt exactly one enemy attack for a tactical turn. Cooldowns are intentionally
    /// ignored because the turn action itself is the rate limit.</summary>
    public bool TryPerformTacticalEnemyAttack(Hero hero, Enemy enemy, List<Projectile> projectiles, Maze maze)
    {
        if (!CanPerformTacticalEnemyAttack(hero, enemy, maze)) return false;

        PerformEnemyAttack(hero, enemy, projectiles);
        return true;
    }

    public bool CanPerformTacticalEnemyAttack(Hero hero, Enemy enemy, Maze maze)
    {
        if (!hero.IsAlive || !enemy.IsAlive) return false;

        float dx = hero.X - enemy.X;
        float dy = hero.Y - enemy.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        bool overlapping = distance <= hero.Radius + enemy.Radius + 0.05f;
        bool hasLineOfSight = overlapping || HasLineOfSight(enemy.X, enemy.Y, hero.X, hero.Y, maze);
        float effectiveDistance = MathF.Max(0f, distance - hero.Radius);
        return effectiveDistance <= enemy.AttackRange + 0.01f && hasLineOfSight;
    }
    
    /// <summary>Clear line between two points — delegates to the one shared walk (SightLine).</summary>
    private bool HasLineOfSight(float x1, float y1, float x2, float y2, Maze maze) =>
        SightLine.Clear(maze, x1, y1, x2, y2);
}

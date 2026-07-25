using System.Collections.Generic;
namespace TheMazeRPG.Core.Models;

/// <summary>
/// Threat tier of an enemy. Basic is the common case; Elite is a rarer, tougher regular
/// enemy; Boss is the one-per-floor set piece. Drives XP reward multiplier (xlsx: Basic 1x /
/// Elite 1.5x / Boss 2x), loot-drop chance, and a visual distinction in the renderer.
/// </summary>
public enum EnemyTier
{
    Basic,
    Elite,
    Boss
}

/// <summary>
/// Represents an enemy in the maze
/// </summary>
public class Enemy
{
    public float X { get; set; }
    public float Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Level { get; set; } = 1;

    // Stats. These store the enemy's RACE-EFFECTIVE, class-and-level-grown values
    // (race effectiveness is baked in at generation by EnemyFactory), so combat formulas
    // use them directly.
    public int Strength { get; set; } = 1;
    public int Constitution { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Dexterity { get; set; } = 1;
    public int Intelligence { get; set; } = 1;
    public int Wisdom { get; set; } = 1;
    public int Charisma { get; set; } = 1;

    // For Perlin-based movement
    public double NoiseOffsetX { get; set; }
    public double NoiseOffsetY { get; set; }

    public bool IsAlive => Hp > 0;
    public string Type { get; set; } = "Slime";
    public string Class { get; set; } = "Warrior"; // Character class (Warrior/Mage/Rogue/...) — drives gear, stats, shape
    public string Race { get; set; } = "Human";
    public EnemyTier Tier { get; set; } = EnemyTier.Basic;
    public bool IsBoss => Tier == EnemyTier.Boss;
    public bool IsElite => Tier == EnemyTier.Elite;

    /// <summary>XP reward multiplier for this tier, from the "Monster Tier / XP Reward" table in
    /// Levels and Stats.xlsx (Basic ≈25.08, Elite ≈37.625, Boss ≈50.17 → exactly 1x / 1.5x / 2x).</summary>
    public float XpMultiplier => Tier switch
    {
        EnemyTier.Elite => 1.5f,
        EnemyTier.Boss => 2.0f,
        _ => 1.0f
    };

    // The enemy's equipped/primary attack (from its class loadout); drives damage scaling + visual.
    public Attack? CurrentAttack { get; set; }
    
    // Combat state
    public bool InCombat { get; set; }
    public int AttackSpeed { get; set; } = 40; // Ticks between attacks
    public int AttackCooldown { get; set; }
    public float AttackRange { get; set; } = 1.0f; // How close they need to be to attack
    // Collision radius in tiles (used for hitbox-based interactions)
    public float Radius { get; set; } = 0.35f;
    
    // Smooth movement for combat
    public float TargetX { get; set; }
    public float TargetY { get; set; }

    // Animation state for attack movement
    public float AnimationOffsetX { get; set; }
    public float AnimationOffsetY { get; set; }

    // Temporary data for AI/movement (e.g., velocity)
    public Dictionary<string, object>? TempData { get; set; }
}

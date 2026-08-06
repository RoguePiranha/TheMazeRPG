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

    /// <summary>
    /// Whether this creature can work a door (owner ruling 2026-08-06: anything with hands and
    /// the wits to use them opens doors like the hero does — bump-to-open; a shut door only
    /// stops what can't manage a handle). True for every current humanoid enemy; note 14's
    /// creature templates set this false for the mindless and the handless — zombies, beasts,
    /// oozes — for whom a closed door genuinely ends a pursuit.
    /// </summary>
    public bool CanOpenDoors { get; set; } = true;
    public bool IsBoss => Tier == EnemyTier.Boss;
    public bool IsElite => Tier == EnemyTier.Elite;

    /// <summary>Creature bulk (owner rulings 2026-08-05) — the one shared size knob: large
    /// creatures deliver proportionally harder hits (damage pipeline, replacing the old hardcoded
    /// boss ×1.35 + level floor) and, when stealth lands, are proportionally easier to spot and
    /// louder (small = sneaky, big = seen — Planning note 11). Set by EnemyFactory per tier for
    /// humanoids (1.0 / 1.1 / 1.3); creature templates (Dire bears, trolls, dragons) carry their own.</summary>
    public float SizeScale { get; set; } = 1f;

    // Generated-dungeon context. Regular enemies belong to a coherent encounter group and either
    // remain near their home room or follow a short route through connected rooms while idle.
    public int EncounterId { get; set; } = -1;
    public int HomeRoomId { get; set; } = -1;
    public List<int> PatrolRoomIds { get; set; } = new();
    public int PatrolRouteIndex { get; set; }

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

    // Elemental affinity profile (seeded from race+class, static — enemies don't grow). Gives its
    // magic attacks damage/cost scaling and grants it resistance to matching incoming elements.
    public Affinities Affinities { get; set; } = new();

    // Loot carried on the body — populated on death (see GameState.HandleEnemyDefeated) and
    // transferred to the hero by looting the corpse (right-click → Loot). Gold is a small coin drop.
    public List<Combinable> Inventory { get; set; } = new();
    public int Gold { get; set; }
    
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

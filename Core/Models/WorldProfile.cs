using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>Region dimensions and population for one WorldSize (Data/Config/worldgen.json).
/// The defaults mirror the "Small" profile so a missing or unparsable worldgen.json degrades to a
/// playable small world — the region generator's geometry (60-tile mountain, 100-tile cleared
/// band, forest fringe, river) physically cannot fit in anything much smaller, and an undersized
/// fallback used to crash town generation outright.</summary>
public class WorldSizeProfile
{
    public int RegionWidth { get; set; } = 400;
    public int RegionHeight { get; set; } = 384;
    public int Population { get; set; } = 60;

    /// <summary>Depth in tiles of the forest fringe beyond the cleared band. Consumed by the region
    /// generator (PR 6); stored now so the size profile is complete in one table.</summary>
    public int ForestDepth { get; set; } = 32;
}

/// <summary>
/// Multipliers one Hostility level applies over tunables that already exist elsewhere in the game —
/// the payoff of having centralized them. Every field here has a live consumer; knobs whose systems
/// don't exist yet are deliberately left out rather than stubbed (a setting that reads nothing is
/// worse than an absent one). Reserved for later PRs: guard patrol coverage (note 09), offscreen
/// event frequency (note 17), wildlife temperament (note 14).
/// </summary>
public class HostilityProfile
{
    /// <summary>Scales the per-floor enemy count.</summary>
    public float EnemyDensity { get; set; } = 1f;

    /// <summary>Shifts the level band enemies spawn in, clamped so levels stay at least 1.</summary>
    public int LevelOffset { get; set; }

    /// <summary>Absolute chance (not a multiplier) that a regular spawn rolls Elite.</summary>
    public float EliteChance { get; set; } = 0.18f;

    /// <summary>Shifts the per-floor trap chance; +/-1 step of the base 0.4 roll.</summary>
    public float TrapChanceDelta { get; set; }
}

/// <summary>
/// The resolved knob set for the active world: its size profile plus its hostility profile. Loaded
/// from Data/Config/worldgen.json and cached like GameSettings.Current, then re-resolved whenever
/// the active world changes. Gameplay code reads these properties and stays unaware that a world
/// option chose them.
/// </summary>
public class WorldProfile
{
    public WorldSizeProfile Size { get; set; } = new();
    public HostilityProfile Hostility { get; set; } = new();

    // Convenience passthroughs so consumers read one short property, not a two-level path.
    public float EnemyDensity => Hostility.EnemyDensity;
    public int LevelOffset => Hostility.LevelOffset;
    public float EliteChance => Hostility.EliteChance;
    public float TrapChanceDelta => Hostility.TrapChanceDelta;
}

/// <summary>The whole worldgen.json document: one profile table per option.</summary>
public class WorldGenConfig
{
    public Dictionary<string, WorldSizeProfile> Sizes { get; set; } = new();
    public Dictionary<string, HostilityProfile> Hostility { get; set; } = new();
}

using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

public class CharacterClass
{
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#808080";
    public Dictionary<string, int> StatModifiers { get; set; } = new();
    public Dictionary<string, int> StartingStats { get; set; } = new();
    public Dictionary<string, int> StatGrowth { get; set; } = new();
    public HashSet<WeaponType> WeaponAffinities { get; set; } = new();

    /// <summary>Optional starting elemental affinity leanings, keyed by MagicElement name
    /// ("Fire", "Arcane", ...). Added over the neutral baseline when a character of this class is
    /// created (a lean, not a full specialization). See AffinityService / Affinities.</summary>
    public Dictionary<string, float> Affinities { get; set; } = new();
}

public class CharacterRace
{
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>
    /// Per-attribute effectiveness multipliers (see Info/Racial Effectiveness.md). These do NOT
    /// change base/displayed stats; they scale how effectively a stat translates into results:
    /// EffectiveAttribute = BaseAttribute × Effectiveness. Defaults to 1.0 when absent.
    /// </summary>
    public Dictionary<string, float> Effectiveness { get; set; } = new();

    /// <summary>Testing-only race (e.g. "Debug"): selectable in character creation, but excluded
    /// from enemy generation and bestiary totals.</summary>
    public bool Debug { get; set; }

    /// <summary>Whether this race sees normally in darkness within its usual range of vision
    /// (owner ruling 2026-08-05: mapped from D&D — Elf/Dwarf/Goblin/Kobold/Orc/Tiefling yes;
    /// Human/Halfling/Dragonborn no). Consumed by the night-lighting layer.</summary>
    public bool Darkvision { get; set; }

    /// <summary>Creature bulk (owner ruling 2026-08-05: the smaller you are, the easier it is to
    /// sneak, and vice versa — Planning note 11). Feeds Hero.SizeScale: the stealth footprint —
    /// step-noise radius and how fast watchers' awareness climbs at the sight of you. A Kobold is
    /// a natural burglar; a Dragonborn in iron announces themselves.</summary>
    public float Size { get; set; } = 1f;

    // Optional flat pool overrides for testing races: when set, they replace the normally
    // derived max pools (and fill the current value to match). Null = normal derivation.
    public int? HealthOverride { get; set; }
    public int? StaminaOverride { get; set; }
    public int? ManaOverride { get; set; }

    /// <summary>Optional flat base-stat overrides for testing races (e.g. Debug's 100s). Keyed by
    /// attribute name ("Strength", ...); applied after the class's starting stats. Null = use the
    /// class stats unchanged.</summary>
    public Dictionary<string, int>? StatOverrides { get; set; }

    /// <summary>Optional starting elemental affinity leanings, keyed by MagicElement name; added
    /// over the neutral baseline (stacks with the class's leanings). See AffinityService.</summary>
    public Dictionary<string, float> Affinities { get; set; } = new();
}

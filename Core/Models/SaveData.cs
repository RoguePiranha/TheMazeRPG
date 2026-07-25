using System;
using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// A persisted snapshot of the hero's progress, written to disk so it survives an app restart
/// (unlike the in-process-only preservation the dungeon shrine already provides). Regular
/// dungeon floors are never saved — the dungeon is a transient per-dive space (the "Trial") —
/// so a save resumes either in the Overworld or at a safe-room checkpoint (see SafeRoomFloor).
/// Death deletes the hero's save entirely (permadeath). See SaveService.
/// </summary>
public class SaveData
{
    public int Version { get; set; } = 1;

    /// <summary>Identifies this save slot on disk (Saves/{SaveId}.json) — stable across repeated
    /// saves of the same character so re-saving overwrites rather than multiplying files.</summary>
    public string SaveId { get; set; } = "";
    public double PlaytimeSeconds { get; set; }
    public DateTime SavedAtUtc { get; set; }

    /// <summary>Where continuing this save resumes. Null = the Overworld dungeon entrance (the
    /// default). A value N means the save was made in the interstitial safe room after floor N
    /// (e.g. 4 = safe room "4.5") — safe rooms are the only mid-dungeon save points, and
    /// continuing rebuilds that safe room and resumes there.</summary>
    public int? SafeRoomFloor { get; set; }

    public string HeroName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string RaceName { get; set; } = "";

    public int Level { get; set; }
    public int Experience { get; set; }
    public int ExperienceToNext { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }

    // Base (displayed) stats — not the race-effective values, which are re-derived from
    // ClassName/RaceName on load via the normal ApplyClassAndRace + level-up path.
    public int Strength { get; set; }
    public int Constitution { get; set; }
    public int Agility { get; set; }
    public int Dexterity { get; set; }
    public int Intelligence { get; set; }
    public int Wisdom { get; set; }
    public int Charisma { get; set; }

    public int Gold { get; set; }
    public Dictionary<string, int> Resources { get; set; } = new();
    public List<Combinable> Loadout { get; set; } = new();
    public List<Combinable> Inventory { get; set; } = new();
}

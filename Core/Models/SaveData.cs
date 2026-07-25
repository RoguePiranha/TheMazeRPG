using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TheMazeRPG.Core.Models;

/// <summary>Where continuing a save resumes the hero.</summary>
public enum ResumePoint
{
    /// <summary>Default for saves that predate this field. Standing at the Overworld's dungeon
    /// entrance — used for shrine exits and mid-dive quits (regular floors are never saved).</summary>
    OverworldEntrance,

    /// <summary>A brand-new character who hasn't reached their first safe room yet. Resuming
    /// starts a fresh dive from floor 1 (the character is preserved; dungeon progress isn't) —
    /// they've never seen the Overworld, so there's no entrance to stand at.</summary>
    DungeonStart,

    /// <summary>The safe-room checkpoint recorded in SafeRoomFloor.</summary>
    SafeRoom
}

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

    /// <summary>Where continuing this save resumes — see ResumePoint. Stored as a string in the
    /// JSON for readability/robustness against enum reordering.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResumePoint ResumePoint { get; set; } = ResumePoint.OverworldEntrance;

    /// <summary>The safe-room checkpoint's floor when ResumePoint is SafeRoom: N means the
    /// interstitial safe room after floor N (e.g. 4 = safe room "4.5" before Guardian floor 5).
    /// Null otherwise.</summary>
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

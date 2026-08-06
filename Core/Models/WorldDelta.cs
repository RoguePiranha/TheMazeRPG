using System;
using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// A character who died in this world. Permadeath deletes the character *file*, but the world
/// remembers the person (owner ruling 2026-08-05: character death keeps the world). NPCs who knew
/// them carry a memory that surfaces as memorial barks to later characters (note 09 / PR 8).
/// </summary>
public class LegacyHero
{
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }

    /// <summary>Lifetime levels gained, which class advancement resets Level but not. Until PR 1's
    /// XP rework adds Hero.TotalLevelsGained this mirrors Level — the best available approximation.
    /// </summary>
    public int TotalLevelsGained { get; set; }

    /// <summary>Game days survived, from the character's WorldClock.</summary>
    public int DaysLived { get; set; }

    /// <summary>Built from the killing blow, e.g. "slain by a Goblin Mage".</summary>
    public string CauseOfDeath { get; set; } = "";

    /// <summary>e.g. "the Dungeon, floor 7" / "the town".</summary>
    public string DeathLocation { get; set; } = "";

    public int Kills { get; set; }

    /// <summary>Shapes how they're remembered — "Don't say that name here" overrides warmth in the
    /// bark system. Always 0 until NPCs exist to be murdered (PR 8).</summary>
    public int InnocentKills { get; set; }

    public int Gold { get; set; }
    public DateTime DiedAtUtc { get; set; }

    public string SummaryLine =>
        $"{Name} the {Race} {Class}, level {Level} — {CauseOfDeath} in {DeathLocation} after {DaysLived} day(s)";
}

/// <summary>
/// Everything that has HAPPENED to a world, as opposed to how it was generated (that's WorldData).
/// Written to Saves/Worlds/{worldId}/delta.json and updated over the world's whole life, across any
/// number of characters.
/// </summary>
public class WorldDelta
{
    public int Version { get; set; } = 1;

    /// <summary>World truth, not per-hero truth: an NPC killed by one character stays dead for
    /// every character afterward. Populated when NPCs land (PR 8).</summary>
    public List<string> DeadNpcIds { get; set; } = new();

    public List<LegacyHero> FallenHeroes { get; set; } = new();

    /// <summary>npcId → the names of fallen heroes that NPC remembers, for memorial barks. Populated
    /// alongside the Opinion system (note 09).</summary>
    public Dictionary<string, List<string>> NpcMemories { get; set; } = new();

    /// <summary>The furthest world-clock time any character here has reached, so the worlds picker
    /// can show elapsed days. Characters each keep their own clock; this is a display high-water
    /// mark, not a shared simulation time.</summary>
    public double HighWaterGameMinutes { get; set; }

    // Reserved, documented now and populated by later PRs so the file format doesn't churn:
    //   TileDeltas   — construction/destruction of world tiles (PR 6b onward)
    //   PlacedItems  — the world-item/container layer (note 16, PR 6b)
    //   Reputation   — per-NPC and per-faction standing (note 09, PR 9)
    //   EventLog     — offscreen event history (note 17, PR 9c)
}

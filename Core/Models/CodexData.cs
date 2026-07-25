using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>One discovered "{Race} {Class}" enemy archetype's encounter history.</summary>
public class BestiaryEntry
{
    public string Name { get; set; } = "";
    public int Seen { get; set; }
    public int Killed { get; set; }
    public int FirstFloor { get; set; }
    public int LastFloor { get; set; }
}

/// <summary>Cross-run play statistics.</summary>
public class PlayStats
{
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int DeepestFloor { get; set; }
    public int TotalFloorsCleared { get; set; }
    /// <summary>Times the player voluntarily left the dungeon via a safe-room shrine (distinct
    /// from dying — the hero's progress is preserved on exit, unlike on death).</summary>
    public int TotalDungeonExits { get; set; }
}

/// <summary>Persisted discovery/run-history data (the Codex). Saved as JSON under Saves/.</summary>
public class CodexData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, BestiaryEntry> Bestiary { get; set; } = new();
    public PlayStats PlayStats { get; set; } = new();
}

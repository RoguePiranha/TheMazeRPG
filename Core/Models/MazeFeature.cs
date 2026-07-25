namespace TheMazeRPG.Core.Models;

/// <summary>
/// Special features that can appear in the maze
/// </summary>
public enum MazeFeatureType
{
    Stairs,
    Chest,
    Shrine,        // safe-room rest point; touching it exits the dungeon
    GuardianDoor,  // safe-room gate; approaching it spawns that gate's Guardian
    Trap,          // one-shot environmental hazard; deals burst damage when stepped near

    // Overworld (Starting Region) features — reusable, not one-shot.
    DungeonEntrance, // touching this in the Overworld starts a fresh dungeon dive
    MineEntrance,    // start a MineOreActivity here
    Smithy,          // smelt/craft recipes, and (later) Forge-gated Combinable merging
    Stall            // sell items for gold
}

public class MazeFeature
{
    public int X { get; set; }
    public int Y { get; set; }
    public MazeFeatureType Type { get; set; }
    public bool IsUsed { get; set; }
    
    // Chest opening animation state
    public bool IsOpening { get; set; }
    public int OpeningTicks { get; set; }
    public float LightRadius { get; set; }
    public float OpenProgress { get; set; } // 0..1, set by GameState from the tick-rate-derived duration
}

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

    // Perception: a hidden feature (traps) is nearly invisible until the hero notices it (a
    // Wisdom-based spot roll) or Examines it, at which point Perceived flips true. See
    // PerceptionService / GameState.UpdatePerception. Non-hidden features render/behave normally.
    public bool Hidden { get; set; }
    public bool Perceived { get; set; }

    // Chest opening animation state
    public bool IsOpening { get; set; }
    public int OpeningTicks { get; set; }
    public float LightRadius { get; set; }
    public float OpenProgress { get; set; } // 0..1, set by GameState from the tick-rate-derived duration
}

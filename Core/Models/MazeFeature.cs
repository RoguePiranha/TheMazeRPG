using System.Collections.Generic;

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
    Stall,           // sell items for gold
    Lamp             // street lamp: scenery by day, a pool of light in the night layer
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

    // Chest state. Dungeon maps are transient, so this is not part of character saves.
    public bool IsOpened { get; set; }
    public bool IsLocked { get; set; }
    public string RequiredKeyId { get; set; } = "";
    public bool TrapChecked { get; set; }
    public bool IsTrapped { get; set; }
    public bool ChestTrapDetected { get; set; }
    public bool TrapDisarmed { get; set; }
    public bool OpenRewardGranted { get; set; }
    public List<Combinable> Inventory { get; set; } = new();
    public int Gold { get; set; }
}

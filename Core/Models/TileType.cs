namespace TheMazeRPG.Core.Models;

/// <summary>
/// What occupies a map cell. This is the authoritative terrain layer — walkability and sight
/// blocking are both derived from it (see Maze.IsWalkable / Maze.BlocksSight), so there is never a
/// second grid to keep in sync.
///
/// Distinct from DungeonTileType, which records *why* a generated cell exists (room floor vs.
/// corridor vs. doorway) for placement and rendering decisions. This enum records what the cell
/// physically is right now, including state that changes during play — a door being opened.
/// </summary>
public enum TileType
{
    Wall,
    Floor,

    /// <summary>Shut but openable: blocks movement and sight until something opens it. Bumping into
    /// one opens it (no extra input), which is why closed doors are not simply walls.</summary>
    DoorClosed,

    /// <summary>Standing open: walkable, and sight passes through.</summary>
    DoorOpen,

    /// <summary>Shut and needs the matching key. Blocks movement and sight; bumping does not open
    /// it.</summary>
    DoorLocked

    // Room reserved for terrain that isn't a binary obstacle — Water (slows), Rubble (blocks sight
    // but not movement) — which the open-terrain floors (Planning note 04) will want.
}

public static class TileTypes
{
    /// <summary>Can an actor stand here right now? A shut door can't be occupied until it's opened.</summary>
    public static bool IsPassable(this TileType tile) =>
        tile is TileType.Floor or TileType.DoorOpen;

    /// <summary>
    /// Can an actor get through here eventually, by opening what's in the way? Pathfinding and
    /// connectivity ask this rather than IsPassable — otherwise every closed door would read as a
    /// wall and cut its room off the map, so rooms would be unreachable, stairs unplaceable, and
    /// generation validation would fail on perfectly good floors. Locked doors stay excluded: they
    /// genuinely need their key, which is what makes a vault a vault.
    /// </summary>
    public static bool IsTraversable(this TileType tile) =>
        tile is TileType.Floor or TileType.DoorOpen or TileType.DoorClosed;

    /// <summary>Does this stop line of sight? Closed and locked doors do; an open one does not.</summary>
    public static bool BlocksSight(this TileType tile) =>
        tile is TileType.Wall or TileType.DoorClosed or TileType.DoorLocked;

    public static bool IsDoor(this TileType tile) =>
        tile is TileType.DoorClosed or TileType.DoorOpen or TileType.DoorLocked;

    /// <summary>A door that bumping into should open. Locked ones need a key instead.</summary>
    public static bool IsBumpOpenable(this TileType tile) => tile == TileType.DoorClosed;
}

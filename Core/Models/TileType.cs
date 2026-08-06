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
    DoorLocked,

    // --- Outdoor terrain (regions) ---

    /// <summary>Made road or paved street. Walkable; distinct from open ground so the generator can
    /// lay building lots along frontage and the renderer can show a street.</summary>
    Road,

    /// <summary>Deep water — a river. Impassable on foot and crossed at a bridge, but you can see
    /// straight across it, which is what makes a river read as open ground rather than a wall.
    /// </summary>
    Water,

    /// <summary>Standing tree. Blocks movement and sight, like a wall, but is terrain rather than
    /// masonry so forests can be told apart from buildings for rendering and later felling.</summary>
    Tree,

    // --- Rock (owner ruling 2026-08-06: mining means destroying stone, not pressing a button) ---

    /// <summary>Natural rock — a mountainside or cave face. Blocks like a wall, but can be mined
    /// out, yielding rubble and occasionally a little ore.</summary>
    Stone,

    /// <summary>Rock carrying a seam of ore. Mines out like stone but pays properly, and is
    /// visible in an exposed rock face so prospecting is something you do with your eyes.</summary>
    OreVein,

    /// <summary>
    /// Rock that cannot be mined, at any hardness, ever. The map's rim and the dungeon's threshold
    /// are made of it — the first so nobody can tunnel out of the world, the second because the
    /// dungeon's entrance is not ordinary stone (see the dimensional-space ruling in
    /// Implementation Plan 2026-08-06).
    /// </summary>
    Bedrock

    // Room reserved for the rest of note 04's open-terrain palette — shallow water that slows
    // rather than stops, mud, rubble that blocks sight but not movement.
}

public static class TileTypes
{
    /// <summary>Can an actor stand here right now? A shut door can't be occupied until it's opened.</summary>
    public static bool IsPassable(this TileType tile) =>
        tile is TileType.Floor or TileType.DoorOpen or TileType.Road;

    /// <summary>
    /// Can an actor get through here eventually, by opening what's in the way? Pathfinding and
    /// connectivity ask this rather than IsPassable — otherwise every closed door would read as a
    /// wall and cut its room off the map, so rooms would be unreachable, stairs unplaceable, and
    /// generation validation would fail on perfectly good floors. Locked doors stay excluded: they
    /// genuinely need their key, which is what makes a vault a vault.
    /// </summary>
    public static bool IsTraversable(this TileType tile) =>
        tile is TileType.Floor or TileType.DoorOpen or TileType.DoorClosed or TileType.Road;

    /// <summary>Does this stop line of sight? Closed and locked doors do; an open one does not.
    /// Water does not — you can see across a river, which is what makes it read as open country
    /// rather than a wall you happen to be able to look over.</summary>
    public static bool BlocksSight(this TileType tile) =>
        tile is TileType.Wall or TileType.DoorClosed or TileType.DoorLocked or TileType.Tree
            or TileType.Stone or TileType.OreVein or TileType.Bedrock;

    /// <summary>Natural rock, as opposed to masonry someone built. Ore only occurs in rock.</summary>
    public static bool IsNaturalRock(this TileType tile) =>
        tile is TileType.Stone or TileType.OreVein;

    public static bool IsDoor(this TileType tile) =>
        tile is TileType.DoorClosed or TileType.DoorOpen or TileType.DoorLocked;

    /// <summary>A door that bumping into should open. Locked ones need a key instead.</summary>
    public static bool IsBumpOpenable(this TileType tile) => tile == TileType.DoorClosed;
}

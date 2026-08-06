using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents a single maze floor
/// </summary>
public class Maze
{
    public int Width { get; set; } = 41;
    public int Height { get; set; } = 31;
    public int FloorNumber { get; set; } = 1;

    /// <summary>
    /// The authoritative terrain grid. Walkability and sight blocking both derive from it, so there
    /// is no second grid that can drift out of sync.
    /// </summary>
    public TileType[,] Tiles { get; set; }

    /// <summary>
    /// Legacy view of <see cref="Tiles"/> as a blocking bitmap: <c>true</c> wherever an actor can't
    /// walk. Kept because collision, LOS, projectiles, pathing, and both renderers all speak this
    /// language already; it reads and writes through to Tiles rather than duplicating state.
    /// </summary>
    public BlockingGrid Walls => new(this);

    // Track explored cells
    public bool[,] Explored { get; set; }
    
    // Special features in the maze
    public List<MazeFeature> Features { get; set; } = new();

    // Present only on procedurally generated dungeon floors. The Overworld and safe rooms keep
    // using the shared wall grid without inheriting dungeon-specific semantics.
    public DungeonLayout? Dungeon { get; set; }

    // Present only on generated regions (the town and its surroundings). Walls, gates, roads,
    // buildings and the river are a different vocabulary from rooms and corridors, so regions get
    // their own structural record rather than overloading DungeonLayout.
    public RegionLayout? Region { get; set; }
    
    public Maze(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new TileType[width, height];
        // A fresh grid used to be bool[,] defaulting to false — i.e. all *open*. TileType.Wall is 0,
        // so filling Floor keeps that default identical rather than silently inverting it. Every
        // real generator overwrites every cell anyway, but the default shouldn't flip under them.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Tiles[x, y] = TileType.Floor;
        Explored = new bool[width, height];
    }
    
    /// <summary>
    /// Get all empty (non-wall) cells
    /// </summary>
    public List<(int x, int y)> GetEmptyCells()
    {
        var cells = new List<(int x, int y)>();
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (!Walls[x, y])
                {
                    cells.Add((x, y));
                }
            }
        }
        return cells;
    }
    
    /// <summary>Every cell an actor can route through, doors included. See IsTraversable.</summary>
    public List<(int x, int y)> GetTraversableCells()
    {
        var cells = new List<(int x, int y)>();
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (Tiles[x, y].IsTraversable())
                    cells.Add((x, y));
        return cells;
    }

    /// <summary>
    /// Check if a position is walkable
    /// </summary>
    public bool IsWalkable(int x, int y) =>
        InBounds(x, y) && Tiles[x, y].IsPassable();

    /// <summary>
    /// Can an actor route through here, opening doors on the way? Pathfinding and connectivity use
    /// this; physical occupancy uses <see cref="IsWalkable"/>. See TileTypes.IsTraversable for why
    /// the two must differ.
    /// </summary>
    public bool IsTraversable(int x, int y) =>
        InBounds(x, y) && Tiles[x, y].IsTraversable();

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Does this cell stop line of sight? Out of bounds counts as blocking.</summary>
    public bool BlocksSight(int x, int y) =>
        !InBounds(x, y) || Tiles[x, y].BlocksSight();

    public TileType TileAt(int x, int y) => InBounds(x, y) ? Tiles[x, y] : TileType.Wall;

    public bool IsDoor(int x, int y) => InBounds(x, y) && Tiles[x, y].IsDoor();

    /// <summary>
    /// Open a closed door by walking into it (owner ruling 2026-08-05: bump-to-open, no extra
    /// input). Returns true only when a door actually changed state, so callers can react — a
    /// locked door needs its key and reports false.
    /// </summary>
    public bool TryOpenDoor(int x, int y)
    {
        if (!InBounds(x, y) || !Tiles[x, y].IsBumpOpenable()) return false;
        Tiles[x, y] = TileType.DoorOpen;
        return true;
    }

    /// <summary>Unlock and open a locked door — the key-in-hand path.</summary>
    public bool TryUnlockDoor(int x, int y)
    {
        if (!InBounds(x, y) || Tiles[x, y] != TileType.DoorLocked) return false;
        Tiles[x, y] = TileType.DoorOpen;
        return true;
    }

    /// <summary>
    /// BFS distance in steps (via the actual traversable maze graph, not straight-line) from a start
    /// cell to every reachable cell. Cells not reachable from the start are absent from the map.
    /// Used to place the exit genuinely far from the entrance (real maze-solving distance).
    ///
    /// Traversable, not walkable: a closed door is something you open and walk through, so it must
    /// not sever the graph. Locked doors do sever it, which is exactly how a vault stays sealed
    /// until its key turns up.
    /// </summary>
    public Dictionary<(int x, int y), int> BfsDistancesFrom(int startX, int startY)
    {
        var distances = new Dictionary<(int x, int y), int> { [(startX, startY)] = 0 };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int d = distances[(x, y)];
            foreach (var (dx, dy) in directions)
            {
                var next = (x + dx, y + dy);
                if (IsTraversable(next.Item1, next.Item2) && !distances.ContainsKey(next))
                {
                    distances[next] = d + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return distances;
    }
}

/// <summary>
/// Presents <see cref="Maze.Tiles"/> as the blocking bitmap the rest of the codebase already speaks:
/// <c>true</c> means "an actor can't be here", which is what collision, LOS, projectile impacts, and
/// both renderers were all written against. It holds no state of its own — every read and write goes
/// straight through to Tiles, so the tile grid stays the single source of truth.
/// </summary>
public readonly struct BlockingGrid
{
    private readonly Maze _maze;

    public BlockingGrid(Maze maze) => _maze = maze;

    /// <summary>
    /// Any impassable tile reads as <c>true</c>, so a closed or locked door blocks movement and
    /// sight everywhere the old bitmap did, with no call-site changes.
    ///
    /// Read-only by design: writing <c>false</c> would have to pick some passable tile and would
    /// silently flatten a door into bare floor. Terrain writes go to <see cref="Maze.Tiles"/>
    /// directly, where the author has to say which tile they mean.
    /// </summary>
    public bool this[int x, int y] => !_maze.Tiles[x, y].IsPassable();
}

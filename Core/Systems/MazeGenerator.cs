using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Generates connected room-and-corridor dungeon floors. The result still exposes Maze.Walls so
/// movement and combat remain grid-compatible, while Maze.Dungeon records structural meaning.
/// </summary>
public sealed class MazeGenerator
{
    // Tight maps (15x11, 21x15) reject most attempts — packing three walled rooms plus clean
    // corridors into so few cells often needs several rolls. Attempts are sub-millisecond, so a
    // deep retry budget is cheaper than shipping either a crash or a malformed floor.
    private const int MaxGenerationAttempts = 60;
    private readonly int _seed;

    public MazeGenerator(int seed)
    {
        _seed = seed;
    }

    public Maze Generate(int width, int height, int floorNumber)
    {
        if (width < 15 || height < 11)
            throw new ArgumentOutOfRangeException(nameof(width), "Dungeon dimensions must be at least 15x11.");

        IReadOnlyList<string> lastErrors = Array.Empty<string>();
        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var random = new Random(DeriveSeed(width, height, floorNumber, attempt));
            var maze = GenerateCandidate(width, height, floorNumber, random);
            lastErrors = DungeonGenerationValidator.Validate(maze);
            if (lastErrors.Count == 0)
                return maze;
        }

        throw new InvalidOperationException(
            $"Unable to generate a valid {width}x{height} dungeon after {MaxGenerationAttempts} attempts: " +
            string.Join("; ", lastErrors));
    }

    private Maze GenerateCandidate(int width, int height, int floorNumber, Random random)
    {
        var maze = new Maze(width, height) { FloorNumber = floorNumber };
        var layout = new DungeonLayout(width, height);
        layout.Theme = ThemeForFloor(floorNumber);
        maze.Dungeon = layout;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                maze.Tiles[x, y] = TileType.Wall;
                layout.Tiles[x, y] = DungeonTileType.Wall;
            }
        }

        PlaceRooms(maze, layout, random);
        ConnectRooms(maze, layout, random);
        SealRoomBreaches(maze, layout);
        CleanDeadDoors(maze, layout);
        AssignRoomRoles(maze, layout, random);
        AssignRoomArchetypes(layout, random);
        // After roles: which thresholds deserve a door depends on what the room is for.
        PlaceDoors(maze, layout, random);
        PlaceDecorations(layout, random);
        PlaceThemeFeature(layout);

        // The dungeon's walls are masonry and can be broken (loudly — see GameState's mining
        // noise), but its outer shell is bedrock. The dungeon is a dimensional space, not a cavity
        // in the mountain, so there is nothing on the far side of that shell to dig into.
        for (int x = 0; x < width; x++)
        {
            maze.Tiles[x, 0] = TileType.Bedrock;
            maze.Tiles[x, height - 1] = TileType.Bedrock;
        }
        for (int y = 0; y < height; y++)
        {
            maze.Tiles[0, y] = TileType.Bedrock;
            maze.Tiles[width - 1, y] = TileType.Bedrock;
        }

        return maze;
    }

    private static void PlaceRooms(Maze maze, DungeonLayout layout, Random random)
    {
        bool compactMap = maze.Width * maze.Height < 300;
        int entranceWidth = compactMap ? 4 : 5;
        int entranceHeight = compactMap ? 4 : 5;
        AddRoom(maze, layout, new DungeonRoom
        {
            Id = 0,
            X = 1,
            Y = 1,
            Width = entranceWidth,
            Height = entranceHeight,
            Role = DungeonRoomRole.Entrance
        });

        int baseRoomCount = Math.Clamp(maze.Width * maze.Height / 105, 3, 12);
        int targetRooms = Math.Clamp(baseRoomCount + random.Next(-1, 2), 3, 12);
        int minimumRoomSize = compactMap ? 3 : 4;
        int maxRoomWidth = Math.Min(9, maze.Width - 4);
        int maxRoomHeight = Math.Min(7, maze.Height - 4);
        int placementAttempts = targetRooms * 100;

        while (layout.Rooms.Count < targetRooms && placementAttempts-- > 0)
        {
            int roomWidth = random.Next(minimumRoomSize, maxRoomWidth + 1);
            int roomHeight = random.Next(minimumRoomSize, maxRoomHeight + 1);
            int x = random.Next(1, maze.Width - roomWidth);
            int y = random.Next(1, maze.Height - roomHeight);

            var candidate = new DungeonRoom
            {
                Id = layout.Rooms.Count,
                X = x,
                Y = y,
                Width = roomWidth,
                Height = roomHeight
            };

            if (layout.Rooms.Any(existing => RoomsOverlapWithMargin(existing, candidate, 1)))
                continue;

            AddRoom(maze, layout, candidate);
        }
    }

    private static bool RoomsOverlapWithMargin(DungeonRoom first, DungeonRoom second, int margin) =>
        first.X - margin <= second.Right && first.Right + margin >= second.X &&
        first.Y - margin <= second.Bottom && first.Bottom + margin >= second.Y;

    private static void AddRoom(Maze maze, DungeonLayout layout, DungeonRoom room)
    {
        layout.Rooms.Add(room);
        for (int x = room.X; x <= room.Right; x++)
        {
            for (int y = room.Y; y <= room.Bottom; y++)
            {
                maze.Tiles[x, y] = TileType.Floor;
                layout.Tiles[x, y] = DungeonTileType.RoomFloor;
                layout.RegionIds[x, y] = room.Id;
            }
        }
    }

    private static void ConnectRooms(Maze maze, DungeonLayout layout, Random random)
    {
        var connected = new HashSet<int> { 0 };
        var edges = new HashSet<(int first, int second)>();

        // Prim-style minimum spanning tree: every room is reachable without long arbitrary links.
        while (connected.Count < layout.Rooms.Count)
        {
            DungeonRoom? from = null;
            DungeonRoom? to = null;
            int bestScore = int.MaxValue;

            foreach (int connectedId in connected)
            {
                foreach (var candidate in layout.Rooms.Where(room => !connected.Contains(room.Id)))
                {
                    var source = layout.Rooms[connectedId];
                    int distance = ManhattanDistance(source, candidate);
                    int score = distance * 16 + random.Next(16);
                    if (score >= bestScore) continue;

                    bestScore = score;
                    from = source;
                    to = candidate;
                }
            }

            if (from == null || to == null)
                throw new InvalidOperationException("Could not connect all generated rooms.");

            // Mark the room visited either way so the loop always terminates — but only record
            // the edge when a corridor was actually carved. Recording a failed carve used to
            // satisfy the validator's connection count with a phantom edge; now an unlinked
            // room shows up as an incomplete graph (and disconnected tiles), failing the
            // attempt so the floor regenerates instead of shipping.
            bool carved = CarveBestCorridor(maze, layout, from, to, random, preferNewGround: false);
            if (carved) AddConnection(layout, edges, from.Id, to.Id, isLoop: false);
            connected.Add(to.Id);
        }

        // Add a small number of non-tree edges. These create alternate routes while preserving
        // enough dead ends for exploration and encounter placement.
        int desiredLoops = Math.Max(1, layout.Rooms.Count / 5 + random.Next(2));
        var candidates = new List<(DungeonRoom first, DungeonRoom second, int score)>();
        for (int first = 0; first < layout.Rooms.Count; first++)
        {
            for (int second = first + 1; second < layout.Rooms.Count; second++)
            {
                if (edges.Contains((first, second))) continue;
                candidates.Add((layout.Rooms[first], layout.Rooms[second],
                    ManhattanDistance(layout.Rooms[first], layout.Rooms[second]) * 16 + random.Next(16)));
            }
        }

        foreach (var candidate in candidates.OrderBy(item => item.score))
        {
            if (desiredLoops == 0) break;
            if (!CarveBestCorridor(maze, layout, candidate.first, candidate.second, random, preferNewGround: true))
                continue;

            AddConnection(layout, edges, candidate.first.Id, candidate.second.Id, isLoop: true);
            desiredLoops--;
        }
    }

    private static int ManhattanDistance(DungeonRoom first, DungeonRoom second) =>
        Math.Abs(first.CenterX - second.CenterX) + Math.Abs(first.CenterY - second.CenterY);

    private static void AddConnection(
        DungeonLayout layout,
        HashSet<(int first, int second)> edges,
        int first,
        int second,
        bool isLoop)
    {
        var edge = first < second ? (first, second) : (second, first);
        edges.Add(edge);
        layout.Connections.Add(new DungeonConnection
        {
            FromRoomId = first,
            ToRoomId = second,
            IsLoop = isLoop
        });
    }

    /// <summary>
    /// Link two rooms by a corridor that runs between their doorways rather than their centres.
    ///
    /// Routing centre-to-centre drove corridors straight through room interiors and let them spill
    /// out anywhere along an edge, so a room could meet a corridor along several tiles at once —
    /// which reads as a room with one wall missing, not a room with a door. Each room instead gets a
    /// single threshold tile punched through its wall ring on the side facing its neighbour, and the
    /// corridor joins those two thresholds from outside. Rooms keep their walls; entrances are one
    /// tile wide.
    ///
    /// The route between the two thresholds is cost-based rather than a blind elbow. The old
    /// two-elbow carve ran corridors flush along room walls constantly, and the breach-sealing
    /// pass then walled up the tile outside many legitimate doors — most floors shipped with at
    /// least one door that opened onto solid stone. Weighting room-adjacent tiles out of the route
    /// keeps corridors off the masonry in the first place, so sealing (and the artifacts it left)
    /// becomes the rare case instead of the norm.
    /// </summary>
    private static bool CarveBestCorridor(
        Maze maze,
        DungeonLayout layout,
        DungeonRoom from,
        DungeonRoom to,
        Random random,
        bool preferNewGround)
    {
        var fromPort = ChoosePort(from, to, layout, random);
        var toPort = ChoosePort(to, from, layout, random);
        if (fromPort == null || toPort == null)
            return false;

        var (fromDoor, fromOutside) = fromPort.Value;
        var (toDoor, toOutside) = toPort.Value;

        // The jambs beside each threshold stay solid: a corridor sliding through one would leave
        // the new door flapping at the end of a wall stub.
        var blocked = new HashSet<(int x, int y)>();
        foreach (var (door, outside) in new[] { (fromDoor, fromOutside), (toDoor, toOutside) })
        {
            bool horizontal = outside.x != door.x;
            blocked.Add(horizontal ? (door.x, door.y - 1) : (door.x - 1, door.y));
            blocked.Add(horizontal ? (door.x, door.y + 1) : (door.x + 1, door.y));
        }
        blocked.Remove(fromOutside);
        blocked.Remove(toOutside);

        var path = RouteCorridor(layout, fromOutside, toOutside, blocked);
        if (path == null)
            return false;

        // A loop edge only earns its place if it opens genuinely new ground. Compact maps accept a
        // single fresh cell — one new link between two existing corridors is still a real
        // alternate route, and tiny floors rarely have room for more.
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        int freshGroundNeeded = width * height < 300 ? 1 : 2;
        if (preferNewGround &&
            path.Count(cell => layout.Tiles[cell.x, cell.y] == DungeonTileType.Wall) < freshGroundNeeded)
            return false;

        foreach (var (x, y) in path)
        {
            if (layout.Tiles[x, y] != DungeonTileType.Wall) continue; // never overwrite room floor
            maze.Tiles[x, y] = TileType.Floor;
            layout.Tiles[x, y] = DungeonTileType.CorridorFloor;
        }

        CarveThreshold(maze, layout, fromDoor);
        CarveThreshold(maze, layout, toDoor);
        return true;
    }

    // Route costs. Base movement is 10 so the modifiers below can stay integers.
    private const int StepCost = 10;
    private const int RingCost = 200;      // cell flush against a room's floor: a wall breach if carved
    private const int PinchCost = 30;      // cell touching a room only diagonally: rounds its corner tight
    private const int DoorGrazeCost = 80;  // cell in front of someone else's doorway: corridors shouldn't crowd doors
    private const int ReuseDiscount = 6;   // existing corridor: joining the network beats parallel trenches
    private const int TurnCost = 3;        // straight halls with clean elbows over staircase wiggles

    /// <summary>
    /// Cheapest corridor between two port cells: Dijkstra over the map interior, never through room
    /// floor or an existing threshold, with costs that keep corridors clear of room walls (see
    /// constants above). The state includes the incoming direction so turning costs something —
    /// otherwise equal-cost staircase paths beat straight halls.
    /// </summary>
    private static List<(int x, int y)>? RouteCorridor(
        DungeonLayout layout,
        (int x, int y) start,
        (int x, int y) goal,
        HashSet<(int x, int y)> blocked)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        var directions = new (int dx, int dy)[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        // Jambs of doors that already exist are as untouchable as the current pair's: carving
        // through one leaves that earlier door flapping at the end of a wall stub.
        bool IsExistingJamb(int x, int y)
        {
            foreach (var (dx, dy) in new (int, int)[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                if (layout.Tiles[nx, ny] != DungeonTileType.Doorway) continue;
                bool doorRunsEastWest =
                    layout.DoorwayOrientationAt(nx, ny) == DungeonPassageOrientation.EastWest;
                // A jamb sits perpendicular to the door's passage; a cell in line with the
                // passage is the door's own approach and stays routable.
                if (doorRunsEastWest ? dy != 0 : dx != 0) return true;
            }
            return false;
        }

        bool Routable(int x, int y) =>
            x >= 1 && y >= 1 && x <= width - 2 && y <= height - 2 &&
            layout.Tiles[x, y] != DungeonTileType.RoomFloor &&
            layout.Tiles[x, y] != DungeonTileType.Doorway &&
            !blocked.Contains((x, y)) &&
            ((x, y) == start || (x, y) == goal || !IsExistingJamb(x, y));

        int EnterCost(int x, int y)
        {
            int cost = StepCost;
            if (IsBesideRoomFloor(layout, x, y)) cost += RingCost;
            else if (IsDiagonalToRoomFloor(layout, x, y)) cost += PinchCost;
            if ((x, y) != start && (x, y) != goal && IsBesideDoorway(layout, x, y)) cost += DoorGrazeCost;
            if (layout.Tiles[x, y] == DungeonTileType.CorridorFloor) cost -= ReuseDiscount;
            return cost;
        }

        var best = new Dictionary<(int x, int y, int dir), int>();
        var parents = new Dictionary<(int x, int y, int dir), (int x, int y, int dir)>();
        var queue = new PriorityQueue<(int x, int y, int dir), int>();
        for (int dir = 0; dir < 4; dir++)
        {
            best[(start.x, start.y, dir)] = 0;
            queue.Enqueue((start.x, start.y, dir), 0);
        }

        while (queue.TryDequeue(out var state, out int cost))
        {
            if (cost > best.GetValueOrDefault(state, int.MaxValue)) continue;
            if ((state.x, state.y) == goal)
                return ReconstructRoute(parents, state, start);

            for (int dir = 0; dir < 4; dir++)
            {
                int nx = state.x + directions[dir].dx;
                int ny = state.y + directions[dir].dy;
                if (!Routable(nx, ny)) continue;

                int next = cost + EnterCost(nx, ny) + (dir == state.dir ? 0 : TurnCost);
                var key = (nx, ny, dir);
                if (next >= best.GetValueOrDefault(key, int.MaxValue)) continue;
                best[key] = next;
                parents[key] = state;
                queue.Enqueue(key, next);
            }
        }

        return null; // walled off (rooms partition the interior) — caller drops the edge
    }

    private static List<(int x, int y)> ReconstructRoute(
        Dictionary<(int x, int y, int dir), (int x, int y, int dir)> parents,
        (int x, int y, int dir) end,
        (int x, int y) start)
    {
        var path = new List<(int x, int y)> { (end.x, end.y) };
        var cursor = end;
        while ((cursor.x, cursor.y) != start && parents.TryGetValue(cursor, out var parent))
        {
            cursor = parent;
            if (path[^1] != (cursor.x, cursor.y))
                path.Add((cursor.x, cursor.y));
        }
        path.Reverse();
        return path;
    }

    private static bool IsBesideRoomFloor(DungeonLayout layout, int x, int y) =>
        CountRoomFloorNeighbours(layout, x, y) > 0;

    private static bool IsDiagonalToRoomFloor(DungeonLayout layout, int x, int y)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            int nx = x + dx;
            int ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (layout.Tiles[nx, ny] == DungeonTileType.RoomFloor) return true;
        }
        return false;
    }

    private static bool IsBesideDoorway(DungeonLayout layout, int x, int y)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
        {
            int nx = x + dx;
            int ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (layout.Tiles[nx, ny] == DungeonTileType.Doorway) return true;
        }
        return false;
    }

    /// <summary>Punch a one-tile entrance through a room's wall ring and mark it as a doorway.</summary>
    private static void CarveThreshold(Maze maze, DungeonLayout layout, (int x, int y) cell)
    {
        maze.Tiles[cell.x, cell.y] = TileType.Floor;
        layout.Tiles[cell.x, cell.y] = DungeonTileType.Doorway;
    }

    /// <summary>
    /// Corridor carving may run flush along a room's wall ring, and any carved ring cell that
    /// touches room floor is an unmarked, doorless breach — the vault's guaranteed door means
    /// nothing when an open gap sits beside it. Seal each breach back to wall when the map stays
    /// connected without it (traffic then flows through the room's real doorways); where sealing
    /// would sever the graph, keep the opening but mark it a Doorway so it's a legitimate,
    /// doorable entrance instead of a hole.
    /// </summary>
    private static void SealRoomBreaches(Maze maze, DungeonLayout layout)
    {
        if (layout.Rooms.Count == 0) return;
        var anchor = (x: layout.Rooms[0].CenterX, y: layout.Rooms[0].CenterY);

        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                if (layout.Tiles[x, y] != DungeonTileType.CorridorFloor) continue;
                int roomNeighbours = CountRoomFloorNeighbours(layout, x, y);
                if (roomNeighbours == 0) continue;

                maze.Tiles[x, y] = TileType.Wall;
                layout.Tiles[x, y] = DungeonTileType.Wall;
                int traversable = maze.GetTraversableCells().Count;
                var reachable = maze.BfsDistancesFrom(anchor.x, anchor.y);
                if (reachable.Count != traversable)
                {
                    // The cell was the only link between two areas. Bordering exactly one room
                    // tile, it's a legitimate one-tile threshold — promote it. Bordering more
                    // (wedged between two rooms), a Doorway would break the one-tile-threshold
                    // invariant, so leave it as the open corridor it was — no worse than before
                    // this pass existed, and rare.
                    maze.Tiles[x, y] = TileType.Floor;
                    layout.Tiles[x, y] = roomNeighbours == 1
                        ? DungeonTileType.Doorway
                        : DungeonTileType.CorridorFloor;
                }
            }
        }
    }

    private static int CountRoomFloorNeighbours(DungeonLayout layout, int x, int y)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        int count = 0;
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
        {
            int nx = x + dx;
            int ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (layout.Tiles[nx, ny] == DungeonTileType.RoomFloor) count++;
        }
        return count;
    }

    /// <summary>
    /// A doorway is only a doorway while something is on the other side. Breach sealing (above) can
    /// wall up the corridor a threshold opened onto, leaving a door that opens onto solid stone —
    /// the single most common artifact of the old carve, and what made rooms read as sealed-off.
    /// Such a threshold contributes nothing to connectivity (it leads nowhere), so it reverts to
    /// wall. Sealing one can orphan a neighbour in turn, so the pass runs to a fixpoint.
    /// </summary>
    private static void CleanDeadDoors(Maze maze, DungeonLayout layout)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int x = 1; x < width - 1; x++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    if (layout.Tiles[x, y] != DungeonTileType.Doorway) continue;

                    bool hasOutlet = false;
                    foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
                    {
                        var neighbour = layout.Tiles[x + dx, y + dy];
                        if (neighbour != DungeonTileType.Wall && neighbour != DungeonTileType.RoomFloor)
                        {
                            hasOutlet = true;
                            break;
                        }
                    }

                    if (hasOutlet) continue;
                    maze.Tiles[x, y] = TileType.Wall;
                    layout.Tiles[x, y] = DungeonTileType.Wall;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// Pick where <paramref name="room"/> opens toward <paramref name="toward"/>: the threshold tile
    /// in its wall ring, plus the corridor tile just beyond it. Every position along every side is a
    /// candidate, scored by how directly it faces the neighbour and how well it aligns with it — but
    /// a position only qualifies if it makes a *clean* door: jambs (the two wall cells beside the
    /// threshold) still solid, no second doorway beside it, and open ground beyond that isn't flush
    /// against someone's room floor. An existing doorway on a suitable side is reused outright, so
    /// rooms collect a few well-made entrances instead of a riddling of one-off holes.
    /// </summary>
    private static ((int x, int y) door, (int x, int y) outside)? ChoosePort(
        DungeonRoom room,
        DungeonRoom toward,
        DungeonLayout layout,
        Random random)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);

        int dx = toward.CenterX - room.CenterX;
        int dy = toward.CenterY - room.CenterY;

        // Facing side first, its perpendicular next, the far side last.
        var sides = new List<char>();
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            sides.Add(dx >= 0 ? 'E' : 'W');
            sides.Add(dy >= 0 ? 'S' : 'N');
        }
        else
        {
            sides.Add(dy >= 0 ? 'S' : 'N');
            sides.Add(dx >= 0 ? 'E' : 'W');
        }
        foreach (char side in new[] { 'E', 'W', 'S', 'N' })
            if (!sides.Contains(side)) sides.Add(side);

        ((int x, int y) door, (int x, int y) outside)? bestPort = null;
        int bestScore = int.MaxValue;

        for (int sideIndex = 0; sideIndex < sides.Count; sideIndex++)
        {
            char side = sides[sideIndex];
            bool horizontal = side is 'E' or 'W';
            int ideal = horizontal
                ? Math.Clamp(toward.CenterY, room.Y, room.Bottom)
                : Math.Clamp(toward.CenterX, room.X, room.Right);
            int from = horizontal ? room.Y : room.X;
            int to = horizontal ? room.Bottom : room.Right;

            for (int along = from; along <= to; along++)
            {
                (int x, int y) door = side switch
                {
                    'E' => (room.Right + 1, along),
                    'W' => (room.X - 1, along),
                    'S' => (along, room.Bottom + 1),
                    _ => (along, room.Y - 1)
                };
                (int x, int y) outside = side switch
                {
                    'E' => (door.x + 1, door.y),
                    'W' => (door.x - 1, door.y),
                    'S' => (door.x, door.y + 1),
                    _ => (door.x, door.y - 1)
                };

                // Both the threshold and the tile beyond it must sit inside the boundary wall.
                if (door.x < 1 || door.y < 1 || door.x >= width - 1 || door.y >= height - 1) continue;
                if (outside.x < 1 || outside.y < 1 || outside.x >= width - 1 || outside.y >= height - 1) continue;

                bool reused = layout.Tiles[door.x, door.y] == DungeonTileType.Doorway;
                if (reused)
                {
                    // A door this room already has, still leading somewhere: route from it again.
                    if (layout.Tiles[outside.x, outside.y] == DungeonTileType.Wall) continue;
                    if (layout.Tiles[outside.x, outside.y] == DungeonTileType.RoomFloor) continue;
                }
                else
                {
                    // Only virgin wall becomes a new threshold. A ring cell some corridor already
                    // runs through is a breach, not a door site.
                    if (layout.Tiles[door.x, door.y] != DungeonTileType.Wall) continue;
                    // One room only — a cell shared between two rooms' rings would be a hole
                    // joining them into one space.
                    if (CountRoomFloorNeighbours(layout, door.x, door.y) != 1) continue;
                    // Jambs: the wall must continue on both sides of the threshold, or the door
                    // floats at the end of a stub and reads as a broken corner.
                    var jambs = horizontal
                        ? new[] { (door.x, door.y - 1), (door.x, door.y + 1) }
                        : new[] { (door.x - 1, door.y), (door.x + 1, door.y) };
                    if (jambs.Any(j => layout.Tiles[j.Item1, j.Item2] != DungeonTileType.Wall)) continue;
                    // Never two doorways side by side — that's a double-wide hole.
                    if (IsBesideDoorway(layout, door.x, door.y)) continue;
                    // The landing beyond must be usable corridor ground: not room floor, not flush
                    // against anyone's room floor, not crowding another door.
                    if (layout.Tiles[outside.x, outside.y] is DungeonTileType.RoomFloor or DungeonTileType.Doorway) continue;
                    if (CountRoomFloorNeighbours(layout, outside.x, outside.y) != 0) continue;
                    if (IsBesideDoorway(layout, outside.x, outside.y)) continue;
                }

                int score = sideIndex * 4 + Math.Abs(along - ideal) + (reused ? -8 : 0) + random.Next(2);
                if (score >= bestScore) continue;
                bestScore = score;
                bestPort = (door, outside);
            }
        }

        return bestPort;
    }

    /// <summary>Share of ordinary thresholds that get a door hung in them. Most openings in a ruin
    /// are just openings — a door everywhere reads as a corridor of airlocks, and makes every room
    /// entry a bump. Rooms worth shutting (below) ignore this and always get one.</summary>
    private const double OrdinaryDoorChance = 0.3;

    /// <summary>
    /// Hang doors in some of the thresholds. A shut door blocks sight, so a room with one keeps its
    /// contents hidden until you open it — worth having where a room is meant to hold something, and
    /// not worth having on every opening.
    ///
    /// Doors are left closed rather than open: walking into one opens it. Path search routes through
    /// closed doors (Maze.IsTraversable), so hanging a door never severs the room behind it.
    /// </summary>
    private static void PlaceDoors(Maze maze, DungeonLayout layout, Random random)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (layout.Tiles[x, y] != DungeonTileType.Doorway) continue;

                // A threshold belongs to whichever room it opens into.
                var room = AdjacentRoom(layout, x, y);
                bool alwaysDoored = room?.Role is DungeonRoomRole.Treasure or DungeonRoomRole.Exit;
                if (!alwaysDoored && random.NextDouble() >= OrdinaryDoorChance) continue;

                maze.Tiles[x, y] = TileType.DoorClosed;
            }
        }
    }

    /// <summary>The room a threshold tile opens into, if any.</summary>
    private static DungeonRoom? AdjacentRoom(DungeonLayout layout, int x, int y)
    {
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
        {
            var room = layout.RoomAt(x + dx, y + dy);
            if (room != null && layout.Tiles[x + dx, y + dy] == DungeonTileType.RoomFloor)
                return room;
        }
        return null;
    }

    private static void AssignRoomRoles(Maze maze, DungeonLayout layout, Random random)
    {
        var distances = maze.BfsDistancesFrom(layout.EntranceX, layout.EntranceY);
        var exitRoom = layout.Rooms
            .Where(room => room.Role != DungeonRoomRole.Entrance)
            .OrderByDescending(room => distances.GetValueOrDefault((room.CenterX, room.CenterY), -1))
            .First();

        exitRoom.Role = DungeonRoomRole.Exit;
        layout.ExitX = exitRoom.CenterX;
        layout.ExitY = exitRoom.CenterY;

        var specialRooms = layout.Rooms
            .Where(room => room.Role == DungeonRoomRole.Standard)
            .OrderBy(_ => random.Next())
            .ToList();

        if (specialRooms.Count > 0)
            specialRooms[0].Role = DungeonRoomRole.Treasure;
        if (specialRooms.Count > 1)
            specialRooms[1].Role = DungeonRoomRole.Hazard;
    }

    private static void AssignRoomArchetypes(DungeonLayout layout, Random random)
    {
        foreach (var room in layout.Rooms)
        {
            room.Archetype = room.Role switch
            {
                DungeonRoomRole.Entrance => DungeonRoomArchetype.EntranceHall,
                DungeonRoomRole.Exit => DungeonRoomArchetype.ExitChamber,
                DungeonRoomRole.Treasure => DungeonRoomArchetype.Vault,
                DungeonRoomRole.Hazard => DungeonRoomArchetype.TrapGallery,
                _ => DungeonRoomArchetype.StoreRoom
            };
        }

        var standardRooms = layout.Rooms
            .Where(room => room.Role == DungeonRoomRole.Standard)
            .OrderBy(_ => random.Next())
            .ToList();
        var requiredVariety = new[]
        {
            DungeonRoomArchetype.GuardPost,
            DungeonRoomArchetype.Barracks,
            DungeonRoomArchetype.Lair,
            DungeonRoomArchetype.AbandonedCamp,
            DungeonRoomArchetype.StoreRoom
        };
        var additionalOptions = AdditionalArchetypesFor(layout.Theme);

        for (int i = 0; i < standardRooms.Count; i++)
        {
            standardRooms[i].Archetype = i < requiredVariety.Length
                ? requiredVariety[i]
                : additionalOptions[random.Next(additionalOptions.Length)];
        }
    }

    private static void PlaceDecorations(DungeonLayout layout, Random random)
    {
        foreach (var room in layout.Rooms)
        {
            int desiredCount = Math.Clamp(room.Width * room.Height / 14, 1, 4);
            var available = new List<(int x, int y)>();
            for (int x = room.X; x <= room.Right; x++)
            {
                for (int y = room.Y; y <= room.Bottom; y++)
                {
                    if ((x == layout.EntranceX && y == layout.EntranceY) ||
                        (x == layout.ExitX && y == layout.ExitY) ||
                        (x == room.CenterX && y == room.CenterY))
                    {
                        continue;
                    }
                    available.Add((x, y));
                }
            }

            var archetypeDecorations = DecorationsFor(room.Archetype);
            var themeDecorations = DecorationsFor(layout.Theme);
            foreach (var (x, y) in available.OrderBy(_ => random.Next()).Take(desiredCount))
            {
                var decorationTypes = random.NextDouble() < 0.65
                    ? archetypeDecorations
                    : themeDecorations;
                layout.Decorations.Add(new DungeonDecoration
                {
                    X = x,
                    Y = y,
                    RoomId = room.Id,
                    Type = decorationTypes[random.Next(decorationTypes.Length)],
                    Variant = random.Next(4)
                });
            }
        }
    }

    private static DungeonDecorationType[] DecorationsFor(DungeonRoomArchetype archetype) => archetype switch
    {
        DungeonRoomArchetype.EntranceHall =>
            new[] { DungeonDecorationType.Banner, DungeonDecorationType.Brazier, DungeonDecorationType.Rubble },
        DungeonRoomArchetype.GuardPost =>
            new[] { DungeonDecorationType.WeaponRack, DungeonDecorationType.Brazier, DungeonDecorationType.Crate },
        DungeonRoomArchetype.Barracks =>
            new[] { DungeonDecorationType.Bedroll, DungeonDecorationType.WeaponRack, DungeonDecorationType.Crate },
        DungeonRoomArchetype.Lair =>
            new[] { DungeonDecorationType.Bones, DungeonDecorationType.Mushrooms, DungeonDecorationType.Rubble },
        DungeonRoomArchetype.Vault =>
            new[] { DungeonDecorationType.Crate, DungeonDecorationType.Barrel, DungeonDecorationType.Rune },
        DungeonRoomArchetype.TrapGallery =>
            new[] { DungeonDecorationType.Rune, DungeonDecorationType.Bones, DungeonDecorationType.Rubble },
        DungeonRoomArchetype.AbandonedCamp =>
            new[] { DungeonDecorationType.Campfire, DungeonDecorationType.Bedroll, DungeonDecorationType.BrokenTable },
        DungeonRoomArchetype.ExitChamber =>
            new[] { DungeonDecorationType.Brazier, DungeonDecorationType.Banner, DungeonDecorationType.Rune },
        _ => new[] { DungeonDecorationType.Crate, DungeonDecorationType.Barrel, DungeonDecorationType.BrokenTable }
    };

    private static DungeonRoomArchetype[] AdditionalArchetypesFor(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Castle => new[]
        {
            DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.Barracks,
            DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.StoreRoom
        },
        DungeonTheme.Sewer => new[]
        {
            DungeonRoomArchetype.Lair, DungeonRoomArchetype.Lair,
            DungeonRoomArchetype.StoreRoom, DungeonRoomArchetype.AbandonedCamp
        },
        DungeonTheme.Cemetery => new[]
        {
            DungeonRoomArchetype.Lair, DungeonRoomArchetype.AbandonedCamp,
            DungeonRoomArchetype.Lair, DungeonRoomArchetype.StoreRoom
        },
        DungeonTheme.Library => new[]
        {
            DungeonRoomArchetype.StoreRoom, DungeonRoomArchetype.StoreRoom,
            DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.AbandonedCamp
        },
        DungeonTheme.Forge => new[]
        {
            DungeonRoomArchetype.Barracks, DungeonRoomArchetype.GuardPost,
            DungeonRoomArchetype.StoreRoom, DungeonRoomArchetype.GuardPost
        },
        _ => new[]
        {
            DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.AbandonedCamp,
            DungeonRoomArchetype.Barracks, DungeonRoomArchetype.Lair
        }
    };

    private static DungeonDecorationType[] DecorationsFor(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Castle => new[]
        {
            DungeonDecorationType.Banner, DungeonDecorationType.Brazier, DungeonDecorationType.WeaponRack
        },
        DungeonTheme.Sewer => new[]
        {
            DungeonDecorationType.Barrel, DungeonDecorationType.Mushrooms, DungeonDecorationType.Rubble
        },
        DungeonTheme.Cemetery => new[]
        {
            DungeonDecorationType.Bones, DungeonDecorationType.Rune, DungeonDecorationType.Rubble
        },
        DungeonTheme.Library => new[]
        {
            DungeonDecorationType.BrokenTable, DungeonDecorationType.Crate, DungeonDecorationType.Rune
        },
        DungeonTheme.Forge => new[]
        {
            DungeonDecorationType.Brazier, DungeonDecorationType.WeaponRack, DungeonDecorationType.Rubble
        },
        _ => new[]
        {
            DungeonDecorationType.Campfire, DungeonDecorationType.Bedroll, DungeonDecorationType.Crate
        }
    };

    private static void PlaceThemeFeature(DungeonLayout layout)
    {
        var preferredArchetypes = layout.Theme switch
        {
            DungeonTheme.Castle => new[] { DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.Barracks },
            DungeonTheme.Sewer => new[] { DungeonRoomArchetype.Lair, DungeonRoomArchetype.StoreRoom },
            DungeonTheme.Cemetery => new[] { DungeonRoomArchetype.AbandonedCamp, DungeonRoomArchetype.Lair },
            DungeonTheme.Library => new[] { DungeonRoomArchetype.StoreRoom, DungeonRoomArchetype.GuardPost },
            DungeonTheme.Forge => new[] { DungeonRoomArchetype.Barracks, DungeonRoomArchetype.GuardPost },
            _ => new[] { DungeonRoomArchetype.GuardPost, DungeonRoomArchetype.AbandonedCamp }
        };
        var room = preferredArchetypes
            .Select(archetype => layout.Rooms.FirstOrDefault(candidate => candidate.Archetype == archetype))
            .FirstOrDefault(candidate => candidate != null)
            // Fall back to any Standard room: with only 1-2 Standard rooms, some themes'
            // preferred archetypes don't exist yet (assignment order fills GuardPost/Barracks
            // first), and the validator requires exactly one theme feature whenever a Standard
            // room exists — returning empty-handed guaranteed a per-attempt validation failure
            // on such floors.
            ?? layout.Rooms.FirstOrDefault(candidate => candidate.Role == DungeonRoomRole.Standard);
        if (room == null) return;

        layout.ThemeFeatures.Add(new DungeonThemeFeature
        {
            X = room.CenterX,
            Y = room.CenterY,
            RoomId = room.Id,
            Type = layout.Theme switch
            {
                DungeonTheme.Castle => DungeonThemeFeatureType.CastleAlarm,
                DungeonTheme.Sewer => DungeonThemeFeatureType.SewerRunoff,
                DungeonTheme.Cemetery => DungeonThemeFeatureType.RestlessGrave,
                DungeonTheme.Library => DungeonThemeFeatureType.ArcaneWard,
                DungeonTheme.Forge => DungeonThemeFeatureType.HeatVent,
                _ => DungeonThemeFeatureType.HideoutTripwire
            }
        });
    }

    private DungeonTheme ThemeForFloor(int floorNumber)
    {
        var themes = Enum.GetValues<DungeonTheme>();
        long value = (long)_seed + floorNumber - 1;
        int index = (int)((value % themes.Length + themes.Length) % themes.Length);
        return themes[index];
    }

    private int DeriveSeed(int width, int height, int floorNumber, int attempt)
    {
        unchecked
        {
            int value = _seed;
            value = value * 397 ^ width;
            value = value * 397 ^ height;
            value = value * 397 ^ floorNumber;
            value = value * 397 ^ attempt;
            return value;
        }
    }
}

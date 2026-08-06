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
    private const int MaxGenerationAttempts = 20;
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

            CarveBestCorridor(maze, layout, from, to, random, preferNewGround: false);
            AddConnection(layout, edges, from.Id, to.Id, isLoop: false);
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

        var horizontalFirst = BuildCorridorPath(fromOutside.x, fromOutside.y, toOutside.x, toOutside.y, true);
        var verticalFirst = BuildCorridorPath(fromOutside.x, fromOutside.y, toOutside.x, toOutside.y, false);

        // Prefer the elbow that hugs rooms least. Running flush along a room's outside wall is what
        // produced multi-tile contacts before; scoring it down keeps corridors off the masonry.
        int horizontalHug = CountRoomHugs(layout, horizontalFirst);
        int verticalHug = CountRoomHugs(layout, verticalFirst);

        List<(int x, int y)> path;
        if (horizontalHug != verticalHug)
            path = horizontalHug < verticalHug ? horizontalFirst : verticalFirst;
        else
            path = random.Next(2) == 0 ? horizontalFirst : verticalFirst;

        // A loop edge only earns its place if it opens genuinely new ground.
        if (preferNewGround && path.Count(cell => maze.Walls[cell.x, cell.y]) < 2)
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

    /// <summary>Punch a one-tile entrance through a room's wall ring and mark it as a doorway.</summary>
    private static void CarveThreshold(Maze maze, DungeonLayout layout, (int x, int y) cell)
    {
        maze.Tiles[cell.x, cell.y] = TileType.Floor;
        layout.Tiles[cell.x, cell.y] = DungeonTileType.Doorway;
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

    /// <summary>How many cells of this path sit directly against a room's floor — corridors that
    /// graze room walls, which is what we want to avoid.</summary>
    private static int CountRoomHugs(DungeonLayout layout, List<(int x, int y)> path)
    {
        int width = layout.Tiles.GetLength(0);
        int height = layout.Tiles.GetLength(1);
        int hugs = 0;
        foreach (var (x, y) in path)
        {
            foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                if (layout.Tiles[nx, ny] == DungeonTileType.RoomFloor) hugs++;
            }
        }
        return hugs;
    }

    /// <summary>
    /// Pick where <paramref name="room"/> opens toward <paramref name="toward"/>: the threshold tile
    /// in its wall ring, plus the corridor tile just beyond it. Sides are tried facing-first, and any
    /// side that would breach the map boundary is skipped — the outer wall has to stay solid.
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

        // Face the neighbour first; fall back to the remaining sides if that one can't be used.
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

        foreach (char side in sides)
        {
            // Align the opening with the neighbour where possible, so corridors run short and
            // straight instead of doubling back along the wall.
            int doorX, doorY, outX, outY;
            switch (side)
            {
                case 'E':
                    doorX = room.Right + 1; outX = doorX + 1;
                    doorY = outY = Math.Clamp(toward.CenterY, room.Y, room.Bottom);
                    break;
                case 'W':
                    doorX = room.X - 1; outX = doorX - 1;
                    doorY = outY = Math.Clamp(toward.CenterY, room.Y, room.Bottom);
                    break;
                case 'S':
                    doorY = room.Bottom + 1; outY = doorY + 1;
                    doorX = outX = Math.Clamp(toward.CenterX, room.X, room.Right);
                    break;
                default:
                    doorY = room.Y - 1; outY = doorY - 1;
                    doorX = outX = Math.Clamp(toward.CenterX, room.X, room.Right);
                    break;
            }

            // Both the threshold and the tile beyond it must sit inside the boundary wall.
            if (doorX < 1 || doorY < 1 || doorX >= width - 1 || doorY >= height - 1) continue;
            if (outX < 1 || outY < 1 || outX >= width - 1 || outY >= height - 1) continue;
            // Never open into another room's floor — that would merge two rooms into one space.
            if (layout.Tiles[doorX, doorY] == DungeonTileType.RoomFloor) continue;
            if (layout.Tiles[outX, outY] == DungeonTileType.RoomFloor) continue;
            // Rooms may sit a single tile apart, in which case the wall cell between them touches
            // both. Opening it would be a shared hole rather than a doorway into one room.
            if (CountRoomFloorNeighbours(layout, doorX, doorY) != 1) continue;

            return ((doorX, doorY), (outX, outY));
        }

        _ = random; // side choice is deterministic given the geometry; kept for signature symmetry
        return null;
    }

    private static List<(int x, int y)> BuildCorridorPath(
        int startX,
        int startY,
        int endX,
        int endY,
        bool horizontalFirst)
    {
        var path = new List<(int x, int y)>();
        if (horizontalFirst)
        {
            AddLine(path, startX, startY, endX, startY);
            AddLine(path, endX, startY, endX, endY);
        }
        else
        {
            AddLine(path, startX, startY, startX, endY);
            AddLine(path, startX, endY, endX, endY);
        }

        return path.Distinct().ToList();
    }

    private static void AddLine(List<(int x, int y)> path, int startX, int startY, int endX, int endY)
    {
        int dx = Math.Sign(endX - startX);
        int dy = Math.Sign(endY - startY);
        int x = startX;
        int y = startY;
        path.Add((x, y));

        while (x != endX || y != endY)
        {
            x += dx;
            y += dy;
            path.Add((x, y));
        }
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
            .FirstOrDefault(candidate => candidate != null);
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

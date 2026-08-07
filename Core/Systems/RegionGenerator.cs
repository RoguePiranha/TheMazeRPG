using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Builds a starting region from a world's options: a walled town against a mountainside with the
/// dungeon mouth set into it, a river on the far side, roads between gates through a market square,
/// buildings on road frontage, a mine outside the walls, and cleared ground with forest beyond.
///
/// [Starting Region.md](../../Info/Starting%20Region.md) is the grammar every generated region
/// satisfies; the seed picks the wording (owner ruling 2026-08-05: same style, never the same town).
/// Tiles are person-scaled — roughly a metre each — so the spec's ~100m cleared band really is a
/// hundred tiles, which is why regions run to hundreds of tiles per axis (ruling #35).
///
/// The layout is composed in a canonical frame where the mountain is always on the west, then
/// mapped onto the world grid through <see cref="ToWorld"/>. That way the four possible mountain
/// edges cost one coordinate transform rather than four copies of the layout logic.
/// </summary>
public static class RegionGenerator
{
    // A mountain you can tunnel into needs mass behind its face, not a scenic crust.
    private const int MountainDepth = 60;
    private const int RiverWidth = 3;
    private const int RiverInset = 6;      // gap between river and the forest fringe
    private const int ClearedBand = 100;   // Starting Region.md: ~100m of cleared ground outside the walls
    private const int RoadWidth = 3;
    private const int MaxAttempts = 10;

    /// <summary>
    /// Generate a region, retrying at seed+1 if the grammar checks fail. Validation is load-bearing:
    /// a player's world gets no hand-fix pass, so a region that breaks the spec must never ship.
    /// </summary>
    public static Maze Generate(WorldGenOptions options, out int effectiveSeed)
    {
        var profile = WorldService.ResolveProfile(options);

        // Geometry floor, derived from the layout constants above: each axis must hold the
        // cleared band and forest fringe on both open sides plus a real town — and the mountain
        // edge is seed-random, so BOTH axes must satisfy the stricter constraint. An undersized
        // profile used to surface as a cryptic ArgumentException inside AddGate's clamp (min >
        // max); fail up front naming the actual cause instead. MazeGenerator guards its minimum
        // dimensions the same way.
        const int MinTownSpan = 30;
        const int RiverJitter = 6; // CarveRiver's course wanders up to ±6 tiles
        int forest = profile.Size.ForestDepth;
        int minAxis = Math.Max(
            MountainDepth + MinTownSpan + ClearedBand + forest + RiverInset + RiverWidth + RiverJitter,
            2 * (ClearedBand + forest) + MinTownSpan);
        if (profile.Size.RegionWidth < minAxis || profile.Size.RegionHeight < minAxis)
            throw new InvalidOperationException(
                $"Region size profile {profile.Size.RegionWidth}x{profile.Size.RegionHeight} (forest depth {forest}) " +
                $"is below the {minAxis}-per-axis minimum the layout constants require — " +
                "check Data/Config/worldgen.json.");

        IReadOnlyList<string> lastErrors = Array.Empty<string>();

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            effectiveSeed = options.Seed + attempt;
            var maze = Compose(effectiveSeed, options.Size, profile.Size);
            lastErrors = RegionValidator.Validate(maze);
            if (lastErrors.Count == 0)
                return maze;
        }

        throw new InvalidOperationException(
            $"Unable to generate a valid region after {MaxAttempts} attempts: {string.Join("; ", lastErrors)}");
    }

    private static Maze Compose(int seed, WorldSize size, WorldSizeProfile profile)
    {
        var random = new Random(seed);
        int width = profile.RegionWidth;
        int height = profile.RegionHeight;

        var maze = new Maze(width, height) { FloorNumber = 0 };
        var region = new RegionLayout
        {
            Seed = seed,
            Size = size,
            MountainEdge = (RegionEdge)random.Next(4)
        };
        maze.Region = region;

        bool transposed = region.MountainEdge is RegionEdge.North or RegionEdge.South;
        int localWidth = transposed ? height : width;
        int localHeight = transposed ? width : height;

        var frame = new Frame(maze, region.MountainEdge, width, height);

        FillGround(maze);
        CarveMountain(frame, seed, localHeight);
        var river = CarveRiver(frame, region, localWidth, localHeight, random, profile.ForestDepth);
        var town = LayOutTown(region, frame, localWidth, localHeight, profile, river, random);
        var gates = CarveWallsAndGates(frame, region, town, random);
        CarveRoads(frame, region, town, gates, random);
        CarveOutboundRoads(frame, town, gates, localWidth, localHeight);
        PlaceBuildings(frame, region, town, profile, random);
        PlacePointsOfInterest(maze, frame, region, town, gates, localHeight, random);
        PlantForest(frame, region, localWidth, localHeight, profile.ForestDepth, town, random);

        // Re-lay the bedrock rim last. The mountain carve runs over the edge it sits on, and any
        // later stage could too — the rim has to be the last word or a tunnel eventually reaches
        // the edge of the world and keeps going.
        SealBoundary(maze);

        // Everything is lit and known: a town isn't a fog-of-war space.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (maze.Tiles[x, y].IsPassable())
                    maze.Explored[x, y] = true;

        return maze;
    }

    // --- Coordinate frame -------------------------------------------------------------------

    /// <summary>
    /// Maps the canonical layout frame (mountain on the west, river on the east) onto the world
    /// grid for whichever edge the mountain actually occupies.
    /// </summary>
    private sealed class Frame
    {
        private readonly Maze _maze;
        private readonly RegionEdge _edge;
        private readonly int _width;
        private readonly int _height;

        public Frame(Maze maze, RegionEdge edge, int width, int height)
        {
            _maze = maze;
            _edge = edge;
            _width = width;
            _height = height;
        }

        public (int x, int y) ToWorld(int lx, int ly) => _edge switch
        {
            RegionEdge.West => (lx, ly),
            RegionEdge.East => (_width - 1 - lx, ly),
            RegionEdge.North => (ly, lx),
            _ => (ly, _height - 1 - lx)
        };

        public void Set(int lx, int ly, TileType tile)
        {
            var (x, y) = ToWorld(lx, ly);
            if (_maze.InBounds(x, y)) _maze.Tiles[x, y] = tile;
        }

        public TileType Get(int lx, int ly)
        {
            var (x, y) = ToWorld(lx, ly);
            return _maze.InBounds(x, y) ? _maze.Tiles[x, y] : TileType.Wall;
        }

        public void AddFeature(int lx, int ly, MazeFeatureType type)
        {
            var (x, y) = ToWorld(lx, ly);
            if (_maze.InBounds(x, y))
                _maze.Features.Add(new MazeFeature { X = x, Y = y, Type = type });
        }
    }

    /// <summary>The walled town's rect in the canonical frame.</summary>
    private readonly record struct TownRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public int CenterX => (Left + Right) / 2;
        public int CenterY => (Top + Bottom) / 2;
    }

    // --- Stages -----------------------------------------------------------------------------

    private static void FillGround(Maze maze)
    {
        for (int x = 0; x < maze.Width; x++)
            for (int y = 0; y < maze.Height; y++)
                maze.Tiles[x, y] = TileType.Floor;

        SealBoundary(maze);
    }

    /// <summary>
    /// Bedrock, not ordinary wall: the rim has to survive a pick as well as a footstep, or a
    /// determined tunnel eventually digs its way off the edge of the world. Applied again after
    /// every other stage, since the mountain is carved straight over the edge it rests on.
    /// </summary>
    private static void SealBoundary(Maze maze)
    {
        for (int x = 0; x < maze.Width; x++)
        {
            maze.Tiles[x, 0] = TileType.Bedrock;
            maze.Tiles[x, maze.Height - 1] = TileType.Bedrock;
        }
        for (int y = 0; y < maze.Height; y++)
        {
            maze.Tiles[0, y] = TileType.Bedrock;
            maze.Tiles[maze.Width - 1, y] = TileType.Bedrock;
        }
    }

    /// <summary>
    /// The mountainside is natural rock, not masonry — it can be dug into, which is what the mine
    /// actually is (owner ruling 2026-08-06). It is deliberately a thick mass rather than a scenic
    /// crust: a tunnel has to have somewhere to go, or the first few swings hit the edge of the
    /// world. Ore is seeded in seams rather than spread evenly, so prospecting means reading a rock
    /// face rather than swinging at anything, and seams thicken the deeper you push.
    ///
    /// Nothing behind the rock is the dungeon. The dungeon is a separate dimensional space reached
    /// through its threshold, not a cavity inside this mountain, so no tunnel can ever break into it.
    /// </summary>
    private static void CarveMountain(Frame frame, int seed, int localHeight)
    {
        var oreNoise = new PerlinNoise(seed + 4409);

        for (int lx = 0; lx < MountainDepth; lx++)
        {
            for (int ly = 0; ly < localHeight; ly++)
            {
                // Depth is measured from the face (which looks toward the town) inward.
                int depth = MountainDepth - 1 - lx;
                float sample = (float)oreNoise.Noise(lx * 0.09, ly * 0.09);
                float threshold = 0.30f - Math.Min(0.16f, depth * 0.004f);
                frame.Set(lx, ly, sample > threshold ? TileType.OreVein : TileType.Stone);
            }
        }
    }

    /// <summary>The river's column range in the canonical frame, so later stages can keep clear of it.</summary>
    private readonly record struct River(int Left, int Right);

    private static River CarveRiver(
        Frame frame,
        RegionLayout region,
        int localWidth,
        int localHeight,
        Random random,
        int forestDepth)
    {
        _ = forestDepth;
        int baseColumn = localWidth - RiverInset - RiverWidth;
        var noise = new PerlinNoise(region.Seed);

        int left = int.MaxValue;
        int right = int.MinValue;
        var columnAt = new int[localHeight];

        for (int ly = 0; ly < localHeight; ly++)
        {
            // A gently wandering course rather than a ruled line.
            int jitter = (int)MathF.Round((float)noise.Noise(ly * 0.02, 0.5) * 6f);
            int column = Math.Clamp(baseColumn + jitter, 8, localWidth - RiverWidth - 2);
            columnAt[ly] = column;
            left = Math.Min(left, column);
            right = Math.Max(right, column + RiverWidth - 1);

            for (int w = 0; w < RiverWidth; w++)
                frame.Set(column + w, ly, TileType.Water);
        }

        // Flow direction decides which end is upstream, so the intake can sit above the outfall.
        region.RiverFlowsPositive = random.Next(2) == 0;
        int upstreamY = region.RiverFlowsPositive ? localHeight / 4 : localHeight * 3 / 4;
        int downstreamY = region.RiverFlowsPositive ? localHeight * 3 / 4 : localHeight / 4;

        var (ix, iy) = frame.ToWorld(columnAt[upstreamY] - 1, upstreamY);
        region.IntakeX = ix;
        region.IntakeY = iy;
        var (ox, oy) = frame.ToWorld(columnAt[downstreamY] - 1, downstreamY);
        region.OutfallX = ox;
        region.OutfallY = oy;

        // The bridge is no longer carved here: it belongs to the road network, and roads follow the
        // gates — CarveOutboundRoads lays the east road straight through the river at the gate's
        // row, which is what makes the crossing part of a route rather than a plank in the wilds.

        return new River(left, right);
    }

    private static TownRect LayOutTown(
        RegionLayout region,
        Frame frame,
        int localWidth,
        int localHeight,
        WorldSizeProfile profile,
        River river,
        Random random)
    {
        _ = localWidth;
        // The town butts against the mountain, which is its defence on that side; every other side
        // needs the cleared band, then the forest, before anything else.
        int left = MountainDepth;
        int maxRight = river.Left - ClearedBand - profile.ForestDepth;
        int maxBottom = localHeight - ClearedBand - profile.ForestDepth;
        int minTop = ClearedBand + profile.ForestDepth;

        // Size from population rather than filling whatever space happens to be free.
        int desiredWidth = 60 + (int)(profile.Population * 1.4);
        int desiredHeight = 50 + profile.Population;

        int right = Math.Min(maxRight, left + desiredWidth);
        int availableHeight = maxBottom - minTop;
        int townHeight = Math.Min(desiredHeight, availableHeight);
        int slack = Math.Max(0, availableHeight - townHeight);
        int top = minTop + (slack > 0 ? random.Next(slack + 1) : 0);
        int bottom = top + townHeight;

        var town = new TownRect(left, top, right, bottom);
        var (wl, wt) = frame.ToWorld(town.Left, town.Top);
        var (wr, wb) = frame.ToWorld(town.Right, town.Bottom);
        region.WallLeft = Math.Min(wl, wr);
        region.WallRight = Math.Max(wl, wr);
        region.WallTop = Math.Min(wt, wb);
        region.WallBottom = Math.Max(wt, wb);
        return town;
    }

    private static List<(int lx, int ly, char side)> CarveWallsAndGates(
        Frame frame, RegionLayout region, TownRect town, Random random)
    {
        var gates = new List<(int lx, int ly, char side)>();
        for (int lx = town.Left; lx <= town.Right; lx++)
        {
            frame.Set(lx, town.Top, TileType.Wall);
            frame.Set(lx, town.Bottom, TileType.Wall);
        }
        for (int ly = town.Top; ly <= town.Bottom; ly++)
        {
            frame.Set(town.Left, ly, TileType.Wall);
            frame.Set(town.Right, ly, TileType.Wall);
        }

        // Gates: one on the far side from the mountain, one north or south, seed-placed along the
        // wall so no two worlds have the same approach.
        gates.Add(AddGate(frame, region, town, 'E',
            town.Top + town.Height / 3 + random.Next(Math.Max(1, town.Height / 4))));
        gates.Add(AddGate(frame, region, town, random.Next(2) == 0 ? 'N' : 'S',
            town.Left + town.Width / 3 + random.Next(Math.Max(1, town.Width / 4))));
        return gates;
    }

    private static (int lx, int ly, char side) AddGate(
        Frame frame, RegionLayout region, TownRect town, char side, int along)
    {
        int gateLx = 0, gateLy = 0;
        for (int offset = -(RoadWidth / 2); offset <= RoadWidth / 2; offset++)
        {
            int lx, ly;
            switch (side)
            {
                case 'E': lx = town.Right; ly = Math.Clamp(along + offset, town.Top + 1, town.Bottom - 1); break;
                case 'N': ly = town.Top; lx = Math.Clamp(along + offset, town.Left + 1, town.Right - 1); break;
                default: ly = town.Bottom; lx = Math.Clamp(along + offset, town.Left + 1, town.Right - 1); break;
            }
            frame.Set(lx, ly, TileType.Road);
            if (offset == 0)
            {
                gateLx = lx;
                gateLy = ly;
                var (gx, gy) = frame.ToWorld(lx, ly);
                // The stored side must be the WORLD side, not the canonical one: the layout frame
                // rotates with the mountain edge, so canonical 'E' can be world south. Mapping the
                // canonical outward step through ToWorld gives the true facing wherever the
                // mountain ended up.
                var (odx, ody) = side switch { 'E' => (1, 0), 'N' => (0, -1), _ => (0, 1) };
                var (wx, wy) = frame.ToWorld(lx + odx, ly + ody);
                region.Gates.Add(new TownGate
                {
                    X = gx,
                    Y = gy,
                    Side = (wx - gx, wy - gy) switch
                    {
                        (1, 0) => RegionEdge.East,
                        (-1, 0) => RegionEdge.West,
                        (0, -1) => RegionEdge.North,
                        _ => RegionEdge.South
                    }
                });
            }
        }

        return (gateLx, gateLy, side);
    }

    private static void CarveRoads(Frame frame, RegionLayout region, TownRect town,
        List<(int lx, int ly, char side)> gates, Random random)
    {
        int squareSize = 9;
        int squareX = town.CenterX + random.Next(-town.Width / 8, town.Width / 8 + 1);
        int squareY = town.CenterY + random.Next(-town.Height / 8, town.Height / 8 + 1);
        squareX = Math.Clamp(squareX, town.Left + squareSize, town.Right - squareSize);
        squareY = Math.Clamp(squareY, town.Top + squareSize, town.Bottom - squareSize);

        // The plaza itself.
        for (int lx = squareX - squareSize / 2; lx <= squareX + squareSize / 2; lx++)
            for (int ly = squareY - squareSize / 2; ly <= squareY + squareSize / 2; ly++)
                frame.Set(lx, ly, TileType.Road);

        var (sx, sy) = frame.ToWorld(squareX, squareY);
        region.SquareX = sx;
        region.SquareY = sy;
        region.SquareSize = squareSize;

        // Spine roads: the square out to every wall, so each gate is on the network and the town
        // reads as quarters rather than one undivided field.
        StripeHorizontal(frame, town.Left + 1, town.Right - 1, squareY);
        StripeVertical(frame, town.Top + 1, town.Bottom - 1, squareX);

        // The dungeon avenue. Rule (Starting Region.md, note 08 stage 4): a place people visit
        // every day gets a road — and nothing in town is visited harder than the dungeon mouth,
        // with its guard station and its daily divers. The avenue runs from the mountain-side
        // wall (where PlacePointsOfInterest cuts the mouth through, at the town's centre row)
        // east to the vertical spine, putting the mouth on the network. Hidden places — the
        // trapdoor home — deliberately get nothing.
        StripeHorizontal(frame, town.Left + 1, squareX, town.CenterY);

        // Gate access roads: gates are seed-placed along the walls independently of the spines,
        // so each gets its own connecting road to the spine network — the comment above promises
        // "each gate is on the network", and until now nothing actually delivered that (a gate
        // usually opened onto bare field inside the wall).
        foreach (var gate in gates)
        {
            switch (gate.side)
            {
                case 'E':
                    StripeHorizontal(frame, squareX, town.Right - 1, gate.ly);
                    break;
                case 'N':
                    StripeVertical(frame, town.Top + 1, squareY, gate.lx);
                    break;
                default: // 'S'
                    StripeVertical(frame, squareY, town.Bottom - 1, gate.lx);
                    break;
            }
        }

        // Side streets on a rough grid off the spines. Without these every building in town would
        // have to front one of the two main roads, and the whole population ends up in a single
        // terrace along the high street.
        int block = 18 + random.Next(7);
        for (int ly = squareY - block; ly > town.Top + 4; ly -= block)
            StripeHorizontal(frame, town.Left + 1, town.Right - 1, ly);
        for (int ly = squareY + block; ly < town.Bottom - 4; ly += block)
            StripeHorizontal(frame, town.Left + 1, town.Right - 1, ly);
        for (int lx = squareX - block; lx > town.Left + 4; lx -= block)
            StripeVertical(frame, town.Top + 1, town.Bottom - 1, lx);
        for (int lx = squareX + block; lx < town.Right - 4; lx += block)
            StripeVertical(frame, town.Top + 1, town.Bottom - 1, lx);
    }

    /// <summary>
    /// Roads out of town: every gate's road continues across the cleared band and through the
    /// forest to the edge of the region. Starting Region.md: the wilds are untamed *except along
    /// roads*, which the kingdom patrols — a gate that opened onto trackless grass contradicted
    /// that sentence. The east road crosses the river on its way out; StripeHorizontal paves
    /// water like any other non-wall ground, and that paved crossing at the gate's own row IS the
    /// bridge — a crossing on a route people actually walk, not a plank dropped mid-wilderness.
    /// </summary>
    private static void CarveOutboundRoads(
        Frame frame, TownRect town, List<(int lx, int ly, char side)> gates, int localWidth, int localHeight)
    {
        foreach (var gate in gates)
        {
            switch (gate.side)
            {
                case 'E':
                    StripeHorizontal(frame, town.Right + 1, localWidth - 2, gate.ly);
                    break;
                case 'N':
                    StripeVertical(frame, 1, town.Top - 1, gate.lx);
                    break;
                default: // 'S'
                    StripeVertical(frame, town.Bottom + 1, localHeight - 2, gate.lx);
                    break;
            }
        }
    }

    private static void StripeHorizontal(Frame frame, int fromX, int toX, int centreY)
    {
        for (int lx = Math.Min(fromX, toX); lx <= Math.Max(fromX, toX); lx++)
            for (int offset = -(RoadWidth / 2); offset <= RoadWidth / 2; offset++)
                if (frame.Get(lx, centreY + offset) != TileType.Wall)
                    frame.Set(lx, centreY + offset, TileType.Road);
    }

    private static void StripeVertical(Frame frame, int fromY, int toY, int centreX)
    {
        for (int ly = Math.Min(fromY, toY); ly <= Math.Max(fromY, toY); ly++)
            for (int offset = -(RoadWidth / 2); offset <= RoadWidth / 2; offset++)
                if (frame.Get(centreX + offset, ly) != TileType.Wall)
                    frame.Set(centreX + offset, ly, TileType.Road);
    }

    private static void PlaceBuildings(
        Frame frame,
        RegionLayout region,
        TownRect town,
        WorldSizeProfile profile,
        Random random)
    {
        // Named premises first — the town needs its trades wherever else it varies — then homes
        // fill the remaining frontage until the population is housed (about two per home).
        var wanted = new List<BuildingKind>
        {
            BuildingKind.Smithy, BuildingKind.Alchemist, BuildingKind.Carpenter,
            BuildingKind.Temple, BuildingKind.TrainingSchool, BuildingKind.GuardStation,
            BuildingKind.Tavern, BuildingKind.WaterTower
        };
        int homes = Math.Max(6, profile.Population / 2);
        for (int i = 0; i < homes; i++) wanted.Add(BuildingKind.Home);

        int id = 0;
        var placed = new List<Building>();

        foreach (var kind in wanted)
        {
            var (bw, bh) = FootprintFor(kind, random);
            Building? building = null;

            // Per-building budget: a shared pool drained by early failures silently skipped the
            // later homes, so congested layouts shipped towns housing a fraction of the profile
            // population.
            int attempts = 60;
            while (attempts-- > 0)
            {
                int lx = random.Next(town.Left + 2, Math.Max(town.Left + 3, town.Right - bw - 1));
                int ly = random.Next(town.Top + 2, Math.Max(town.Top + 3, town.Bottom - bh - 1));
                var candidate = new Building { Id = id, Kind = kind, X = lx, Y = ly, Width = bw, Height = bh };

                if (!IsClearForBuilding(frame, candidate)) continue;
                if (placed.Any(other => Overlaps(other, candidate, 1))) continue;

                // A building is only usable if it can open onto a road.
                var door = FindRoadFacingDoor(frame, candidate);
                if (door == null) continue;

                candidate.DoorX = door.Value.lx;
                candidate.DoorY = door.Value.ly;
                building = candidate;
                break;
            }

            if (building == null) continue;

            CarveBuilding(frame, building);
            placed.Add(building);

            // Store the building entirely in world space. The layout frame is the generator's own
            // scratch coordinate system; everything downstream (validation, rendering, later NPC
            // anchors) speaks world coordinates, and a record holding a mix of the two is a trap.
            var (doorWx, doorWy) = frame.ToWorld(building.DoorX, building.DoorY);
            var (cornerAx, cornerAy) = frame.ToWorld(building.X, building.Y);
            var (cornerBx, cornerBy) = frame.ToWorld(building.Right, building.Bottom);
            var (interiorX, interiorY) = frame.ToWorld(building.CenterX, building.CenterY);

            var stored = new Building
            {
                Id = id,
                Kind = kind,
                X = Math.Min(cornerAx, cornerBx),
                Y = Math.Min(cornerAy, cornerBy),
                Width = Math.Abs(cornerBx - cornerAx) + 1,
                Height = Math.Abs(cornerBy - cornerAy) + 1,
                DoorX = doorWx,
                DoorY = doorWy,
                InteriorX = interiorX,
                InteriorY = interiorY
            };
            region.Buildings.Add(stored);
            id++;
        }

        // Exactly one home hides the trapdoor to the dark temple (inert for now).
        var homesPlaced = region.Buildings.Where(b => b.Kind == BuildingKind.Home).ToList();
        if (homesPlaced.Count > 0)
            homesPlaced[random.Next(homesPlaced.Count)].HasTrapdoor = true;
    }

    private static (int w, int h) FootprintFor(BuildingKind kind, Random random) => kind switch
    {
        BuildingKind.Temple => (13, 11),
        BuildingKind.TrainingSchool => (13, 9),
        BuildingKind.Tavern => (11, 9),
        BuildingKind.Smithy => (9, 8),
        BuildingKind.GuardStation => (9, 7),
        BuildingKind.Alchemist => (8, 7),
        BuildingKind.Carpenter => (9, 7),
        BuildingKind.WaterTower => (6, 6),
        _ => (random.Next(6, 9), random.Next(5, 8))
    };

    private static bool Overlaps(Building a, Building b, int margin) =>
        a.X - margin <= b.Right && a.Right + margin >= b.X &&
        a.Y - margin <= b.Bottom && a.Bottom + margin >= b.Y;

    /// <summary>A plot is buildable only on open ground — never over roads, water or the walls.</summary>
    private static bool IsClearForBuilding(Frame frame, Building building)
    {
        for (int lx = building.X - 1; lx <= building.Right + 1; lx++)
            for (int ly = building.Y - 1; ly <= building.Bottom + 1; ly++)
                if (frame.Get(lx, ly) != TileType.Floor)
                    return false;
        return true;
    }

    /// <summary>
    /// Find a wall tile with a road directly outside it — the building's frontage. Buildings that
    /// can't reach a road aren't placed at all, which is what keeps every door reachable.
    /// </summary>
    private static (int lx, int ly)? FindRoadFacingDoor(Frame frame, Building building)
    {
        var candidates = new List<(int lx, int ly)>();

        for (int lx = building.X + 1; lx < building.Right; lx++)
        {
            if (RoadWithin(frame, lx, building.Y - 1, 0, -1)) candidates.Add((lx, building.Y));
            if (RoadWithin(frame, lx, building.Bottom + 1, 0, 1)) candidates.Add((lx, building.Bottom));
        }
        for (int ly = building.Y + 1; ly < building.Bottom; ly++)
        {
            if (RoadWithin(frame, building.X - 1, ly, -1, 0)) candidates.Add((building.X, ly));
            if (RoadWithin(frame, building.Right + 1, ly, 1, 0)) candidates.Add((building.Right, ly));
        }

        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>Is there road within a few tiles in this direction? Buildings sit just off the
    /// street rather than flush against it, so a short reach counts as frontage.</summary>
    private static bool RoadWithin(Frame frame, int lx, int ly, int dx, int dy)
    {
        for (int step = 0; step < 4; step++)
        {
            var tile = frame.Get(lx + dx * step, ly + dy * step);
            if (tile == TileType.Road) return true;
            if (tile != TileType.Floor) return false;
        }
        return false;
    }

    private static void CarveBuilding(Frame frame, Building building)
    {
        for (int lx = building.X; lx <= building.Right; lx++)
        {
            for (int ly = building.Y; ly <= building.Bottom; ly++)
            {
                bool border = lx == building.X || lx == building.Right ||
                              ly == building.Y || ly == building.Bottom;
                frame.Set(lx, ly, border ? TileType.Wall : TileType.Floor);
            }
        }

        // One door, shut — the same bump-to-open door the dungeon uses.
        frame.Set(building.DoorX, building.DoorY, TileType.DoorClosed);

        // And a clear step from the threshold to the street.
        for (int step = 1; step <= 4; step++)
        {
            int ox = building.DoorX + OutwardX(building) * step;
            int oy = building.DoorY + OutwardY(building) * step;
            if (frame.Get(ox, oy) == TileType.Road) break;
            frame.Set(ox, oy, TileType.Road);
        }
    }

    private static int OutwardX(Building b) =>
        b.DoorX == b.X ? -1 : b.DoorX == b.Right ? 1 : 0;

    private static int OutwardY(Building b) =>
        b.DoorY == b.Y ? -1 : b.DoorY == b.Bottom ? 1 : 0;

    private static void PlacePointsOfInterest(
        Maze maze,
        Frame frame,
        RegionLayout region,
        TownRect town,
        List<(int lx, int ly, char side)> gates,
        int localHeight,
        Random random)
    {
        // Dungeon mouth: a forecourt cut into the mountain face at the town's centre row, opening
        // through the west wall onto the dungeon avenue (carved by CarveRoads). Three tiles wide,
        // like every road: this is the most-trafficked doorway in the region, and a one-tile slot
        // read as a crack in the rock rather than the entrance the whole town is built around.
        int mouthY = town.CenterY;
        for (int lx = MountainDepth - 3; lx <= town.Left; lx++)
            for (int dy = -1; dy <= 1; dy++)
                frame.Set(lx, mouthY + dy, TileType.Road);
        frame.AddFeature(MountainDepth - 1, mouthY, MazeFeatureType.DungeonEntrance);

        // The threshold and its frame are bedrock. The dungeon is a separate dimensional space
        // reached through this doorway, not a cavity in the rock, so the doorway itself is not
        // ordinary stone and no pick will take it down (owner ruling 2026-08-06).
        for (int lx = MountainDepth - 4; lx <= MountainDepth; lx++)
            for (int dy = -2; dy <= 2; dy++)
                if (frame.Get(lx, mouthY + dy).IsNaturalRock())
                    frame.Set(lx, mouthY + dy, TileType.Bedrock);

        // The mine is a real adit: an opening driven into the mountainside with rock all around it
        // to work, not a symbol standing on grass.
        int aditLength = 8;

        // Starting Region.md wants the guard station beside the dungeon approach; today buildings
        // land on whatever road frontage fits, and repositioning a carved building isn't worth
        // building here — note 08's prefab-on-lots rework places it properly, with a validator.

        // The mine sits outside the walls, further down the same mountainside (Starting Region.md) —
        // so it has to clear the town's vertical span, not merely sit against the mountain.
        bool below = gates.Any(g => g.side == 'S') || random.Next(2) == 0;
        int mineY = below
            ? Math.Min(town.Bottom + 12 + random.Next(20), localHeight - 3)
            : Math.Max(town.Top - 12 - random.Next(20), 2);

        // Drive the adit in from the face, leaving a working face of live rock at its end.
        for (int lx = MountainDepth - aditLength; lx <= MountainDepth; lx++)
            frame.Set(lx, mineY, TileType.Floor);
        frame.AddFeature(MountainDepth - aditLength + 1, mineY, MazeFeatureType.MineEntrance);

        // A track from the mine along the mountain's foot and round to the nearest gate, staying
        // outside the walls the whole way.
        var target = gates
            .OrderBy(g => Math.Abs(g.ly - mineY) + Math.Abs(g.lx - MountainDepth))
            .First();
        int laneY = below
            ? Math.Min(town.Bottom + 4, localHeight - 3)
            : Math.Max(town.Top - 4, 2);
        StripeVertical(frame, Math.Min(mineY, laneY), Math.Max(mineY, laneY), MountainDepth + 1);
        StripeHorizontal(frame, MountainDepth + 1, target.lx, laneY);
        StripeVertical(frame, Math.Min(laneY, target.ly), Math.Max(laneY, target.ly), target.lx);

        // Trade premises get their interactable feature on the interior side of their door —
        // one step inward, never ON the door tile, where the bump-to-open door and the Press-E
        // interaction would fight over the same cell.
        foreach (var building in region.Buildings)
        {
            var type = building.Kind switch
            {
                BuildingKind.Smithy => MazeFeatureType.Smithy,
                _ => (MazeFeatureType?)null
            };
            if (type == null) continue;
            int inwardX = building.DoorX == building.X ? 1 : building.DoorX == building.Right ? -1 : 0;
            int inwardY = building.DoorY == building.Y ? 1 : building.DoorY == building.Bottom ? -1 : 0;
            maze.Features.Add(new MazeFeature
            {
                X = Clamp(building.DoorX + inwardX, maze.Width),
                Y = Clamp(building.DoorY + inwardY, maze.Height),
                Type = type.Value
            });
        }

        // Market stalls ring the square.
        var (sqx, sqy) = (region.SquareX, region.SquareY);
        int stalls = 4;
        for (int i = 0; i < stalls; i++)
        {
            int angle = i * 90;
            int dx = angle switch { 0 => 1, 180 => -1, _ => 0 };
            int dy = angle switch { 90 => 1, 270 => -1, _ => 0 };
            int stallX = Clamp(sqx + dx * (region.SquareSize / 2 + 1), maze.Width);
            int stallY = Clamp(sqy + dy * (region.SquareSize / 2 + 1), maze.Height);
            if (maze.Tiles[stallX, stallY].IsPassable())
                maze.Features.Add(new MazeFeature { X = stallX, Y = stallY, Type = MazeFeatureType.Stall });
        }

        // Street lamps along the east-west spine road. The spine sits at the plaza's jittered
        // row, not the town's centre row — find the road by scanning outward from centre, and
        // only ever plant a lamp on road (never inside a home, whose floor is also passable).
        for (int lx = town.Left + 6; lx < town.Right; lx += 14)
        {
            for (int r = 0; r <= town.Height / 2; r++)
            {
                bool placed = false;
                foreach (int ly in r == 0 ? new[] { town.CenterY } : new[] { town.CenterY - r, town.CenterY + r })
                {
                    if (ly <= town.Top || ly >= town.Bottom) continue;
                    if (frame.Get(lx, ly) != TileType.Road) continue;
                    var (wx, wy) = frame.ToWorld(lx, ly);
                    if (maze.InBounds(wx, wy))
                        maze.Features.Add(new MazeFeature { X = wx, Y = wy, Type = MazeFeatureType.Lamp });
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }
    }

    private static int Clamp(int value, int limit) => Math.Clamp(value, 0, limit - 1);

    private static void PlantForest(
        Frame frame,
        RegionLayout region,
        int localWidth,
        int localHeight,
        int forestDepth,
        TownRect town,
        Random random)
    {
        var noise = new PerlinNoise(region.Seed + 977);

        for (int lx = 0; lx < localWidth; lx++)
        {
            for (int ly = 0; ly < localHeight; ly++)
            {
                if (frame.Get(lx, ly) != TileType.Floor) continue;

                // Nothing grows within the cleared band; beyond it the wood thickens with distance,
                // so the fringe fades in rather than starting as a wall of trunks.
                int beyond = DistanceFromTown(lx, ly, town) - ClearedBand;
                if (beyond <= 0) continue;

                float density = Math.Min(1f, beyond / (float)Math.Max(1, forestDepth));
                // Perlin returns roughly -1..1; remapped so the whole range contributes rather than
                // half the samples being discarded as negative, which left the "forest" as scatter.
                float sample = ((float)noise.Noise(lx * 0.06, ly * 0.06) + 1f) * 0.5f;
                if (sample * density > 0.28f && random.NextDouble() < 0.92)
                    frame.Set(lx, ly, TileType.Tree);
            }
        }
    }

    /// <summary>Chebyshev distance from the town's walls — 0 inside them.</summary>
    private static int DistanceFromTown(int lx, int ly, TownRect town)
    {
        int dx = Math.Max(Math.Max(town.Left - lx, lx - town.Right), 0);
        int dy = Math.Max(Math.Max(town.Top - ly, ly - town.Bottom), 0);
        return Math.Max(dx, dy);
    }
}

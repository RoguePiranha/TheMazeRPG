using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>Which edge of the region the mountain occupies. The dungeon entrance is set into it,
/// and the river runs on the opposite side (Starting Region.md).</summary>
public enum RegionEdge
{
    West,
    East,
    North,
    South
}

/// <summary>What a generated building is for. Drives its prefab footprint, its POI feature, and
/// later its occupants' occupations (note 09).</summary>
public enum BuildingKind
{
    Home,
    Smithy,
    Alchemist,
    Carpenter,
    Temple,
    TrainingSchool,
    GuardStation,
    Tavern,
    WaterTower,
    MarketStall
}

/// <summary>
/// A building footprint on the region map. Walls are carved into the terrain grid; the interior is
/// floor, reachable through a single door on the road-facing side. Interiors live on the same map
/// (owner ruling) rather than in a sub-map, so this is a rect on the world, not a separate space.
/// </summary>
public sealed class Building
{
    public int Id { get; init; }
    public BuildingKind Kind { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>The one-tile entrance in the outer wall.</summary>
    public int DoorX { get; set; }
    public int DoorY { get; set; }

    /// <summary>A standable tile inside the building — proof it's a room you can be in rather than
    /// a solid block, and the anchor NPCs and furnishings will hang off later.</summary>
    public int InteriorX { get; set; }
    public int InteriorY { get; set; }

    /// <summary>Set on the single authored home that hides the trapdoor (inert until the
    /// dark-temple pass).</summary>
    public bool HasTrapdoor { get; set; }

    public int Right => X + Width - 1;
    public int Bottom => Y + Height - 1;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    /// <summary>Interior cells only — the walls are the rect's border.</summary>
    public bool ContainsInterior(int x, int y) =>
        x > X && x < Right && y > Y && y < Bottom;

    public bool Contains(int x, int y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>A gap in the wall ring where a road leaves town.</summary>
public sealed class TownGate
{
    public int X { get; init; }
    public int Y { get; init; }
    public RegionEdge Side { get; init; }
}

/// <summary>
/// Structural description of a generated region, alongside the terrain grid on <see cref="Maze"/>.
/// The dungeon's equivalent is DungeonLayout; a region is a different vocabulary (walls, gates,
/// roads, buildings, river) so it gets its own record rather than overloading that one.
/// </summary>
public sealed class RegionLayout
{
    public int Seed { get; set; }
    public WorldSize Size { get; set; } = WorldSize.Medium;

    /// <summary>Which edge holds the mountain (and therefore the dungeon mouth).</summary>
    public RegionEdge MountainEdge { get; set; } = RegionEdge.West;

    /// <summary>The walled town's outer rect, inclusive.</summary>
    public int WallLeft { get; set; }
    public int WallTop { get; set; }
    public int WallRight { get; set; }
    public int WallBottom { get; set; }

    public List<TownGate> Gates { get; } = new();
    public List<Building> Buildings { get; } = new();

    /// <summary>The plaza at the road crossing; market stalls sit around it.</summary>
    public int SquareX { get; set; }
    public int SquareY { get; set; }
    public int SquareSize { get; set; }

    /// <summary>Water intake (upstream) and sewage outfall (downstream) on the river — the spec
    /// requires the intake to be strictly upstream of the outfall.</summary>
    public int IntakeX { get; set; }
    public int IntakeY { get; set; }
    public int OutfallX { get; set; }
    public int OutfallY { get; set; }

    /// <summary>River flow direction along its axis, so "upstream" is well defined.</summary>
    public bool RiverFlowsPositive { get; set; } = true;

    public bool InsideWalls(int x, int y) =>
        x >= WallLeft && x <= WallRight && y >= WallTop && y <= WallBottom;

    private HashSet<(int x, int y)>? _interiors;

    /// <summary>
    /// Is this cell inside a building? Interiors are plain floor on the terrain grid — the same
    /// tile as the ground outside — so anything that needs to tell "indoors" from "a field" asks
    /// here. Built once and cached: the renderer asks per visible cell, every frame.
    /// </summary>
    public bool IsBuildingInterior(int x, int y)
    {
        if (_interiors == null)
        {
            _interiors = new HashSet<(int x, int y)>();
            foreach (var building in Buildings)
                for (int bx = building.X + 1; bx < building.Right; bx++)
                    for (int by = building.Y + 1; by < building.Bottom; by++)
                        _interiors.Add((bx, by));
        }
        return _interiors.Contains((x, y));
    }
}

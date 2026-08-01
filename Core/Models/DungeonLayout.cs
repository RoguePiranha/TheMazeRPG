using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Semantic tile information for generated dungeon floors. Maze.Walls remains the authoritative
/// collision grid; this layer describes why a walkable tile exists so population and rendering
/// can make decisions based on the generated structure.
/// </summary>
public enum DungeonTileType
{
    Wall,
    RoomFloor,
    CorridorFloor,
    Doorway
}

public enum DungeonPassageOrientation
{
    EastWest,
    NorthSouth
}

public enum DungeonTheme
{
    Castle,
    Sewer,
    Cemetery,
    Library,
    Forge,
    Hideout
}

public enum DungeonRoomRole
{
    Entrance,
    Standard,
    Treasure,
    Hazard,
    Exit
}

public enum DungeonRoomArchetype
{
    EntranceHall,
    GuardPost,
    Barracks,
    Lair,
    Vault,
    TrapGallery,
    AbandonedCamp,
    StoreRoom,
    ExitChamber
}

public enum DungeonDecorationType
{
    Rubble,
    Bones,
    Crate,
    Barrel,
    Bedroll,
    Banner,
    Brazier,
    Mushrooms,
    BrokenTable,
    Rune,
    Campfire,
    WeaponRack
}

public enum DungeonEncounterKind
{
    Sentries,
    Garrison,
    HuntingPack,
    Ambush,
    Gatekeepers
}

public enum DungeonThemeFeatureType
{
    CastleAlarm,
    SewerRunoff,
    RestlessGrave,
    ArcaneWard,
    HeatVent,
    HideoutTripwire
}

public sealed class DungeonRoom
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DungeonRoomRole Role { get; set; } = DungeonRoomRole.Standard;
    public DungeonRoomArchetype Archetype { get; set; } = DungeonRoomArchetype.StoreRoom;

    public int Right => X + Width - 1;
    public int Bottom => Y + Height - 1;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    public bool Contains(int x, int y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

public sealed class DungeonConnection
{
    public int FromRoomId { get; init; }
    public int ToRoomId { get; init; }
    public bool IsLoop { get; init; }
}

public sealed class DungeonDecoration
{
    public int X { get; init; }
    public int Y { get; init; }
    public int RoomId { get; init; }
    public DungeonDecorationType Type { get; init; }
    public int Variant { get; init; }
}

public sealed class DungeonEncounter
{
    public int Id { get; init; }
    public int HomeRoomId { get; init; }
    public DungeonEncounterKind Kind { get; init; }
    public string Race { get; init; } = "Human";
    public int MemberCount { get; set; }
    public List<int> PatrolRoomIds { get; } = new();
}

public sealed class DungeonThemeFeature
{
    public int X { get; init; }
    public int Y { get; init; }
    public int RoomId { get; init; }
    public DungeonThemeFeatureType Type { get; init; }
    public bool IsTriggered { get; set; }
    public int CooldownTicks { get; set; }
}

public sealed class DungeonLayout
{
    public DungeonTheme Theme { get; set; }
    public DungeonTileType[,] Tiles { get; }
    public int[,] RegionIds { get; }
    public List<DungeonRoom> Rooms { get; } = new();
    public List<DungeonConnection> Connections { get; } = new();
    public List<DungeonDecoration> Decorations { get; } = new();
    public List<DungeonEncounter> Encounters { get; } = new();
    public List<DungeonThemeFeature> ThemeFeatures { get; } = new();

    public int EntranceX { get; set; } = 1;
    public int EntranceY { get; set; } = 1;
    public int ExitX { get; set; }
    public int ExitY { get; set; }

    public DungeonLayout(int width, int height)
    {
        Tiles = new DungeonTileType[width, height];
        RegionIds = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                RegionIds[x, y] = -1;
            }
        }
    }

    public DungeonRoom? RoomAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= RegionIds.GetLength(0) || y >= RegionIds.GetLength(1))
            return null;

        int roomId = RegionIds[x, y];
        return roomId >= 0 && roomId < Rooms.Count ? Rooms[roomId] : null;
    }

    public DungeonPassageOrientation DoorwayOrientationAt(int x, int y)
    {
        bool roomEastWest = IsRoomFloor(x - 1, y) || IsRoomFloor(x + 1, y);
        bool roomNorthSouth = IsRoomFloor(x, y - 1) || IsRoomFloor(x, y + 1);
        if (roomEastWest != roomNorthSouth)
            return roomEastWest
                ? DungeonPassageOrientation.EastWest
                : DungeonPassageOrientation.NorthSouth;

        bool openEastWest = IsWalkable(x - 1, y) && IsWalkable(x + 1, y);
        return openEastWest
            ? DungeonPassageOrientation.EastWest
            : DungeonPassageOrientation.NorthSouth;
    }

    private bool IsRoomFloor(int x, int y) =>
        InBounds(x, y) && Tiles[x, y] == DungeonTileType.RoomFloor;

    private bool IsWalkable(int x, int y) =>
        InBounds(x, y) && Tiles[x, y] != DungeonTileType.Wall;

    private bool InBounds(int x, int y) =>
        x >= 0 && y >= 0 && x < Tiles.GetLength(0) && y < Tiles.GetLength(1);
}

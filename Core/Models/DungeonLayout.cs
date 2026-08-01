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

public enum DungeonRoomRole
{
    Entrance,
    Standard,
    Treasure,
    Hazard,
    Exit
}

public sealed class DungeonRoom
{
    public int Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DungeonRoomRole Role { get; set; } = DungeonRoomRole.Standard;

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

public sealed class DungeonLayout
{
    public DungeonTileType[,] Tiles { get; }
    public int[,] RegionIds { get; }
    public List<DungeonRoom> Rooms { get; } = new();
    public List<DungeonConnection> Connections { get; } = new();

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
}

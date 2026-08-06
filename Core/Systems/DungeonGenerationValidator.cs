using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>Structural checks applied before a generated dungeon floor is accepted.</summary>
public static class DungeonGenerationValidator
{
    public static IReadOnlyList<string> Validate(Maze maze)
    {
        var errors = new List<string>();
        var layout = maze.Dungeon;
        if (layout == null)
        {
            errors.Add("Dungeon layout metadata is missing");
            return errors;
        }

        if (!maze.IsWalkable(layout.EntranceX, layout.EntranceY))
            errors.Add("Entrance is not walkable");

        for (int x = 0; x < maze.Width; x++)
        {
            if (!maze.Walls[x, 0] || !maze.Walls[x, maze.Height - 1])
                errors.Add($"Open tile on north/south boundary at x={x}");

            for (int y = 0; y < maze.Height; y++)
            {
                bool semanticWall = layout.Tiles[x, y] == DungeonTileType.Wall;
                // Compare against traversability rather than the blocking bitmap: a shut door
                // legitimately blocks movement while being semantically open ground.
                if (maze.IsTraversable(x, y) == semanticWall)
                    errors.Add($"Collision and semantic tiles disagree at ({x},{y})");
            }
        }
        for (int y = 0; y < maze.Height; y++)
        {
            if (!maze.Walls[0, y] || !maze.Walls[maze.Width - 1, y])
                errors.Add($"Open tile on east/west boundary at y={y}");
        }

        int minimumRooms = maze.Width * maze.Height >= 600 ? 4 : 3;
        if (layout.Rooms.Count < minimumRooms)
            errors.Add($"Only {layout.Rooms.Count} rooms were placed");

        for (int index = 0; index < layout.Rooms.Count; index++)
        {
            var room = layout.Rooms[index];
            if (room.Id != index)
                errors.Add($"Room {index} has non-contiguous id {room.Id}");
            if (room.X < 1 || room.Y < 1 || room.Right >= maze.Width - 1 || room.Bottom >= maze.Height - 1)
            {
                errors.Add($"Room {room.Id} extends outside the dungeon boundary");
                continue;
            }
            if (!maze.IsWalkable(room.CenterX, room.CenterY))
                errors.Add($"Room {room.Id} center is not walkable");

            var expectedArchetype = room.Role switch
            {
                DungeonRoomRole.Entrance => DungeonRoomArchetype.EntranceHall,
                DungeonRoomRole.Exit => DungeonRoomArchetype.ExitChamber,
                DungeonRoomRole.Treasure => DungeonRoomArchetype.Vault,
                DungeonRoomRole.Hazard => DungeonRoomArchetype.TrapGallery,
                _ => room.Archetype
            };
            if (room.Archetype != expectedArchetype)
                errors.Add($"Room {room.Id} role {room.Role} has incompatible archetype {room.Archetype}");

            for (int x = room.X; x <= room.Right; x++)
            {
                for (int y = room.Y; y <= room.Bottom; y++)
                {
                    if (maze.Walls[x, y] || layout.RegionIds[x, y] != room.Id)
                        errors.Add($"Room {room.Id} has invalid interior tile ({x},{y})");
                }
            }
        }

        // Connectivity is measured over traversable ground (doors included, since you open them),
        // which is the same graph BfsDistancesFrom walks — counting only currently-walkable tiles
        // would compare two different graphs and report a phantom mismatch.
        int traversableCount = maze.GetTraversableCells().Count;
        var distances = maze.BfsDistancesFrom(layout.EntranceX, layout.EntranceY);
        if (distances.Count != traversableCount)
            errors.Add($"Only {distances.Count} of {traversableCount} traversable tiles are connected");

        var exitRooms = layout.Rooms.Where(room => room.Role == DungeonRoomRole.Exit).ToList();
        if (exitRooms.Count != 1)
        {
            errors.Add($"Exactly one exit room is required (found {exitRooms.Count})");
        }
        else
        {
            var exitRoom = exitRooms[0];
            if (!exitRoom.Contains(layout.ExitX, layout.ExitY))
                errors.Add("Exit coordinates are outside the exit room");
            int exitDistance = distances.GetValueOrDefault((layout.ExitX, layout.ExitY), -1);
            int minimumDistance = Math.Max(8, (maze.Width + maze.Height) / 4);
            if (exitDistance < minimumDistance)
                errors.Add($"Exit path is too short ({exitDistance}, expected at least {minimumDistance})");
        }

        if (layout.Connections.Count < Math.Max(0, layout.Rooms.Count - 1))
            errors.Add("Room connection graph is incomplete");
        if (layout.Rooms.Count > 2 && layout.Connections.All(connection => !connection.IsLoop))
            errors.Add("Dungeon has no alternate room connection");
        if (!layout.Tiles.Cast<DungeonTileType>().Any(tile =>
                tile is DungeonTileType.CorridorFloor or DungeonTileType.Doorway))
            errors.Add("Dungeon has no corridor tiles");

        for (int x = 1; x < maze.Width - 1; x++)
        {
            for (int y = 1; y < maze.Height - 1; y++)
            {
                if (layout.Tiles[x, y] != DungeonTileType.Doorway) continue;
                bool besideRoom = layout.Tiles[x - 1, y] == DungeonTileType.RoomFloor ||
                                  layout.Tiles[x + 1, y] == DungeonTileType.RoomFloor ||
                                  layout.Tiles[x, y - 1] == DungeonTileType.RoomFloor ||
                                  layout.Tiles[x, y + 1] == DungeonTileType.RoomFloor;
                // A doorway should hold an actual door (or bare floor, where a door wasn't placed) —
                // never solid wall.
                if (!besideRoom || !maze.IsTraversable(x, y))
                    errors.Add($"Invalid doorway at ({x},{y})");
            }
        }

        var decorationCells = new HashSet<(int x, int y)>();
        foreach (var decoration in layout.Decorations)
        {
            var room = layout.RoomAt(decoration.X, decoration.Y);
            if (room?.Id != decoration.RoomId)
                errors.Add($"Decoration at ({decoration.X},{decoration.Y}) is outside room {decoration.RoomId}");
            if (!decorationCells.Add((decoration.X, decoration.Y)))
                errors.Add($"Multiple decorations occupy ({decoration.X},{decoration.Y})");
        }
        if (layout.Decorations.Count < layout.Rooms.Count)
            errors.Add("Not every room received environmental dressing");

        var expectedThemeFeature = layout.Theme switch
        {
            DungeonTheme.Castle => DungeonThemeFeatureType.CastleAlarm,
            DungeonTheme.Sewer => DungeonThemeFeatureType.SewerRunoff,
            DungeonTheme.Cemetery => DungeonThemeFeatureType.RestlessGrave,
            DungeonTheme.Library => DungeonThemeFeatureType.ArcaneWard,
            DungeonTheme.Forge => DungeonThemeFeatureType.HeatVent,
            _ => DungeonThemeFeatureType.HideoutTripwire
        };
        if (layout.Rooms.Any(room => room.Role == DungeonRoomRole.Standard) && layout.ThemeFeatures.Count != 1)
            errors.Add($"Theme {layout.Theme} requires exactly one environmental feature");
        foreach (var feature in layout.ThemeFeatures)
        {
            var room = layout.RoomAt(feature.X, feature.Y);
            if (room?.Id != feature.RoomId)
                errors.Add($"Theme feature at ({feature.X},{feature.Y}) is outside room {feature.RoomId}");
            if (feature.Type != expectedThemeFeature)
                errors.Add($"Theme {layout.Theme} has incompatible feature {feature.Type}");
            if (decorationCells.Contains((feature.X, feature.Y)))
                errors.Add($"Theme feature overlaps a decoration at ({feature.X},{feature.Y})");
        }

        return errors;
    }
}

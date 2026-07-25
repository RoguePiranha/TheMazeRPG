using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// The first Overworld region (see Info/Starting Region.md): a small, hand-authored, fixed
/// layout — not procedurally generated at runtime. Reuses Maze/MazeFeature exactly like the
/// dungeon's safe room does, just with a different, larger, permanent layout. This vertical
/// slice models only 4 points of interest (dungeon entrance, mine, smithy, stall); the fuller
/// town (walls, houses, temple, dark-temple tunnel, population) is explicitly later work.
/// </summary>
public static class OverworldGenerator
{
    public const int Width = 31;
    public const int Height = 15;

    public static Maze Generate()
    {
        var maze = new Maze(Width, Height) { FloorNumber = 0 }; // not a dungeon floor; unused elsewhere

        // Open field, walled only at the border (same simple approach as the safe room).
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                bool isBorder = x == 0 || x == Width - 1 || y == 0 || y == Height - 1;
                maze.Walls[x, y] = isBorder;
                if (!isBorder) maze.Explored[x, y] = true; // fully lit; this is not a fog-of-war space
            }
        }

        // Dungeon entrance on the "mountain side" (left edge, per Starting Region.md), mine
        // entrance just further into town from it, smithy/stall toward the town-square side.
        maze.Features.Add(new MazeFeature { X = 2, Y = 7, Type = MazeFeatureType.DungeonEntrance });
        maze.Features.Add(new MazeFeature { X = 7, Y = 7, Type = MazeFeatureType.MineEntrance });
        maze.Features.Add(new MazeFeature { X = 20, Y = 4, Type = MazeFeatureType.Smithy });
        maze.Features.Add(new MazeFeature { X = 20, Y = 10, Type = MazeFeatureType.Stall });

        return maze;
    }
}

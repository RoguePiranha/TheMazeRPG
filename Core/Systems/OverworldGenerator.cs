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

    /// <summary>
    /// Build the town. Dimensions are parameters rather than constants so the renderer's camera
    /// culling can be exercised at the scale a real region actually needs — one tile is roughly one
    /// metre (dungeon corridors are a tile wide), so the 31x15 default is about 31m x 15m, and
    /// Starting Region.md's 100m cleared band alone needs hundreds of tiles per axis. The real
    /// layout arrives with the region generator; POIs are placed proportionally here so a larger
    /// canvas still produces a walkable, interactable town to measure and look at.
    /// </summary>
    public static Maze Generate(int width = Width, int height = Height)
    {
        width = System.Math.Max(9, width);
        height = System.Math.Max(9, height);
        var maze = new Maze(width, height) { FloorNumber = 0 }; // not a dungeon floor; unused elsewhere

        // Open field, walled only at the border (same simple approach as the safe room).
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                maze.Walls[x, y] = isBorder;
                if (!isBorder) maze.Explored[x, y] = true; // fully lit; this is not a fog-of-war space
            }
        }

        // Fractions chosen to reproduce the original hand-placed 31x15 coordinates exactly, so the
        // default town is unchanged while a scaled one stays sensibly laid out.
        int Fx(float fraction) => System.Math.Clamp((int)(width * fraction), 1, width - 2);
        int Fy(float fraction) => System.Math.Clamp((int)(height * fraction), 1, height - 2);

        // Dungeon entrance on the "mountain side" (left edge, per Starting Region.md), mine
        // entrance just further into town from it, smithy/stall toward the town-square side.
        maze.Features.Add(new MazeFeature { X = Fx(0.065f), Y = Fy(0.5f), Type = MazeFeatureType.DungeonEntrance });
        maze.Features.Add(new MazeFeature { X = Fx(0.23f), Y = Fy(0.5f), Type = MazeFeatureType.MineEntrance });
        maze.Features.Add(new MazeFeature { X = Fx(0.65f), Y = Fy(0.3f), Type = MazeFeatureType.Smithy });
        maze.Features.Add(new MazeFeature { X = Fx(0.65f), Y = Fy(0.7f), Type = MazeFeatureType.Stall });

        // Street lamps (owner ruling 2026-08-05: night should be night — lit only in pools).
        // One by the dungeon mouth (the guard station's brazier, eventually), one at the mine
        // path, two flanking the smithy/stall lane, one mid-field on the walk between them.
        maze.Features.Add(new MazeFeature { X = Fx(0.13f), Y = Fy(0.36f), Type = MazeFeatureType.Lamp });
        maze.Features.Add(new MazeFeature { X = Fx(0.3f), Y = Fy(0.63f), Type = MazeFeatureType.Lamp });
        maze.Features.Add(new MazeFeature { X = Fx(0.47f), Y = Fy(0.5f), Type = MazeFeatureType.Lamp });
        maze.Features.Add(new MazeFeature { X = Fx(0.72f), Y = Fy(0.3f), Type = MazeFeatureType.Lamp });
        maze.Features.Add(new MazeFeature { X = Fx(0.72f), Y = Fy(0.7f), Type = MazeFeatureType.Lamp });

        return maze;
    }
}

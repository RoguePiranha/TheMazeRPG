using System;
using System.Linq;
using TheMazeRPG.Core.Systems;

namespace TheMazeRPG;

/// <summary>
/// Simple console test to visualize maze generation
/// </summary>
public static class MazeTest
{
    public static void PrintMaze(int seed = 12345)
    {
        var generator = new MazeGenerator(seed);
        var maze = generator.Generate(41, 31, 1);
        
        Console.WriteLine($"Maze {maze.Width}x{maze.Height} - Floor {maze.FloorNumber}");
        Console.WriteLine($"Features: {maze.Features.Count}");
        Console.WriteLine();
        
        for (int y = 0; y < maze.Height; y++)
        {
            for (int x = 0; x < maze.Width; x++)
            {
                if (x == 1 && y == 1)
                    Console.Write('S'); // Start
                else if (maze.Features.Any(f => f.X == x && f.Y == y && f.Type == Core.Models.MazeFeatureType.Stairs))
                    Console.Write('E'); // Exit/Stairs
                else if (maze.Features.Any(f => f.X == x && f.Y == y && f.Type == Core.Models.MazeFeatureType.Chest))
                    Console.Write('C'); // Chest
                else if (maze.Walls[x, y])
                    Console.Write('█'); // Wall
                else
                    Console.Write(' '); // Path
            }
            Console.WriteLine();
        }
        
        Console.WriteLine($"\nEmpty cells: {maze.GetEmptyCells().Count}");
        Console.WriteLine($"Stairs at: ({maze.Features.First(f => f.Type == Core.Models.MazeFeatureType.Stairs).X}, {maze.Features.First(f => f.Type == Core.Models.MazeFeatureType.Stairs).Y})");
    }
}

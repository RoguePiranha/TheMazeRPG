using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents a single maze floor
/// </summary>
public class Maze
{
    public int Width { get; set; } = 41;
    public int Height { get; set; } = 31;
    public int FloorNumber { get; set; } = 1;
    
    // 2D grid: true = wall, false = open passage
    public bool[,] Walls { get; set; }
    
    // Track explored cells
    public bool[,] Explored { get; set; }
    
    // Special features in the maze
    public List<MazeFeature> Features { get; set; } = new();
    
    public Maze(int width, int height)
    {
        Width = width;
        Height = height;
        Walls = new bool[width, height];
        Explored = new bool[width, height];
    }
    
    /// <summary>
    /// Get all empty (non-wall) cells
    /// </summary>
    public List<(int x, int y)> GetEmptyCells()
    {
        var cells = new List<(int x, int y)>();
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (!Walls[x, y])
                {
                    cells.Add((x, y));
                }
            }
        }
        return cells;
    }
    
    /// <summary>
    /// Check if a position is walkable
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
        return !Walls[x, y];
    }
}

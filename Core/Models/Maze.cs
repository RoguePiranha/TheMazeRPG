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

    /// <summary>
    /// BFS distance in steps (via the actual walkable maze graph, not straight-line) from a start
    /// cell to every reachable cell. Cells not reachable from the start are absent from the map.
    /// Used to place the exit genuinely far from the entrance (real maze-solving distance).
    /// </summary>
    public Dictionary<(int x, int y), int> BfsDistancesFrom(int startX, int startY)
    {
        var distances = new Dictionary<(int x, int y), int> { [(startX, startY)] = 0 };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int d = distances[(x, y)];
            foreach (var (dx, dy) in directions)
            {
                var next = (x + dx, y + dy);
                if (IsWalkable(next.Item1, next.Item2) && !distances.ContainsKey(next))
                {
                    distances[next] = d + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return distances;
    }
}

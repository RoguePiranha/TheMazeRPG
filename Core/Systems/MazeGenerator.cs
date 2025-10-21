using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Generates procedural mazes using randomized depth-first search
/// </summary>
public class MazeGenerator
{
    private readonly Random _random;
    
    public MazeGenerator(int seed)
    {
        _random = new Random(seed);
    }
    
    public Maze Generate(int width, int height, int floorNumber)
    {
        // Ensure odd dimensions for proper DFS maze generation
        if (width % 2 == 0) width--;
        if (height % 2 == 0) height--;
        
        var maze = new Maze(width, height) { FloorNumber = floorNumber };
        
        // Initialize all cells as walls
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                maze.Walls[x, y] = true;
            }
        }
        
        // Start carving from (1, 1)
        CarveMaze(maze, 1, 1);
        
        // Ensure start and end are clear
        maze.Walls[1, 1] = false; // Start position
        
        // Place stairs at a far corner
        int stairsX = width - 2;
        int stairsY = height - 2;
        maze.Walls[stairsX, stairsY] = false;
        
        maze.Features.Add(new MazeFeature
        {
            X = stairsX,
            Y = stairsY,
            Type = MazeFeatureType.Stairs
        });
        
        // Add some random treasure chests (1-3 per floor)
        int chestCount = 1 + floorNumber / 3;
        var emptyCells = maze.GetEmptyCells();
        
        for (int i = 0; i < chestCount && emptyCells.Count > 0; i++)
        {
            int idx = _random.Next(emptyCells.Count);
            var (x, y) = emptyCells[idx];
            emptyCells.RemoveAt(idx);
            
            // Don't place on start or stairs
            if ((x == 1 && y == 1) || (x == stairsX && y == stairsY))
                continue;
                
            maze.Features.Add(new MazeFeature
            {
                X = x,
                Y = y,
                Type = MazeFeatureType.Chest
            });
        }
        
        return maze;
    }
    
    private void CarveMaze(Maze maze, int x, int y)
    {
        // Carve out this cell
        maze.Walls[x, y] = false;
        
        // Define directions: North, South, East, West
        var directions = new List<(int dx, int dy)>
        {
            (0, -2),  // North
            (0, 2),   // South
            (2, 0),   // East
            (-2, 0)   // West
        };
        
        // Shuffle directions for randomization
        Shuffle(directions);
        
        // Try each direction
        foreach (var (dx, dy) in directions)
        {
            int newX = x + dx;
            int newY = y + dy;
            
            // Check if the new position is valid and unvisited
            if (IsInBounds(maze, newX, newY) && maze.Walls[newX, newY])
            {
                // Carve the wall between current cell and new cell
                maze.Walls[x + dx / 2, y + dy / 2] = false;
                
                // Recursively carve from the new cell
                CarveMaze(maze, newX, newY);
            }
        }
    }
    
    private bool IsInBounds(Maze maze, int x, int y)
    {
        return x > 0 && x < maze.Width - 1 && y > 0 && y < maze.Height - 1;
    }
    
    private void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}

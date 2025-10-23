using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Handles hero pathfinding and enemy movement
/// </summary>
public class MovementSystem
{
    private readonly PerlinNoise _noise;
    private readonly Random _random;
    
    public MovementSystem(int seed)
    {
        _noise = new PerlinNoise(seed);
        _random = new Random(seed);
    }
    
    /// <summary>
    /// Move enemy using inertia-based smooth random walk (no Perlin)
    /// </summary>
    public void MoveEnemySmoothRandom(Enemy enemy, Maze maze)
    {
        // Use persistent velocity with random direction changes
        if (enemy == null) return;
        if (enemy.TempData == null) enemy.TempData = new Dictionary<string, object>();
        float speed = 0.07f + enemy.Agility * 0.008f;
        // Initialize velocity if not present
        if (!enemy.TempData.ContainsKey("vx")) enemy.TempData["vx"] = (float)(_random.NextDouble() * 2 - 1) * speed;
        if (!enemy.TempData.ContainsKey("vy")) enemy.TempData["vy"] = (float)(_random.NextDouble() * 2 - 1) * speed;
        float vx = (float)enemy.TempData["vx"];
        float vy = (float)enemy.TempData["vy"];
        // Occasionally change direction
        if (_random.NextDouble() < 0.08)
        {
            float angle = (float)(_random.NextDouble() * MathF.PI * 2);
            vx = MathF.Cos(angle) * speed;
            vy = MathF.Sin(angle) * speed;
        }
        // Add inertia (smooth changes)
        vx = vx * 0.85f + ((float)(_random.NextDouble() * 2 - 1) * speed) * 0.15f;
        vy = vy * 0.85f + ((float)(_random.NextDouble() * 2 - 1) * speed) * 0.15f;
        // Wall avoidance
        int lookAheadX = (int)MathF.Round(enemy.X + vx * 2.5f);
        int lookAheadY = (int)MathF.Round(enemy.Y + vy * 2.5f);
        if (!IsWalkable(maze, lookAheadX, lookAheadY))
        {
            vx = -vx * 0.5f;
            vy = -vy * 0.5f;
        }
        float newX = enemy.X + vx;
        float newY = enemy.Y + vy;
        int gridX = (int)MathF.Round(newX);
        int gridY = (int)MathF.Round(newY);
        if (IsWalkable(maze, gridX, gridY))
        {
            enemy.X = newX;
            enemy.Y = newY;
            enemy.TargetX = newX;
            enemy.TargetY = newY;
            enemy.TempData["vx"] = vx;
            enemy.TempData["vy"] = vy;
        }
        else
        {
            // Try random direction if blocked
            float angle = (float)(_random.NextDouble() * MathF.PI * 2);
            vx = MathF.Cos(angle) * speed;
            vy = MathF.Sin(angle) * speed;
            newX = enemy.X + vx;
            newY = enemy.Y + vy;
            gridX = (int)MathF.Round(newX);
            gridY = (int)MathF.Round(newY);
            if (IsWalkable(maze, gridX, gridY))
            {
                enemy.X = newX;
                enemy.Y = newY;
                enemy.TargetX = newX;
                enemy.TargetY = newY;
                enemy.TempData["vx"] = vx;
                enemy.TempData["vy"] = vy;
            }
        }
    }
    
    /// <summary>
    /// Move hero toward enemy during combat to maintain attack range
    /// </summary>
    public void MoveHeroTowardEnemy(Hero hero, Enemy enemy, Maze maze)
    {
        // Get hero's current attack
        var attack = hero.CurrentAttack ?? new Attack { Name = "Unarmed Strike", Range = 1.0f };
        
    // Calculate direction to enemy (use actual enemy position, not their planned target)
    float dx = enemy.X - hero.X;
    float dy = enemy.Y - hero.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        
        if (distance < 0.1f) return; // Already at target
        
        // Determine desired range based on attack type
        float desiredRange = attack.Range * 0.85f; // Stay just inside attack range
        
    // Movement speed scales with Agility
    float baseSpeed = 0.06f;
    float speed = baseSpeed * (1.0f + 0.05f * (hero.Agility - 4));
        
        // If we're too far, use pathfinding to navigate around obstacles
        if (distance > desiredRange + 0.3f)
        {
            // Try to find a path to the enemy
        var path = FindPathToTarget(hero.GridX, hero.GridY, (int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y), maze);
            
            if (path != null && path.Count > 1)
            {
                // Move along the path
                var target = path[1];
                float targetDx = target.x - hero.X;
                float targetDy = target.y - hero.Y;
                float targetDistance = MathF.Sqrt(targetDx * targetDx + targetDy * targetDy);
                
                if (targetDistance > 0.1f)
                {
                    hero.X += (targetDx / targetDistance) * speed;
                    hero.Y += (targetDy / targetDistance) * speed;
                }
                else
                {
                    hero.X = target.x;
                    hero.Y = target.y;
                }
            }
            else
            {
                // No path found, try direct movement
                dx /= distance;
                dy /= distance;
                
                float newX = hero.X + dx * speed;
                float newY = hero.Y + dy * speed;
                
                int gridX = (int)MathF.Round(newX);
                int gridY = (int)MathF.Round(newY);
                
                if (IsWalkable(maze, gridX, gridY))
                {
                    hero.X = newX;
                    hero.Y = newY;
                }
            }
        }
        else if (distance < desiredRange - 0.3f && attack.Range > 1.5f)
        {
            // Too close for ranged - back away slowly
            dx /= distance;
            dy /= distance;
            
            float newX = hero.X - dx * speed * 0.5f;
            float newY = hero.Y - dy * speed * 0.5f;
            
            int gridX = (int)MathF.Round(newX);
            int gridY = (int)MathF.Round(newY);
            
            if (IsWalkable(maze, gridX, gridY))
            {
                hero.X = newX;
                hero.Y = newY;
            }
        }
        
        // Mark current cell as explored
        maze.Explored[hero.GridX, hero.GridY] = true;
    }
    
    /// <summary>
    /// Move enemy toward a target position during combat
    /// </summary>
    public void MoveEnemyTowardTarget(Enemy enemy, float targetX, float targetY, Maze maze)
    {
        // Use BFS pathfinding to follow the hero around obstacles
    int startX = (int)MathF.Round(enemy.X);
    int startY = (int)MathF.Round(enemy.Y);
    // Always use hero's current grid position for pathfinding
    int heroGridX = (int)MathF.Round(targetX);
    int heroGridY = (int)MathF.Round(targetY);

    var path = FindPathToTarget(startX, startY, heroGridX, heroGridY, maze);

        float desiredRange = enemy.AttackRange * 0.9f;
        float baseSpeed = 0.08f;
        float speed = baseSpeed * (1.0f + enemy.Agility * 0.05f);

        if (path != null && path.Count > 1)
        {
            // Move along the path until within attack range
            var next = path[1];
            float dx = next.x - enemy.X;
            float dy = next.y - enemy.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // Only move if not already in attack range
            float distToHero = MathF.Sqrt((heroGridX - enemy.X) * (heroGridX - enemy.X) + (heroGridY - enemy.Y) * (heroGridY - enemy.Y));
            if (distToHero > desiredRange + 0.3f)
            {
                if (distance > 0.1f)
                {
                    enemy.X += (dx / distance) * speed;
                    enemy.Y += (dy / distance) * speed;
                }
                else
                {
                    enemy.X = next.x;
                    enemy.Y = next.y;
                }
            }
            else if (distToHero < desiredRange - 0.3f && enemy.AttackRange > 1.5f)
            {
                // Too close for ranged enemy - back away
                float awayDx = enemy.X - heroGridX;
                float awayDy = enemy.Y - heroGridY;
                float awayDist = MathF.Sqrt(awayDx * awayDx + awayDy * awayDy);
                if (awayDist > 0.1f)
                {
                    // Prevent backing into walls
                    float backX = enemy.X + (awayDx / awayDist) * speed * 0.5f;
                    float backY = enemy.Y + (awayDy / awayDist) * speed * 0.5f;
                    int backGridX = (int)MathF.Round(backX);
                    int backGridY = (int)MathF.Round(backY);
                    if (IsWalkable(maze, backGridX, backGridY))
                    {
                        enemy.X = backX;
                        enemy.Y = backY;
                    }
                    // else: stay put if wall behind
                }
            }
        }
        // Mark current grid cell as explored (optional for AI)
        // maze.Explored[enemy.X, enemy.Y] = true;
    }
    
    /// <summary>
    /// Move hero toward unexplored areas with smooth sub-grid movement
    /// </summary>
    public void MoveHeroTowardUnexplored(Hero hero, Maze maze)
    {
        // Find nearest unexplored cell using BFS
        var path = FindPathToUnexplored(hero.GridX, hero.GridY, maze);
        
        if (path != null && path.Count > 1)
        {
            // Get target position (next step in path)
            var target = path[1];
            
            // Move smoothly toward target
            float dx = target.x - hero.X;
            float dy = target.y - hero.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance > 0.1f)
            {
                // Move a fraction of the way toward target (smooth movement)
                // Movement speed scales with Agility
                float baseSpeed = 0.075f;
                float speed = baseSpeed * (1.0f + 0.05f * (hero.Agility - 4));
                hero.X += (dx / distance) * speed;
                hero.Y += (dy / distance) * speed;
            }
            else
            {
                // Snap to target when close enough
                hero.X = target.x;
                hero.Y = target.y;
            }
            
            // Mark current grid cell as explored
            maze.Explored[hero.GridX, hero.GridY] = true;
        }
    }
    
    /// <summary>
    /// BFS to find path to a specific target
    /// </summary>
    private List<(int x, int y)>? FindPathToTarget(int startX, int startY, int targetX, int targetY, Maze maze)
    {
        if (startX == targetX && startY == targetY)
            return new List<(int x, int y)> { (startX, startY) };
        
        var queue = new Queue<(int x, int y, List<(int x, int y)> path)>();
        var visited = new HashSet<(int, int)>();
        
        queue.Enqueue((startX, startY, new List<(int x, int y)> { (startX, startY) }));
        visited.Add((startX, startY));
        
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        
        while (queue.Count > 0)
        {
            var (x, y, path) = queue.Dequeue();
            
            // Check if we reached the target
            if (x == targetX && y == targetY)
            {
                return path;
            }
            
            // Explore neighbors
            foreach (var (dx, dy) in directions)
            {
                int newX = x + dx;
                int newY = y + dy;
                
                if (IsWalkable(maze, newX, newY) && !visited.Contains((newX, newY)))
                {
                    visited.Add((newX, newY));
                    var newPath = new List<(int x, int y)>(path) { (newX, newY) };
                    queue.Enqueue((newX, newY, newPath));
                }
            }
        }
        
        return null; // No path found
    }
    
    /// <summary>
    /// BFS to find path to nearest unexplored cell
    /// </summary>
    private List<(int x, int y)>? FindPathToUnexplored(int startX, int startY, Maze maze)
    {
        var queue = new Queue<(int x, int y, List<(int x, int y)> path)>();
        var visited = new HashSet<(int, int)>();
        
        queue.Enqueue((startX, startY, new List<(int x, int y)> { (startX, startY) }));
        visited.Add((startX, startY));
        
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        
        while (queue.Count > 0)
        {
            var (x, y, path) = queue.Dequeue();
            
            // Check if we found an unexplored cell
            if (!maze.Explored[x, y])
            {
                return path;
            }
            
            // Explore neighbors
            foreach (var (dx, dy) in directions)
            {
                int newX = x + dx;
                int newY = y + dy;
                
                if (IsWalkable(maze, newX, newY) && !visited.Contains((newX, newY)))
                {
                    visited.Add((newX, newY));
                    var newPath = new List<(int x, int y)>(path) { (newX, newY) };
                    queue.Enqueue((newX, newY, newPath));
                }
            }
        }
        
        // No unexplored cells found - just stay put or move randomly
        return null;
    }
    
    private bool IsWalkable(Maze maze, int x, int y)
    {
        if (x < 0 || x >= maze.Width || y < 0 || y >= maze.Height)
            return false;
        
        return !maze.Walls[x, y];
    }
}

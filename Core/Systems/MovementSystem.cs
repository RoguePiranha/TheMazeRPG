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
        // Waypoint-based wandering using BFS to reduce jitter and wall shuffling
        if (enemy == null) return;
        if (enemy.TempData == null) enemy.TempData = new Dictionary<string, object>();

        int enemyGX = (int)MathF.Round(enemy.X);
        int enemyGY = (int)MathF.Round(enemy.Y);
        int recalcIn = enemy.TempData.TryGetValue("wanderRecalc", out var recalcObj) ? (int)recalcObj : 0;
        int targetX = enemy.TempData.TryGetValue("wanderTargetX", out var txObj) ? (int)txObj : -1;
        int targetY = enemy.TempData.TryGetValue("wanderTargetY", out var tyObj) ? (int)tyObj : -1;

        bool needNewTarget = recalcIn <= 0 || targetX < 0 || targetY < 0 || !IsWalkable(maze, targetX, targetY);
        if (needNewTarget)
        {
            var cell = FindRandomReachableCell(enemyGX, enemyGY, maze, 4, 10);
            if (cell.HasValue)
            {
                targetX = cell.Value.x;
                targetY = cell.Value.y;
                enemy.TempData["wanderTargetX"] = targetX;
                enemy.TempData["wanderTargetY"] = targetY;
                enemy.TempData["wanderRecalc"] = _random.Next(60, 180); // 6-18s at 10 tps
            }
            else
            {
                // fallback: small random nudge area
                targetX = enemyGX + _random.Next(-2, 3);
                targetY = enemyGY + _random.Next(-2, 3);
                enemy.TempData["wanderTargetX"] = targetX;
                enemy.TempData["wanderTargetY"] = targetY;
                enemy.TempData["wanderRecalc"] = 40;
            }
        }
        else
        {
            enemy.TempData["wanderRecalc"] = recalcIn - 1;
        }

        if (enemyGX == targetX && enemyGY == targetY)
        {
            // Arrived; pick a new target soon
            enemy.TempData["wanderRecalc"] = 0;
            return;
        }

        var path = FindPathToTarget(enemyGX, enemyGY, targetX, targetY, maze);
        if (path == null || path.Count <= 1)
        {
            // Can't path; try new target next tick
            enemy.TempData["wanderRecalc"] = 0;
            return;
        }

        var next = path[1];
        float dx = next.x - enemy.X;
        float dy = next.y - enemy.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 0.05f)
        {
            float baseSpeed = 0.06f;
            float speed = baseSpeed * (1.0f + enemy.Agility * 0.04f);
            enemy.X += (dx / dist) * speed;
            enemy.Y += (dy / dist) * speed;
        }
        else
        {
            enemy.X = next.x;
            enemy.Y = next.y;
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
        
        // Determine desired range based on attack type - aim just inside the attack range
        // Use a small fixed offset so we don't stop short due to rounding/path granularity
        float desiredRange = attack.Range - 0.15f;
        if (desiredRange < 0.5f) desiredRange = MathF.Min(attack.Range * 0.75f, 0.5f); // clamp to reasonable minimum

        // If we're already within the actual attack range, check if we have line of sight
        // For ranged attacks, keep moving if blocked by walls even if in range
        // This ensures the hero will navigate around corners to get a clear shot
        // Use a small buffer (0.05f less than attack range) to ensure we're definitely in range
        // Exception: for ranged attacks that are too close we'll handle backing away below.
        if (distance <= attack.Range - 0.05f && !(attack.Range > 1.5f && distance < desiredRange - 0.3f))
        {
            // For ranged attacks beyond melee range, verify line of sight before stopping
            bool isMeleeRange = distance <= 1.5f;
            // LOS is priority for all attacks; allow a stop only if LOS is clear or bodies overlap
            bool overlappingBodies = distance <= (hero.Radius + enemy.Radius + 0.05f);
            bool needsLOS = true; // always require LOS to stop, except when overlapping
            
            if (!needsLOS || overlappingBodies || CheckLineOfSight(hero.X, hero.Y, enemy.X, enemy.Y, maze))
            {
                // Mark current cell as explored and stop movement
                maze.Explored[hero.GridX, hero.GridY] = true;
                return;
            }
            // If we're in range but blocked, continue moving to get LOS
        }

    // Movement speed scales with Agility
    float baseSpeed = 0.06f;
    float speed = baseSpeed * (1.0f + 0.05f * (hero.Agility - 4));
        
        // If we're too far, use pathfinding to navigate around obstacles
        if (distance > desiredRange)
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
            // Too close for ranged. Normalize direction toward the enemy.
            dx /= distance;
            dy /= distance;

            if (!CheckLineOfSight(hero.X, hero.Y, enemy.X, enemy.Y, maze))
            {
                // No clean shot (e.g. kited back against a wall) - close in to regain
                // line of sight rather than staying pinned with no line to the target.
                float newX = hero.X + dx * speed;
                float newY = hero.Y + dy * speed;
                if (IsWalkable(maze, (int)MathF.Round(newX), (int)MathF.Round(newY)))
                {
                    hero.X = newX;
                    hero.Y = newY;
                }
            }
            else
            {
                // Kite backward, but only if the new spot keeps a walkable cell AND line of sight.
                float newX = hero.X - dx * speed * 0.5f;
                float newY = hero.Y - dy * speed * 0.5f;
                if (IsWalkable(maze, (int)MathF.Round(newX), (int)MathF.Round(newY))
                    && CheckLineOfSight(newX, newY, enemy.X, enemy.Y, maze))
                {
                    hero.X = newX;
                    hero.Y = newY;
                }
            }
        }
        
        // Mark current cell as explored
        maze.Explored[hero.GridX, hero.GridY] = true;
    }
    
    /// <summary>
    /// Move hero toward a specific target location (e.g., stairs after getting key)
    /// </summary>
    public void MoveHeroTowardTarget(Hero hero, float targetX, float targetY, Maze maze)
    {
        // Use pathfinding to navigate around obstacles
        int targetGridX = (int)MathF.Round(targetX);
        int targetGridY = (int)MathF.Round(targetY);
        
        var path = FindPathToTarget(hero.GridX, hero.GridY, targetGridX, targetGridY, maze);
        
        if (path != null && path.Count > 1)
        {
            // Move along the path
            var nextStep = path[1];
            float dx = nextStep.x - hero.X;
            float dy = nextStep.y - hero.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            
            if (distance > 0.1f)
            {
                float baseSpeed = 0.08f;
                float speed = baseSpeed * (1.0f + 0.05f * (hero.Agility - 4));
                hero.X += (dx / distance) * speed;
                hero.Y += (dy / distance) * speed;
            }
            else
            {
                hero.X = nextStep.x;
                hero.Y = nextStep.y;
            }

            // If we're very close to the final target cell, snap to center to ensure interactions trigger
            if (MathF.Abs(hero.X - targetGridX) < 0.35f && MathF.Abs(hero.Y - targetGridY) < 0.35f)
            {
                hero.X = targetGridX;
                hero.Y = targetGridY;
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

    // Enemy desired range: aim slightly inside their attack range to ensure they close the distance
    float desiredRange = enemy.AttackRange - 0.15f;
    if (desiredRange < 0.5f) desiredRange = MathF.Min(enemy.AttackRange * 0.75f, 0.5f);
        float baseSpeed = 0.08f;
        float speed = baseSpeed * (1.0f + enemy.Agility * 0.05f);
        
        float distToHero = MathF.Sqrt((targetX - enemy.X) * (targetX - enemy.X) + (targetY - enemy.Y) * (targetY - enemy.Y));

        if (path != null && path.Count > 1)
        {
            // Move along the path until within attack range
            var next = path[1];
            float dx = next.x - enemy.X;
            float dy = next.y - enemy.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // Melee enemies: always close the distance if not in range
            // Ranged enemies: maintain optimal range
            if (enemy.AttackRange <= 1.5f)
            {
                // Melee - keep moving closer until in range
                if (distToHero > desiredRange)
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
            }
            else
            {
                // Ranged - prefer to gain LOS and be just inside range
                bool hasLOS = CheckLineOfSight(enemy.X, enemy.Y, targetX, targetY, maze);
                if (!hasLOS || distToHero > desiredRange + 0.4f)
                {
                    // Advance to gain LOS or close distance
                    if (distance > 0.1f)
                    {
                        enemy.X += (dx / distance) * speed * 0.75f;
                        enemy.Y += (dy / distance) * speed * 0.75f;
                    }
                    else
                    {
                        enemy.X = next.x;
                        enemy.Y = next.y;
                    }
                }
                else if (distToHero < desiredRange - 0.35f && hasLOS)
                {
                    // Too close and we can shoot - back away (kiting)
                    float awayDx = enemy.X - targetX;
                    float awayDy = enemy.Y - targetY;
                    float awayDist = MathF.Sqrt(awayDx * awayDx + awayDy * awayDy);
                    if (awayDist > 0.1f)
                    {
                        float backX = enemy.X + (awayDx / awayDist) * speed * 0.55f;
                        float backY = enemy.Y + (awayDy / awayDist) * speed * 0.55f;
                        int backGridX = (int)MathF.Round(backX);
                        int backGridY = (int)MathF.Round(backY);
                        if (IsWalkable(maze, backGridX, backGridY))
                        {
                            enemy.X = backX;
                            enemy.Y = backY;
                        }
                    }
                }
                // else: hold position inside sweet spot with LOS
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
    
    /// <summary>
    /// Pick a random reachable walkable cell within [minRadius, maxRadius] from the start.
    /// Uses path existence as a reachability check.
    /// </summary>
    private (int x, int y)? FindRandomReachableCell(int startX, int startY, Maze maze, int minRadius, int maxRadius)
    {
        if (minRadius < 1) minRadius = 1;
        if (maxRadius < minRadius) maxRadius = minRadius + 1;

        for (int attempts = 0; attempts < 60; attempts++)
        {
            int r = _random.Next(minRadius, Math.Max(minRadius + 1, maxRadius + 1));
            double theta = _random.NextDouble() * Math.PI * 2.0;
            int tx = startX + (int)MathF.Round((float)(Math.Cos(theta) * r));
            int ty = startY + (int)MathF.Round((float)(Math.Sin(theta) * r));

            if (tx < 0 || tx >= maze.Width || ty < 0 || ty >= maze.Height) continue;
            if (!IsWalkable(maze, tx, ty)) continue;

            var path = FindPathToTarget(startX, startY, tx, ty, maze);
            if (path != null && path.Count > 1)
            {
                return (tx, ty);
            }
        }

        return null;
    }
    
    private bool IsWalkable(Maze maze, int x, int y)
    {
        if (x < 0 || x >= maze.Width || y < 0 || y >= maze.Height)
            return false;
        
        return !maze.Walls[x, y];
    }
    
    /// <summary>
    /// Check if there's a clear line of sight between two points (no walls blocking)
    /// </summary>
    private bool CheckLineOfSight(float x1, float y1, float x2, float y2, Maze maze)
    {
        // Use Bresenham's line algorithm
        int startX = (int)MathF.Floor(x1);
        int startY = (int)MathF.Floor(y1);
        int endX = (int)MathF.Floor(x2);
        int endY = (int)MathF.Floor(y2);
        
        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        int err = dx - dy;
        
        int currentX = startX;
        int currentY = startY;
        
        while (true)
        {
            // Check if current position is a wall
            if (currentX >= 0 && currentX < maze.Width && 
                currentY >= 0 && currentY < maze.Height)
            {
                if (maze.Walls[currentX, currentY])
                {
                    return false; // Wall blocks line of sight
                }
            }
            
            // Reached the end point
            if (currentX == endX && currentY == endY)
            {
                break;
            }
            
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                currentX += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                currentY += sy;
            }
        }
        
        return true; // Clear line of sight
    }
}

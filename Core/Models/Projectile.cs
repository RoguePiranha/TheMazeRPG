using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents a visual projectile or weapon effect during combat
/// </summary>
public class Projectile
{
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float CurrentX { get; set; }
    public float CurrentY { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float Speed { get; set; } = 0.3f;
    public AttackAnimation Type { get; set; }
    public string AttackName { get; set; } = ""; // To differentiate sword from dagger
    public int LifeTime { get; set; } = 0;
    public int MaxLifeTime { get; set; } = 30;
    public bool IsActive => LifeTime < MaxLifeTime && !HitWall;
    public bool HitWall { get; set; } = false;
    
    public void Update(Maze? maze = null)
    {
        if (!IsActive) return;
        
        LifeTime++;
        
        // Move toward target
        float dx = TargetX - CurrentX;
        float dy = TargetY - CurrentY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        
        if (distance > 0.1f)
        {
            float newX = CurrentX + (dx / distance) * Speed;
            float newY = CurrentY + (dy / distance) * Speed;
            
            // Check for wall collision
            if (maze != null)
            {
                int gridX = (int)MathF.Round(newX);
                int gridY = (int)MathF.Round(newY);
                
                // Check bounds
                if (gridX >= 0 && gridX < maze.Width && gridY >= 0 && gridY < maze.Height)
                {
                    // Check if hit a wall
                    if (maze.Walls[gridX, gridY])
                    {
                        HitWall = true;
                        return;
                    }
                }
                else
                {
                    // Out of bounds
                    HitWall = true;
                    return;
                }
            }
            
            CurrentX = newX;
            CurrentY = newY;
        }
    }
}

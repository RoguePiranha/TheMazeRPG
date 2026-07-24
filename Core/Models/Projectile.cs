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
    public string AttackName { get; set; } = ""; // Display name (kept for HUD/labels)
    // Stable visual style, chosen from the attack's id (not name substrings) — the renderer
    // switches on this so renaming an attack never changes/breaks its effect.
    public VisualStyle Visual { get; set; } = VisualStyle.Blade;
    // Combat fields
    public ProjectileTeam Team { get; set; } = ProjectileTeam.Neutral;
    public int Damage { get; set; } = 0;
    // Collision radius in tiles for contact damage
    public float Radius { get; set; } = 0.2f;
    // Whether this projectile can hit multiple targets across its lifetime
    public bool CanHitMultiple { get; set; } = false;
    // Internal: has already dealt its single-target hit
    public bool ConsumedOnHit { get; set; } = false;
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

public enum ProjectileTeam
{
    Neutral,
    Hero,
    Enemy
}

/// <summary>
/// The visual effect a projectile renders as. Chosen per-attack from a stable id
/// (see AttackVisuals), so visuals never depend on the display name.
/// </summary>
public enum VisualStyle
{
    Blade,        // default melee blade line
    SwordArc,     // sweeping sword arc
    HeavyArc,     // thick heavy-weapon arc
    QuickSlash,   // rapid triple slashes
    Backstab,     // rogue X-slash + blood
    Arrow,        // arrow with fletching
    PoisonDart,   // dart with green trail
    MagicComet,   // cyan/blue magic comet (Mana/Magic bolt)
    MagicMissile, // purple glowing orb
    ArcaneRing,   // expanding AoE ring (also drives AoE behavior)
    Sonic,        // concentric sound-wave rings
    HolyStrike,   // radiant golden strike
    ImpactBurst,  // unarmed impact burst
    Parry         // defensive arc flash (not yet produced by any attack)
}

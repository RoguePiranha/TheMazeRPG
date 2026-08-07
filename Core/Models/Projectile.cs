using System;
using System.Collections.Generic;

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

    /// <summary>Who fired this, as a display string (e.g. "Goblin Mage") — a projectile doesn't hold
    /// a reference to its shooter, and by the time one lands the shooter may already be dead. Used
    /// to describe a hero's cause of death in their legacy record.</summary>
    public string SourceLabel { get; set; } = "";
    // Stable visual style, chosen from the attack's id (not name substrings) — the renderer
    // switches on this so renaming an attack never changes/breaks its effect.
    public VisualStyle Visual { get; set; } = VisualStyle.Blade;
    // Elemental flavor (magic attacks) — drives the projectile's color; None = the visual's own
    // default color. See MagicElements.For.
    public MagicElement Element { get; set; } = MagicElement.None;
    // The attacker's accuracy (their effective Dexterity at fire time), used against the target's
    // Agility to roll a dodge on contact (see GameState.RollDodge).
    public float Accuracy { get; set; }
    /// <summary>Whether this attack counts as magic for the target's 20% magic resist — decided
    /// at spawn (Magic animation OR a mana/faith cost), so contact-time resolution matches the
    /// target-locked path exactly.</summary>
    public bool IsMagic { get; set; }
    // Combat fields
    public ProjectileTeam Team { get; set; } = ProjectileTeam.Neutral;
    public int Damage { get; set; } = 0;
    // Directional (manual-fire) hero shots don't know their target at spawn time, so they carry
    // the hero's pre-defense offensive damage here (> 0) and the final hit is computed against
    // whatever enemy they strike (see GameState.ProcessProjectileCollisions). Target-locked
    // auto-combat shots leave this 0 and use the pre-baked Damage instead.
    public int StatDamage { get; set; } = 0;
    // Collision radius in tiles for contact damage
    public float Radius { get; set; } = 0.2f;
    // Whether this projectile can hit multiple targets across its lifetime
    public bool CanHitMultiple { get; set; } = false;
    // Internal: has already dealt its single-target hit
    public bool ConsumedOnHit { get; set; } = false;
    /// <summary>
    /// Multi-hit memory: enemies this projectile has already resolved against (hit OR dodged).
    /// An AoE that overlaps a target for many ticks must damage it exactly once, not once per
    /// tick — and a dodge exempts that enemy without cancelling the effect for everyone else.
    /// Transient like the projectile itself; never persisted.
    /// </summary>
    public HashSet<Enemy> HitTargets { get; } = new();
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

                    // A step that changes BOTH grid coordinates crossed a cell corner between
                    // samples. If the two cells flanking that corner are solid, the shot is
                    // squeezing through a zero-width gap — it stops on the corner exactly as it
                    // would on a wall face (mirrors SightLine's diagonal rule: what can't be
                    // seen through can't be shot through).
                    int fromGridX = (int)MathF.Round(CurrentX);
                    int fromGridY = (int)MathF.Round(CurrentY);
                    if (gridX != fromGridX && gridY != fromGridY)
                    {
                        bool ShoulderBlocked(int x, int y) =>
                            x < 0 || x >= maze.Width || y < 0 || y >= maze.Height || maze.Walls[x, y];
                        if (ShoulderBlocked(gridX, fromGridY) && ShoulderBlocked(fromGridX, gridY))
                        {
                            HitWall = true;
                            return;
                        }
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
    Parry,        // defensive arc flash (not yet produced by any attack)
    StaffThrust,  // pole thrust (staff/spear held weapons)
    PickaxeSwing  // the miner's pick — combat swings and rock-chipping alike
}

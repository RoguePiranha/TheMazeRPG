using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Short-lived visual effect for impacts (on-hit flashes, etc.).
/// </summary>
public class HitEffect
{
    public float X { get; set; }
    public float Y { get; set; }
    public int LifeTime { get; set; }
    public int MaxLifeTime { get; set; } = 8; // very short
    public HitEffectType Type { get; set; } = HitEffectType.Impact;
    public ProjectileTeam Team { get; set; } = ProjectileTeam.Neutral; // derive color in renderer
    /// <summary>The visual style of the projectile that caused this impact, so the renderer can
    /// play a matching impact burst (an arrow shatters; a slash sparks) instead of one generic
    /// flash. None-equivalent default keeps old call sites on the generic flash.</summary>
    public VisualStyle Visual { get; set; } = VisualStyle.Blade;
    public bool IsActive => LifeTime < MaxLifeTime;
}

public enum HitEffectType
{
    Impact
}

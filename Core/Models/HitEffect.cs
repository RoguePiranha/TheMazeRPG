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
    public bool IsActive => LifeTime < MaxLifeTime;
}

public enum HitEffectType
{
    Impact
}

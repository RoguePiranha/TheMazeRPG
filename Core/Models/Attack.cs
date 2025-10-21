using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents an attack that can be performed in combat
/// </summary>
public class Attack
{
    public string Name { get; set; } = "Strike";
    public int Damage { get; set; }
    public float Range { get; set; } = 1.0f; // How close to be to use this attack
    public int Cooldown { get; set; } = 30; // Ticks between uses
    public AttackAnimation Animation { get; set; } = AttackAnimation.Melee;
    public string Description { get; set; } = "";
    
    // Special effects
    public float CritChance { get; set; } = 0.0f;
    public float ParryChance { get; set; } = 0.0f;
    public float KnockbackDistance { get; set; } = 0.0f;
    
    public Attack Clone()
    {
        return new Attack
        {
            Name = Name,
            Damage = Damage,
            Range = Range,
            Cooldown = Cooldown,
            Animation = Animation,
            Description = Description,
            CritChance = CritChance,
            ParryChance = ParryChance,
            KnockbackDistance = KnockbackDistance
        };
    }
}

public enum AttackAnimation
{
    Melee,      // Lunge forward
    Ranged,     // Step back while attacking
    Magic,      // Stay still, projectile goes forward
    Heavy,      // Big wind-up, knockback
    Quick       // Rapid jab, minimal movement
}

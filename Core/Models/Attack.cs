using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents an attack that can be performed in combat
/// </summary>
public class Attack
{
    /// <summary>Stable identifier for behavior/visual lookups (preferred over matching on Name).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Strike";
    public int Damage { get; set; }
    public float Range { get; set; } = 1.0f; // How close to be to use this attack
    public int Cooldown { get; set; } = 30; // Ticks between uses
    public AttackAnimation Animation { get; set; } = AttackAnimation.Melee;
    public string Description { get; set; } = "";
    
    // Resource costs (0 = free basic attack)
    public int StaminaCost { get; set; } = 0;
    public int ManaCost { get; set; } = 0;
    public int FaithCost { get; set; } = 0;
    public bool IsHeavyAttack => StaminaCost > 0 || ManaCost > 0 || FaithCost > 0;
    
    // Special effects
    public float CritChance { get; set; } = 0.0f;
    public float ParryChance { get; set; } = 0.0f;
    public float KnockbackDistance { get; set; } = 0.0f;
    
    public Attack Clone()
    {
        return new Attack
        {
            Id = Id,
            Name = Name,
            Damage = Damage,
            Range = Range,
            Cooldown = Cooldown,
            Animation = Animation,
            Description = Description,
            StaminaCost = StaminaCost,
            ManaCost = ManaCost,
            FaithCost = FaithCost,
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

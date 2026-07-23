namespace TheMazeRPG.Core.Models;

/// <summary>
/// A held weapon. Produces an <see cref="Attack"/> when used, which is how the combat
/// system will eventually source attacks (replacing AttackFactory's hardcoded switch).
/// </summary>
public class Weapon : Combinable
{
    public override CombinableKind Kind => CombinableKind.Weapon;

    public int BaseDamage { get; set; } = 5;
    public float Range { get; set; } = 1.0f;
    public int Cooldown { get; set; } = 20;
    public AttackAnimation Animation { get; set; } = AttackAnimation.Melee;
    public float CritChance { get; set; } = 0.05f;
    public float KnockbackDistance { get; set; } = 0f;
    public int StaminaCost { get; set; } = 0;

    /// <summary>Project this weapon into an Attack the combat system can execute.</summary>
    public Attack ToAttack() => new()
    {
        Name = Name,
        Damage = BaseDamage,
        Range = Range,
        Cooldown = Cooldown,
        Animation = Animation,
        Description = Description,
        CritChance = CritChance,
        KnockbackDistance = KnockbackDistance,
        StaminaCost = StaminaCost
    };
}

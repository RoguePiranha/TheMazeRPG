namespace TheMazeRPG.Core.Models;

/// <summary>
/// A castable spell. Levels with use (Game Idea.md) and can evolve/merge. Produces an
/// <see cref="Attack"/> when cast.
/// </summary>
public class Spell : Combinable
{
    public override CombinableKind Kind => CombinableKind.Spell;

    public int BaseDamage { get; set; } = 6;
    public float Range { get; set; } = 3.0f;
    public int Cooldown { get; set; } = 18;
    public int ManaCost { get; set; } = 0;
    public int FaithCost { get; set; } = 0;
    public AttackAnimation Animation { get; set; } = AttackAnimation.Magic;
    public float CritChance { get; set; } = 0.1f;

    /// <summary>Project this spell into an Attack the combat system can execute.</summary>
    public Attack ToAttack() => new()
    {
        Name = Name,
        Damage = BaseDamage,
        Range = Range,
        Cooldown = Cooldown,
        Animation = Animation,
        Description = Description,
        CritChance = CritChance,
        ManaCost = ManaCost,
        FaithCost = FaithCost
    };
}

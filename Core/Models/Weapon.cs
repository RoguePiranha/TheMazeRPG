using System.Text.Json.Serialization;

namespace TheMazeRPG.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WeaponType>))]
public enum WeaponType
{
    Unknown,
    Sword,
    Dagger,
    Bow,
    Axe,
    Mace,
    Spear,
    Staff
}

/// <summary>
/// A held weapon. Produces an <see cref="Attack"/> when used, which is how the combat
/// system will eventually source attacks (replacing AttackFactory's hardcoded switch).
/// </summary>
public class Weapon : Combinable
{
    public override CombinableKind Kind => CombinableKind.Weapon;

    public WeaponType WeaponType { get; set; }
    public int BaseDamage { get; set; } = 5;
    public float Range { get; set; } = 1.0f;
    public int Cooldown { get; set; } = 20;
    public AttackAnimation Animation { get; set; } = AttackAnimation.Melee;
    public float CritChance { get; set; } = 0.05f;
    public float KnockbackDistance { get; set; } = 0f;
    public int StaminaCost { get; set; } = 0;

    /// <summary>Number of held-item slots occupied. Bows and large weapons require both hands.</summary>
    public int HandsRequired { get; set; } = 1;

    /// <summary>Project this weapon into an Attack the combat system can execute. Rarity applies
    /// here (see RarityScaling), so a combined/crafted higher-rarity weapon actually hits harder.</summary>
    public Attack ToAttack() => new()
    {
        Id = Id,
        Name = Name,
        Damage = RarityScaling.ScaleDamage(BaseDamage, Rarity),
        Range = Range,
        Cooldown = RarityScaling.ScaleCooldown(Cooldown, Rarity),
        Animation = Animation,
        Description = Description,
        CritChance = CritChance,
        KnockbackDistance = KnockbackDistance,
        StaminaCost = StaminaCost
    };
}

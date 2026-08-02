namespace TheMazeRPG.Core.Models;

/// <summary>
/// Sample base combinables (spells, weapons, items, abilities) used to seed and
/// demonstrate the combine system. Each method returns a fresh instance so results
/// can be mutated/merged without affecting the "template".
/// Eventually these move to JSON data under Data/; kept in code for now to bootstrap.
/// </summary>
public static class CombinableCatalog
{
    public static Spell Fireball() => new()
    {
        Id = "fireball", Name = "Fireball", Rarity = Rarity.Common,
        BaseDamage = 8, Range = 3.0f, Cooldown = 18, ManaCost = 10,
        Attributes = { GameAttribute.Fire },
        Description = "Shoots a fireball."
    };

    public static Spell IceShard() => new()
    {
        Id = "ice-shard", Name = "Ice Shard", Rarity = Rarity.Common,
        BaseDamage = 7, Range = 3.0f, Cooldown = 16, ManaCost = 8,
        Attributes = { GameAttribute.Ice },
        Description = "Shoots an ice shard."
    };

    public static Weapon Bow() => new()
    {
        Id = "bow", Name = "Bow", Rarity = Rarity.Common,
        BaseDamage = 6, Range = 4.0f, Cooldown = 15, Animation = AttackAnimation.Ranged,
        Description = "Shoots arrows."
    };

    public static Weapon Sword() => new()
    {
        Id = "sword", Name = "Sword", Rarity = Rarity.Common,
        BaseDamage = 8, Range = 1.2f, Cooldown = 18, Animation = AttackAnimation.Melee,
        Attributes = { GameAttribute.Sharp },
        Description = "A strength-based melee weapon."
    };

    public static Weapon Dagger() => new()
    {
        Id = "dagger", Name = "Dagger", Rarity = Rarity.Common,
        BaseDamage = 6, Range = 1.0f, Cooldown = 12, Animation = AttackAnimation.Quick,
        Attributes = { GameAttribute.Sharp },
        Description = "A dexterity-based melee weapon."
    };

    public static Item ShieldGenerator() => new()
    {
        Id = "shield-generator", Name = "Shield Generator", Rarity = Rarity.Uncommon,
        Attributes = { GameAttribute.Arcane },
        Description = "Creates a shield that absorbs damage."
    };

    public static Item HealthPotion() => new()
    {
        Id = "health-potion", Name = "Health Potion", Rarity = Rarity.Common,
        UseEffect = ItemUseEffect.RestoreHealth, EffectPower = 30, Consumable = true,
        Description = "Restores health when consumed."
    };

    public static Ability DenseMusculature() => new()
    {
        Id = "dense-musculature", Name = "Dense Musculature", Rarity = Rarity.Rare,
        Modifiers = { ["StrengthMult"] = 1.5f },
        Description = "Each point in Strength is worth 1.5."
    };

    public static Ability ManaCircuitry() => new()
    {
        Id = "mana-circuitry", Name = "Mana Circuitry", Rarity = Rarity.Rare,
        Modifiers = { ["SpellCooldownPct"] = -0.1f, ["ManaRegen"] = 1f },
        Description = "Decreases spell cooldown and increases mana regen."
    };
}

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

    public static Item Torch() => new()
    {
        Id = "torch", Name = "Torch", Rarity = Rarity.Common,
        Attributes = { GameAttribute.Fire },
        Description = "A burning brand. Carried, it pushes the night back a few paces."
    };

    public static Ability NightSight() => new()
    {
        Id = "night-sight", Name = "Night Sight", Rarity = Rarity.Rare,
        Modifiers = { ["NightSight"] = 1f },
        Description = "The dark thins to dusk — the whole world visible, not just a pool of light."
    };

    /// <summary>The level-milestone unlocks (L5/10/15), as real Combinables so they live in the
    /// Loadout/Inventory like everything else and survive RefreshAttacks and save/load. Stats
    /// mirror the old direct Attack appends; mana-bolt gains a small cost now that it's a real
    /// spell. Null for an unknown id.</summary>
    public static Combinable? BuildUnlock(string id) => id switch
    {
        "power-strike" => new Weapon
        {
            Id = "power-strike", Name = "Power Strike", Rarity = Rarity.Uncommon,
            BaseDamage = 18, Range = 1.2f, Cooldown = 28, Animation = AttackAnimation.Heavy,
            CritChance = 0.12f, Attributes = { GameAttribute.Heavy },
            Description = "A heavy blow with bonus crit."
        },
        "quick-jab" => new Weapon
        {
            Id = "quick-jab", Name = "Quick Jab", Rarity = Rarity.Uncommon,
            BaseDamage = 12, Range = 1.0f, Cooldown = 16, Animation = AttackAnimation.Quick,
            CritChance = 0.18f, Attributes = { GameAttribute.Light },
            Description = "A rapid jab with high crit chance."
        },
        "mana-bolt" => new Spell
        {
            Id = "mana-bolt", Name = "Mana Bolt", Rarity = Rarity.Uncommon,
            BaseDamage = 22, Range = 2.0f, Cooldown = 32, ManaCost = 8,
            CritChance = 0.10f, Attributes = { GameAttribute.Magic },
            Description = "A ranged bolt of ordered mana."
        },
        _ => null
    };
}

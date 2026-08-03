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
        WeaponType = WeaponType.Bow,
        BaseDamage = 6, Range = 4.0f, Cooldown = 15, Animation = AttackAnimation.Ranged,
        HandsRequired = 2,
        Description = "Shoots arrows."
    };

    public static Weapon Sword() => new()
    {
        Id = "sword", Name = "Sword", Rarity = Rarity.Common,
        WeaponType = WeaponType.Sword,
        BaseDamage = 8, Range = 1.2f, Cooldown = 18, Animation = AttackAnimation.Melee,
        Attributes = { GameAttribute.Sharp },
        Description = "A strength-based melee weapon."
    };

    public static Weapon Dagger() => new()
    {
        Id = "dagger", Name = "Dagger", Rarity = Rarity.Common,
        WeaponType = WeaponType.Dagger,
        BaseDamage = 6, Range = 1.0f, Cooldown = 12, Animation = AttackAnimation.Quick,
        Attributes = { GameAttribute.Sharp },
        Description = "A dexterity-based melee weapon."
    };

    public static Weapon Staff() => new()
    {
        Id = "staff", Name = "Staff", Rarity = Rarity.Common,
        WeaponType = WeaponType.Staff,
        BaseDamage = 5, Range = 1.3f, Cooldown = 22, Animation = AttackAnimation.Melee,
        Attributes = { GameAttribute.Arcane },
        Description = "A balanced staff that can serve as an arcane focus."
    };

    public static Item ShieldGenerator() => new()
    {
        Id = "shield-generator", Name = "Shield Generator", Rarity = Rarity.Uncommon,
        Attributes = { GameAttribute.Arcane }, EquipSlot = EquipmentSlot.Amulet, DefenseBonus = 4,
        Description = "An amulet-sized projector that reinforces the wearer's defenses."
    };

    public static Item HolySymbol() => new()
    {
        Id = "holy-symbol", Name = "Holy Symbol", Rarity = Rarity.Common,
        Attributes = { GameAttribute.Blessed, GameAttribute.Holy },
        EquipSlot = EquipmentSlot.Amulet,
        Description = "A small blessed emblem used as a focus for faith."
    };

    public static Armor IronHelm() => new()
    {
        Id = "iron-helm", Name = "Iron Helm", Rarity = Rarity.Common,
        Slot = EquipmentSlot.Head, DefenseBonus = 2, Attributes = { GameAttribute.Heavy },
        Description = "A plain iron helmet."
    };

    public static Armor LeatherCoat() => new()
    {
        Id = "leather-coat", Name = "Leather Coat", Rarity = Rarity.Common,
        Slot = EquipmentSlot.Chest, DefenseBonus = 3, Attributes = { GameAttribute.Light },
        Description = "Flexible chest armor made for travel."
    };

    public static Armor LeatherGloves() => new()
    {
        Id = "leather-gloves", Name = "Leather Gloves", Rarity = Rarity.Common,
        Slot = EquipmentSlot.Hands, DefenseBonus = 1, Attributes = { GameAttribute.Light },
        Description = "Protective gloves that leave the fingers mobile."
    };

    public static Armor GuardLeggings() => new()
    {
        Id = "guard-leggings", Name = "Guard Leggings", Rarity = Rarity.Common,
        Slot = EquipmentSlot.Legs, DefenseBonus = 2, Attributes = { GameAttribute.Medium },
        Description = "Layered protection for the legs."
    };

    public static Armor TrailBoots() => new()
    {
        Id = "trail-boots", Name = "Trail Boots", Rarity = Rarity.Common,
        Slot = EquipmentSlot.Feet, DefenseBonus = 1, Attributes = { GameAttribute.Light },
        Description = "Sturdy boots for broken dungeon floors."
    };

    public static Item WardRing() => new()
    {
        Id = "ward-ring", Name = "Ward Ring", Rarity = Rarity.Uncommon,
        EquipSlot = EquipmentSlot.RingLeft, DefenseBonus = 2,
        Attributes = { GameAttribute.Blessed }, Description = "A ring etched with a small protective ward."
    };

    public static Item ChestKey(string keyId) => new()
    {
        Id = keyId, KeyId = keyId, Name = "Chest Key", Rarity = Rarity.Common,
        Description = "A key made for one particular dungeon chest."
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

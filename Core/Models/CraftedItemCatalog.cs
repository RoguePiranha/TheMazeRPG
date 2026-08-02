namespace TheMazeRPG.Core.Models;

/// <summary>
/// Maps a recipe's "weapon" outputId to a real Weapon Combinable. A recipe-crafted item is a
/// normal Combinable fed through the same equip/inventory pipeline (GameState.AcquireLoot) as
/// dungeon loot — this catalog just supplies the base stats, the same role AttackFactory's
/// hardcoded factory methods play for class starting gear.
/// </summary>
public static class CraftedItemCatalog
{
    public static Combinable? Build(string outputId) => outputId switch
    {
        "iron-sword" => new Weapon
        {
            Id = "iron-sword", Name = "Iron Sword", Rarity = Rarity.Common,
            WeaponType = WeaponType.Sword,
            BaseDamage = 7, Range = 1.2f, Cooldown = 16, Animation = AttackAnimation.Melee,
            CritChance = 0.08f, Attributes = { GameAttribute.Sharp },
            Description = "A sword forged from smelted iron ingots."
        },
        _ => null
    };
}

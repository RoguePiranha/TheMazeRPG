using System;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Produces loot (weapons/spells/items) found in chests. Rarity scales with the floor.
/// Draws from <see cref="CombinableCatalog"/> for now; later this becomes JSON loot tables.
/// </summary>
public static class LootService
{
    // Base gear that can drop. Weapons/spells are equippable to the hotbar; items go to inventory.
    private static readonly Func<Combinable>[] Pool =
    {
        CombinableCatalog.Sword,
        CombinableCatalog.Dagger,
        CombinableCatalog.Bow,
        CombinableCatalog.Fireball,
        CombinableCatalog.IceShard,
        CombinableCatalog.ShieldGenerator,
        CombinableCatalog.Torch,
        CombinableCatalog.NightSight
    };

    public static Combinable Roll(int floor, Random rng)
    {
        var item = Pool[rng.Next(Pool.Length)]();
        item.Rarity = RollRarity(floor, rng);
        return item;
    }

    // Higher floors bias toward better rarities (Game Idea.md: "item rarity increases based on floor").
    private static Rarity RollRarity(int floor, Random rng)
    {
        int roll = rng.Next(100) + floor * 4;
        return roll switch
        {
            < 55 => Rarity.Common,
            < 80 => Rarity.Uncommon,
            < 93 => Rarity.Rare,
            < 99 => Rarity.Epic,
            < 106 => Rarity.Legendary,
            _ => Rarity.Mythic
        };
    }
}

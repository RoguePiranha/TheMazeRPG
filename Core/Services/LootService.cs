using System;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Produces loot (weapons/spells/items) found in chests and on enemies. Rarity is INTRINSIC to
/// each definition (owner ruling 2026-08-05: an Iron item is Common forever — it never rolls
/// Legendary; rarity climbs only through combining/crafting). The randomness lives in WHICH
/// entry drops: selection is weighted by the entry's own rarity, and deeper floors shift the
/// odds toward the pool's rarer entries (Game Idea.md: "item rarity increases based on floor").
/// Draws from <see cref="CombinableCatalog"/> for now; later this becomes JSON loot tables.
/// </summary>
public static class LootService
{
    // Base gear that can drop. Physical gear uses body slots; spells use the action bar.
    private static readonly Func<Combinable>[] Pool =
    {
        CombinableCatalog.Sword,
        CombinableCatalog.Dagger,
        CombinableCatalog.Bow,
        CombinableCatalog.Fireball,
        CombinableCatalog.IceShard,
        CombinableCatalog.ShieldGenerator,
        CombinableCatalog.IronHelm,
        CombinableCatalog.LeatherCoat,
        CombinableCatalog.LeatherGloves,
        CombinableCatalog.GuardLeggings,
        CombinableCatalog.TrailBoots,
        CombinableCatalog.WardRing,
        CombinableCatalog.HealthPotion,
        CombinableCatalog.Torch,
        CombinableCatalog.NightSight
    };

    // Each factory's intrinsic rarity, read once — definitions don't change at runtime.
    private static readonly (Func<Combinable> Make, Rarity Rarity)[] Weighted =
        Pool.Select(make => (make, make().Rarity)).ToArray();

    /// <summary>Base draw weight per rarity tier (Common overwhelmingly likely at floor 0).</summary>
    private static float BaseWeight(Rarity rarity) => rarity switch
    {
        Rarity.Common => 100f,
        Rarity.Uncommon => 45f,
        Rarity.Rare => 18f,
        Rarity.Epic => 6f,
        Rarity.Legendary => 2f,
        _ => 0.5f
    };

    /// <summary>Floor scaling: each floor multiplies a tier's weight by (1 + 0.08·floor)^tier,
    /// so depth compounds hardest for the rarest entries without ever re-rolling anything.</summary>
    private static float Weight(Rarity rarity, int floor) =>
        BaseWeight(rarity) * MathF.Pow(1f + 0.08f * Math.Max(0, floor), (int)rarity);

    public static Combinable Roll(int floor, Random rng)
    {
        float total = Weighted.Sum(entry => Weight(entry.Rarity, floor));
        double roll = rng.NextDouble() * total;
        foreach (var (make, rarity) in Weighted)
        {
            roll -= Weight(rarity, floor);
            if (roll <= 0) return make();
        }
        return Weighted[^1].Make();
    }
}

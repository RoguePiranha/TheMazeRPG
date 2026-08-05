using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Rarity's mechanical weight (owner ruling 2026-08-05: rarity is intrinsic to a definition —
/// plain Iron gear is Common forever and can never roll Legendary; the randomness lives in
/// WHICH item drops, and rarity climbs only through combining/crafting). Per tier above Common:
/// +8% damage, −3% cooldown (floored), +15% armor/accessory defense, +25% consumable effect.
/// Applied at the projection/consumption seams so stored definitions stay untouched.
/// </summary>
public static class RarityScaling
{
    public static float Damage(Rarity rarity) => 1f + 0.08f * (int)rarity;
    public static float Cooldown(Rarity rarity) => Math.Max(0.70f, 1f - 0.03f * (int)rarity);
    public static float Defense(Rarity rarity) => 1f + 0.15f * (int)rarity;
    public static float Effect(Rarity rarity) => 1f + 0.25f * (int)rarity;

    public static int ScaleDamage(int baseDamage, Rarity rarity) =>
        Math.Max(1, (int)MathF.Round(baseDamage * Damage(rarity)));
    public static int ScaleCooldown(int baseCooldown, Rarity rarity) =>
        Math.Max(1, (int)MathF.Round(baseCooldown * Cooldown(rarity)));
    public static int ScaleDefense(int baseDefense, Rarity rarity) =>
        (int)MathF.Round(baseDefense * Defense(rarity));
    public static int ScaleEffect(int basePower, Rarity rarity) =>
        (int)MathF.Round(basePower * Effect(rarity));
}

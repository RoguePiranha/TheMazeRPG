using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Maps an attack to its projectile <see cref="VisualStyle"/> using the stable attack id,
/// with a sensible fallback by animation. This replaces the old approach of matching
/// substrings of the display name (which silently broke when names didn't match, e.g. the
/// Archer's "Quick Shot"/"Power Shot" rendering as a plain line instead of an arrow).
/// </summary>
public static class AttackVisuals
{
    public static VisualStyle For(Attack attack) => attack.Id switch
    {
        "quick-slash" => VisualStyle.SwordArc,
        "quick-stab" or "quick-jab" => VisualStyle.QuickSlash,
        "devastating-backstab" => VisualStyle.Backstab,
        "quick-shot" or "power-shot" => VisualStyle.Arrow,
        "magic-dart" or "mana-bolt" => VisualStyle.MagicComet,
        "arcane-blast" => VisualStyle.ArcaneRing,
        "divine-wrath" => VisualStyle.MagicMissile,
        "sound-wave" or "sonic-boom" => VisualStyle.Sonic,
        "holy-touch" => VisualStyle.HolyStrike,
        "light-punch" => VisualStyle.ImpactBurst,
        "pickaxe" => VisualStyle.PickaxeSwing,
        _ => FallbackFor(attack.Animation)
    };

    /// <summary>
    /// Hero-aware style: the universal basic attack reads as whatever is actually in hand
    /// (owner ruling 2026-08-06) — the staff thrusts, the axe cleaves, the pick swings, and
    /// bare hands punch. Named techniques keep their authored visuals; ranged/magic weapons
    /// keep their animation-derived effects.
    /// </summary>
    public static VisualStyle ForHero(Hero hero, Attack attack)
    {
        if (attack.Id != "basic-attack") return For(attack);

        Weapon? weapon = hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) as Weapon ??
            hero.Equipment.GetValueOrDefault(EquipmentSlot.OffHand) as Weapon;
        if (weapon == null) return VisualStyle.ImpactBurst; // bare fists

        if (weapon.Animation is AttackAnimation.Melee or AttackAnimation.Heavy or AttackAnimation.Quick)
        {
            return weapon.WeaponType switch
            {
                WeaponType.Sword => VisualStyle.SwordArc,
                WeaponType.Dagger => VisualStyle.QuickSlash,
                WeaponType.Axe or WeaponType.Mace => VisualStyle.HeavyArc,
                WeaponType.Spear or WeaponType.Staff => VisualStyle.StaffThrust,
                WeaponType.Pickaxe => VisualStyle.PickaxeSwing,
                _ => VisualStyle.Blade
            };
        }
        return For(attack);
    }

    // Any unmapped attack (including future/merged ones) still gets a sensible effect.
    private static VisualStyle FallbackFor(AttackAnimation animation) => animation switch
    {
        AttackAnimation.Ranged => VisualStyle.Arrow,
        AttackAnimation.Magic => VisualStyle.MagicComet,
        AttackAnimation.Heavy => VisualStyle.HeavyArc,
        AttackAnimation.Quick => VisualStyle.QuickSlash,
        _ => VisualStyle.Blade
    };
}

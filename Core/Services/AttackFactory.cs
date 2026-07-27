using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Builds a class's starting loadout as equippable <see cref="Weapon"/>/<see cref="Spell"/>
/// data, and projects it into the <see cref="Attack"/>s the combat system runs. Attacks are
/// therefore sourced from equipped data (Ids included), not a hardcoded Attack switch — the
/// foundation for inventory/equip and for merged gear changing your attacks.
/// </summary>
public static class AttackFactory
{
    /// <summary>The weapons/spells a class starts with (light attack first, then heavy).</summary>
    public static List<Combinable> GetStartingLoadout(string className) => className switch
    {
        "Warrior" => new List<Combinable> { QuickSlash(), HeavyCleave() },
        "Mage" or "Mage Apprentice" => new List<Combinable> { MagicDart(), ArcaneBlast() },
        "Rogue" => new List<Combinable> { QuickStab(), DevastatingBackstab() },
        "Cleric" or "Priest" => new List<Combinable> { HolyTouch(), DivineWrath() },
        "Ranger" or "Archer" => new List<Combinable> { QuickShot(), PowerShot() },
        "Bard" => new List<Combinable> { SoundWave(), SonicBoom() },
        _ => new List<Combinable> { LightPunch(), HeavyStrike() } // Wanderer and others
    };

    /// <summary>Project a loadout into executable attacks (weapons and spells only).</summary>
    public static List<Attack> ToAttacks(IEnumerable<Combinable> loadout)
    {
        var attacks = new List<Attack>();
        foreach (var c in loadout)
        {
            if (c is Weapon w) attacks.Add(w.ToAttack());
            else if (c is Spell s) attacks.Add(s.ToAttack());
        }
        return attacks;
    }

    /// <summary>Convenience: the projected starting attacks for a class.</summary>
    public static List<Attack> GetStartingAttacks(string className) => ToAttacks(GetStartingLoadout(className));

    // --- Warrior ---
    private static Weapon QuickSlash() => new()
    {
        Id = "quick-slash", Name = "Quick Slash", Rarity = Rarity.Common,
        BaseDamage = 6, Range = 1.2f, Cooldown = 15, Animation = AttackAnimation.Melee,
        CritChance = 0.1f, KnockbackDistance = 0.2f,
        Attributes = { GameAttribute.Sharp }, Description = "A quick sword strike"
    };
    private static Weapon HeavyCleave() => new()
    {
        Id = "heavy-cleave", Name = "Heavy Cleave", Rarity = Rarity.Common,
        BaseDamage = 15, Range = 1.2f, Cooldown = 40, Animation = AttackAnimation.Heavy,
        CritChance = 0.15f, KnockbackDistance = 0.5f, StaminaCost = 25,
        Attributes = { GameAttribute.Sharp, GameAttribute.Heavy }, Description = "A devastating sword strike"
    };

    // --- Mage ---
    private static Spell MagicDart() => new()
    {
        // Id kept as "magic-dart" (stable) though the display name is "Mana Dart" — the caster's
        // basic reliable ordered-mana bolt (pale blue), per the magic-system doc.
        Id = "magic-dart", Name = "Mana Dart", Rarity = Rarity.Common,
        BaseDamage = 5, Range = 3.0f, Cooldown = 18, Animation = AttackAnimation.Magic,
        CritChance = 0.1f, Attributes = { GameAttribute.Magic }, Description = "A small bolt of ordered mana"
    };
    private static Spell ArcaneBlast() => new()
    {
        Id = "arcane-blast", Name = "Arcane Blast", Rarity = Rarity.Uncommon,
        BaseDamage = 18, Range = 3.5f, Cooldown = 50, Animation = AttackAnimation.Magic,
        CritChance = 0.25f, ManaCost = 30, Attributes = { GameAttribute.Arcane }, Description = "A devastating arcane explosion"
    };

    // --- Rogue ---
    private static Weapon QuickStab() => new()
    {
        Id = "quick-stab", Name = "Quick Stab", Rarity = Rarity.Common,
        BaseDamage = 7, Range = 1.0f, Cooldown = 12, Animation = AttackAnimation.Quick,
        CritChance = 0.2f, Attributes = { GameAttribute.Sharp }, Description = "A rapid strike"
    };
    private static Weapon DevastatingBackstab() => new()
    {
        Id = "devastating-backstab", Name = "Devastating Backstab", Rarity = Rarity.Uncommon,
        BaseDamage = 20, Range = 1.0f, Cooldown = 35, Animation = AttackAnimation.Quick,
        CritChance = 0.4f, StaminaCost = 20, Attributes = { GameAttribute.Sharp }, Description = "A lethal strike from the shadows"
    };

    // --- Cleric / Priest ---
    private static Weapon HolyTouch() => new()
    {
        Id = "holy-touch", Name = "Holy Touch", Rarity = Rarity.Common,
        BaseDamage = 5, Range = 1.5f, Cooldown = 20, Animation = AttackAnimation.Melee,
        CritChance = 0.1f, Attributes = { GameAttribute.Holy }, Description = "A blessed strike"
    };
    private static Spell DivineWrath() => new()
    {
        Id = "divine-wrath", Name = "Divine Wrath", Rarity = Rarity.Uncommon,
        BaseDamage = 16, Range = 2.0f, Cooldown = 45, Animation = AttackAnimation.Magic,
        CritChance = 0.15f, FaithCost = 25, Attributes = { GameAttribute.Holy }, Description = "Unleash holy fury"
    };

    // --- Ranger / Archer ---
    private static Weapon QuickShot() => new()
    {
        Id = "quick-shot", Name = "Quick Shot", Rarity = Rarity.Common,
        BaseDamage = 6, Range = 4.0f, Cooldown = 15, Animation = AttackAnimation.Ranged,
        CritChance = 0.15f, Description = "A rapid arrow"
    };
    private static Weapon PowerShot() => new()
    {
        Id = "power-shot", Name = "Power Shot", Rarity = Rarity.Uncommon,
        BaseDamage = 17, Range = 5.0f, Cooldown = 38, Animation = AttackAnimation.Ranged,
        CritChance = 0.3f, StaminaCost = 22, Description = "A devastating arrow"
    };

    // --- Bard ---
    private static Spell SoundWave() => new()
    {
        Id = "sound-wave", Name = "Sound Wave", Rarity = Rarity.Common,
        BaseDamage = 5, Range = 2.0f, Cooldown = 18, Animation = AttackAnimation.Magic,
        KnockbackDistance = 0.3f, Attributes = { GameAttribute.Magic }, Description = "A small sonic burst"
    };
    private static Spell SonicBoom() => new()
    {
        Id = "sonic-boom", Name = "Sonic Boom", Rarity = Rarity.Uncommon,
        BaseDamage = 14, Range = 2.5f, Cooldown = 42, Animation = AttackAnimation.Magic,
        KnockbackDistance = 0.8f, ManaCost = 20, Attributes = { GameAttribute.Magic }, Description = "A powerful shockwave"
    };

    // --- Wanderer / default ---
    private static Weapon LightPunch() => new()
    {
        Id = "light-punch", Name = "Light Punch", Rarity = Rarity.Common,
        BaseDamage = 4, Range = 1.0f, Cooldown = 15, Animation = AttackAnimation.Melee,
        CritChance = 0.05f, Description = "A basic punch"
    };
    private static Weapon HeavyStrike() => new()
    {
        Id = "heavy-strike", Name = "Heavy Strike", Rarity = Rarity.Common,
        BaseDamage = 12, Range = 1.0f, Cooldown = 35, Animation = AttackAnimation.Heavy,
        CritChance = 0.1f, StaminaCost = 18, Attributes = { GameAttribute.Heavy }, Description = "A powerful strike"
    };
}

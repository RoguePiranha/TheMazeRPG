using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Builds learned combat techniques independently from physical equipment.</summary>
public static class AttackFactory
{
    public static List<Attack> GetStartingAttacks(string className) => GetClassAttacks(className);

    public static List<Attack> GetClassAttacks(string className, int level = 1)
    {
        List<Attack> attacks = className switch
        {
            "Warrior" => new() { QuickSlash(), HeavyCleave() },
            "Mage" or "Mage Apprentice" => new() { MagicDart(), ArcaneBlast() },
            "Rogue" => new() { QuickStab(), DevastatingBackstab() },
            "Cleric" or "Priest" => new() { HolyTouch(), DivineWrath() },
            "Ranger" or "Archer" => new() { QuickShot(), PowerShot() },
            "Bard" => new() { SoundWave(), SonicBoom() },
            _ => new() { LightPunch(), HeavyStrike() }
        };

        if (level >= 5) attacks.Add(new Attack { Id = "power-strike", Name = "Power Strike", Damage = 18, Range = 1.2f, Cooldown = 28, Animation = AttackAnimation.Heavy, CritChance = 0.12f, Description = "A heavy blow with bonus crit." });
        if (level >= 10) attacks.Add(new Attack { Id = "quick-jab", Name = "Quick Jab", Damage = 12, Range = 1.0f, Cooldown = 16, Animation = AttackAnimation.Quick, CritChance = 0.18f, Description = "A rapid jab with high crit chance." });
        if (level >= 15) attacks.Add(new Attack { Id = "mana-bolt", Name = "Mana Bolt", Damage = 22, Range = 2.0f, Cooldown = 32, Animation = AttackAnimation.Magic, CritChance = 0.10f, Description = "A ranged magic attack." });
        return attacks;
    }

    public static List<Attack> GetSlottedSpellAttacks(IEnumerable<Combinable> loadout)
    {
        var attacks = new List<Attack>();
        foreach (Combinable item in loadout)
            if (item is Spell spell) attacks.Add(spell.ToAttack());
        return attacks;
    }

    public static Dictionary<EquipmentSlot, Combinable> GetStartingEquipment(string className)
    {
        var equipment = new Dictionary<EquipmentSlot, Combinable>();
        switch (className)
        {
            case "Warrior":
                equipment[EquipmentSlot.MainHand] = CombinableCatalog.Sword();
                break;
            case "Rogue":
                equipment[EquipmentSlot.MainHand] = CombinableCatalog.Dagger();
                equipment[EquipmentSlot.OffHand] = CombinableCatalog.Dagger();
                break;
            case "Ranger":
            case "Archer":
                equipment[EquipmentSlot.MainHand] = CombinableCatalog.Bow();
                break;
            case "Cleric":
            case "Priest":
                equipment[EquipmentSlot.Amulet] = CombinableCatalog.ShieldGenerator();
                break;
        }
        return equipment;
    }

    // Class actions describe what the hero does. Equipped weapons modify physical damage but are
    // never converted into hotbar entries.
    private static Attack QuickSlash() => new() { Id = "quick-slash", Name = "Quick Slash", Damage = 6, Range = 1.2f, Cooldown = 15, Animation = AttackAnimation.Melee, CritChance = 0.1f, KnockbackDistance = 0.2f, Description = "A quick cutting technique." };
    private static Attack HeavyCleave() => new() { Id = "heavy-cleave", Name = "Heavy Cleave", Damage = 15, Range = 1.2f, Cooldown = 40, Animation = AttackAnimation.Heavy, CritChance = 0.15f, KnockbackDistance = 0.5f, StaminaCost = 25, Description = "A committed sweeping technique." };
    private static Attack MagicDart() => new() { Id = "magic-dart", Name = "Mana Dart", Damage = 5, Range = 3.0f, Cooldown = 18, Animation = AttackAnimation.Magic, CritChance = 0.1f, Description = "A small bolt of ordered mana." };
    private static Attack ArcaneBlast() => new() { Id = "arcane-blast", Name = "Arcane Blast", Damage = 18, Range = 3.5f, Cooldown = 50, Animation = AttackAnimation.Magic, CritChance = 0.25f, ManaCost = 30, Description = "A devastating arcane explosion." };
    private static Attack QuickStab() => new() { Id = "quick-stab", Name = "Quick Stab", Damage = 7, Range = 1.0f, Cooldown = 12, Animation = AttackAnimation.Quick, CritChance = 0.2f, Description = "A rapid close-range technique." };
    private static Attack DevastatingBackstab() => new() { Id = "devastating-backstab", Name = "Devastating Backstab", Damage = 20, Range = 1.0f, Cooldown = 35, Animation = AttackAnimation.Quick, CritChance = 0.4f, StaminaCost = 20, Description = "A lethal strike from the shadows." };
    private static Attack HolyTouch() => new() { Id = "holy-touch", Name = "Holy Touch", Damage = 5, Range = 1.5f, Cooldown = 20, Animation = AttackAnimation.Melee, CritChance = 0.1f, Description = "A blessed close-range strike." };
    private static Attack DivineWrath() => new() { Id = "divine-wrath", Name = "Divine Wrath", Damage = 16, Range = 2.0f, Cooldown = 45, Animation = AttackAnimation.Magic, CritChance = 0.15f, FaithCost = 25, Description = "Unleash holy fury." };
    private static Attack QuickShot() => new() { Id = "quick-shot", Name = "Quick Shot", Damage = 6, Range = 4.0f, Cooldown = 15, Animation = AttackAnimation.Ranged, CritChance = 0.15f, Description = "Loose an arrow without breaking stride." };
    private static Attack PowerShot() => new() { Id = "power-shot", Name = "Power Shot", Damage = 17, Range = 5.0f, Cooldown = 38, Animation = AttackAnimation.Ranged, CritChance = 0.3f, StaminaCost = 22, Description = "A carefully drawn, devastating shot." };
    private static Attack SoundWave() => new() { Id = "sound-wave", Name = "Sound Wave", Damage = 5, Range = 2.0f, Cooldown = 18, Animation = AttackAnimation.Magic, KnockbackDistance = 0.3f, Description = "A small sonic burst." };
    private static Attack SonicBoom() => new() { Id = "sonic-boom", Name = "Sonic Boom", Damage = 14, Range = 2.5f, Cooldown = 42, Animation = AttackAnimation.Magic, KnockbackDistance = 0.8f, ManaCost = 20, Description = "A powerful shockwave." };
    private static Attack LightPunch() => new() { Id = "light-punch", Name = "Light Punch", Damage = 4, Range = 1.0f, Cooldown = 15, Animation = AttackAnimation.Melee, CritChance = 0.05f, Description = "A basic punch." };
    private static Attack HeavyStrike() => new() { Id = "heavy-strike", Name = "Heavy Strike", Damage = 12, Range = 1.0f, Cooldown = 35, Animation = AttackAnimation.Heavy, CritChance = 0.1f, StaminaCost = 18, Description = "A powerful strike." };
}

using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

public readonly record struct WeaponUseProfile(
    bool UsesWeapon,
    bool IsTrained,
    float DamageMultiplier,
    float AccuracyMultiplier,
    string WeaponNames);

/// <summary>Resolves class affinity and learned weapon training into combat modifiers.</summary>
public static class WeaponProficiencyService
{
    public const float UntrainedDamageMultiplier = 0.70f;
    public const float UntrainedAccuracyMultiplier = 0.75f;

    public static bool IsPhysical(Attack attack) =>
        attack.Animation != AttackAnimation.Magic && attack.ManaCost == 0 && attack.FaithCost == 0;

    public static bool IsTrained(Hero hero, Weapon weapon)
    {
        WeaponType type = ResolveType(weapon);
        return type != WeaponType.Unknown &&
            (hero.WeaponTraining.Contains(type) ||
             hero.ClassData?.WeaponAffinities.Contains(type) == true ||
             ProgressionService.Instance.HasActiveWeaponAffinity(hero, type));
    }

    public static WeaponUseProfile Evaluate(Hero hero, Attack attack)
    {
        if (!IsPhysical(attack)) return new(false, true, 1f, 1f, "");

        List<Weapon> weapons = EquippedWeapons(hero).ToList();
        if (weapons.Count == 0) return new(false, true, 1f, 1f, "");

        bool trained = weapons.All(weapon => IsTrained(hero, weapon));
        string names = string.Join(" / ", weapons.Select(weapon => weapon.Name));
        return trained
            ? new(true, true, 1f, 1f, names)
            : new(true, false, UntrainedDamageMultiplier, UntrainedAccuracyMultiplier, names);
    }

    public static string TrainingLabel(Hero hero, Weapon weapon)
    {
        WeaponType type = ResolveType(weapon);
        if (hero.WeaponTraining.Contains(type)) return $"{type} skill";
        if (hero.ClassData?.WeaponAffinities.Contains(type) == true ||
            ProgressionService.Instance.HasActiveWeaponAffinity(hero, type)) return "Active class affinity";
        return $"Untrained: {(1f - UntrainedDamageMultiplier):P0} damage and " +
            $"{(1f - UntrainedAccuracyMultiplier):P0} accuracy penalty";
    }

    public static WeaponType ResolveType(Weapon weapon)
    {
        if (weapon.WeaponType != WeaponType.Unknown) return weapon.WeaponType;

        string id = weapon.Id.ToLowerInvariant();
        if (id.Contains("bow")) return WeaponType.Bow;
        if (id.Contains("dagger")) return WeaponType.Dagger;
        if (id.Contains("sword") || id.Contains("rapier")) return WeaponType.Sword;
        return WeaponType.Unknown;
    }

    private static IEnumerable<Weapon> EquippedWeapons(Hero hero)
    {
        if (hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon main)
            yield return main;
        if (hero.Equipment.GetValueOrDefault(EquipmentSlot.OffHand) is Weapon off)
            yield return off;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Where a combination is allowed to happen (Game Idea.md: spells anytime,
/// weapons at a forge, abilities at a shrine).</summary>
public enum CombineLocation
{
    Anywhere,
    Forge,
    Shrine
}

/// <summary>
/// The merge/combine system — the signature mechanic (Game Idea.md). Takes two
/// <see cref="Combinable"/>s and produces a new one following the design's rules:
///   - same kind + same attributes  → intensify (Fireball + Fireball = Fireball+)
///   - otherwise                     → blend attributes (+ synergies), average rarity
///   - result kind                   → the "carrier" wins (Weapon > Armor > Item > Ability > Spell)
///   - rarity                        → average of the two (point-band model)
///   - level                         → min of the two, reset to 0 if rarity increased
/// Known named combos come from <see cref="RecipeBook"/>; everything else is generated.
/// </summary>
public static class CombinationEngine
{
    /// <summary>Whether two things can be combined at the given location, with a reason if not.</summary>
    public static bool CanCombine(Combinable a, Combinable b, CombineLocation location, out string reason)
    {
        if (a == null || b == null)
        {
            reason = "Two things are required to combine.";
            return false;
        }
        if (ReferenceEquals(a, b))
        {
            reason = "Cannot combine something with itself.";
            return false;
        }

        var required = RequiredLocation(ResultKind(a.Kind, b.Kind));
        if (required != CombineLocation.Anywhere && location != required)
        {
            reason = $"This combination requires a {required}.";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>Combine two things into a new one. Assumes <see cref="CanCombine"/> passed.</summary>
    public static Combinable Combine(Combinable a, Combinable b)
    {
        // Known named recipes take precedence over the generic rules.
        if (RecipeBook.TryGet(a, b, out var recipe))
            return recipe;

        bool sameKind = a.Kind == b.Kind;
        bool sameAttributes = a.Attributes.Count > 0 && a.Attributes.SetEquals(b.Attributes);
        // Same THING, not merely same shape: two different items that happen to share a kind and
        // attribute set (Leather Coat + Leather Gloves, both Armor {Light}) must Blend, not
        // Intensify into a free rarity bump of whichever came first. Identity is the base name
        // with any "+" suffix stripped, so X + X+ still intensifies.
        bool sameIdentity = string.Equals(
            a.Name.TrimEnd('+', ' '), b.Name.TrimEnd('+', ' '),
            StringComparison.OrdinalIgnoreCase);

        return (sameKind && sameIdentity && sameAttributes) ? Intensify(a, b) : Blend(a, b);
    }

    /// <summary>Same thing + same element → a stronger version of it (rarity bumps, level resets).</summary>
    private static Combinable Intensify(Combinable a, Combinable b)
    {
        var attrs = new HashSet<GameAttribute>(a.Attributes);
        var rarity = BumpUp(MaxRarity(a.Rarity, b.Rarity));
        string baseName = a.Name.TrimEnd('+', ' ');
        var result = MakeResult(a.Kind, a, b, attrs, rarity, level: 0,
            name: baseName + "+", id: Slug(baseName) + "-plus");
        result.Intensity = Math.Max(a.Intensity, b.Intensity) + 1;
        result.Description = $"An intensified {baseName}.";
        return result;
    }

    /// <summary>Different things → blend attributes and stats into a new hybrid.</summary>
    private static Combinable Blend(Combinable a, Combinable b)
    {
        var attrs = new HashSet<GameAttribute>(a.Attributes);
        attrs.UnionWith(b.Attributes);
        ApplySynergies(attrs);

        var kind = ResultKind(a.Kind, b.Kind);
        var rarity = AverageRarity(a.Rarity, b.Rarity);

        // Blends take the lower input level. (Game Idea.md's "rarity increase resets level"
        // rule can never fire on this path: AverageRarity is mathematically bounded by the
        // higher input rarity, so a blend never exceeds it — only Intensify raises rarity, and
        // it already builds its result at level 0.)
        int level = Math.Min(a.Level, b.Level);

        string name = $"{a.Name}-{b.Name} Fusion";
        return MakeResult(kind, a, b, attrs, rarity, level, name, Slug(name));
    }

    /// <summary>Construct the concrete result type for a kind, blending combat stats where present.</summary>
    private static Combinable MakeResult(CombinableKind kind, Combinable a, Combinable b,
        HashSet<GameAttribute> attrs, Rarity rarity, int level, string name, string id)
    {
        Combinable result = kind switch
        {
            CombinableKind.Weapon => new Weapon
            {
                WeaponType = WeaponTypeOf(a, b),
                BaseDamage = Math.Max(DamageOf(a), DamageOf(b)) + 2,
                Range = Math.Max(RangeOf(a), RangeOf(b)),
                Cooldown = Math.Max(6, Math.Min(CooldownOf(a), CooldownOf(b))),
                Animation = WeaponAnimationOf(a, b),
                HandsRequired = Math.Max(HandsRequiredOf(a), HandsRequiredOf(b))
            },
            CombinableKind.Spell => new Spell
            {
                BaseDamage = Math.Max(DamageOf(a), DamageOf(b)) + 2,
                Range = Math.Max(RangeOf(a), RangeOf(b)),
                Cooldown = Math.Max(6, Math.Min(CooldownOf(a), CooldownOf(b))),
                ManaCost = Math.Max(ManaCostOf(a), ManaCostOf(b))
            },
            CombinableKind.Ability => new Ability
            {
                Modifiers = MergeModifiers(a, b)
            },
            CombinableKind.Armor => new Armor
            {
                Slot = ArmorSlotOf(a, b),
                DefenseBonus = Math.Max(DefenseOf(a), DefenseOf(b)) + 1
            },
            _ => new Item
            {
                EquipSlot = ItemSlotOf(a, b),
                DefenseBonus = Math.Max(DefenseOf(a), DefenseOf(b))
            }
        };

        result.Id = id;
        result.Name = name;
        result.Rarity = rarity;
        result.Attributes = attrs;
        result.Level = level;
        result.Description = $"A combination of {a.Name} and {b.Name}.";
        return result;
    }

    // --- Rules helpers -----------------------------------------------------

    private static CombineLocation RequiredLocation(CombinableKind resultKind) => resultKind switch
    {
        CombinableKind.Weapon => CombineLocation.Forge,
        CombinableKind.Armor => CombineLocation.Forge,
        CombinableKind.Item => CombineLocation.Forge,
        CombinableKind.Ability => CombineLocation.Shrine,
        _ => CombineLocation.Anywhere // spells
    };

    // Carrier priority decides the result kind (a physical carrier beats a non-physical).
    private static int Priority(CombinableKind k) => k switch
    {
        CombinableKind.Weapon => 5,
        CombinableKind.Armor => 4,
        CombinableKind.Item => 3,
        CombinableKind.Ability => 2,
        CombinableKind.Spell => 1,
        _ => 0
    };

    private static CombinableKind ResultKind(CombinableKind a, CombinableKind b) =>
        Priority(a) >= Priority(b) ? a : b;

    private static void ApplySynergies(HashSet<GameAttribute> attrs)
    {
        if (attrs.Contains(GameAttribute.Fire) && attrs.Contains(GameAttribute.Shadow))
            attrs.Add(GameAttribute.Infernal);
        if (attrs.Contains(GameAttribute.Fire) && attrs.Contains(GameAttribute.Ice))
            attrs.Add(GameAttribute.Frost);
        if (attrs.Contains(GameAttribute.Air) && attrs.Contains(GameAttribute.Lightning))
            attrs.Add(GameAttribute.Storm);
    }

    // Rarity as a point band: Common=3, Uncommon=8, Rare=13, ... (band width 5).
    // Internal (not private) so other systems needing a consistent rarity->value scale (e.g.
    // the Overworld's sell-price formula) reuse this instead of duplicating the formula.
    internal static int RarityPoints(Rarity r) => (int)r * 5 + 3;

    private static Rarity RarityFromPoints(int points)
    {
        int band = Math.Clamp((points - 1) / 5, 0, (int)Rarity.Mythic);
        return (Rarity)band;
    }

    private static Rarity AverageRarity(Rarity a, Rarity b) =>
        RarityFromPoints((RarityPoints(a) + RarityPoints(b) + 1) / 2);

    private static Rarity BumpUp(Rarity r) => (Rarity)Math.Min((int)Rarity.Mythic, (int)r + 1);
    private static Rarity MaxRarity(Rarity a, Rarity b) => (Rarity)Math.Max((int)a, (int)b);

    // --- Combat-field accessors (only Weapon/Spell carry these) ------------

    private static int DamageOf(Combinable c) => c switch { Weapon w => w.BaseDamage, Spell s => s.BaseDamage, _ => 0 };
    private static float RangeOf(Combinable c) => c switch { Weapon w => w.Range, Spell s => s.Range, _ => 0f };
    private static int CooldownOf(Combinable c) => c switch { Weapon w => w.Cooldown, Spell s => s.Cooldown, _ => 20 };
    private static int ManaCostOf(Combinable c) => c switch { Spell s => s.ManaCost, _ => 0 };
    private static int HandsRequiredOf(Combinable c) => c is Weapon weapon ? weapon.HandsRequired : 1;
    private static WeaponType WeaponTypeOf(Combinable a, Combinable b) =>
        a is Weapon first && first.WeaponType != WeaponType.Unknown ? first.WeaponType
        : b is Weapon second ? second.WeaponType : WeaponType.Unknown;
    private static int DefenseOf(Combinable c) => c switch
    {
        Armor armor => armor.DefenseBonus,
        Item item => item.DefenseBonus,
        _ => 0
    };
    private static EquipmentSlot ArmorSlotOf(Combinable a, Combinable b) =>
        a is Armor first ? first.Slot : b is Armor second ? second.Slot : EquipmentSlot.Chest;
    private static EquipmentSlot? ItemSlotOf(Combinable a, Combinable b) =>
        a is Item first && first.EquipSlot.HasValue ? first.EquipSlot
        : b is Item second && second.EquipSlot.HasValue ? second.EquipSlot : null;

    private static AttackAnimation WeaponAnimationOf(Combinable a, Combinable b)
    {
        if (a is Weapon wa) return wa.Animation;
        if (b is Weapon wb) return wb.Animation;
        return AttackAnimation.Melee;
    }

    private static Dictionary<string, float> MergeModifiers(Combinable a, Combinable b)
    {
        var merged = new Dictionary<string, float>();
        foreach (var src in new[] { a, b }.OfType<Ability>())
            foreach (var kv in src.Modifiers)
                merged[kv.Key] = merged.TryGetValue(kv.Key, out var v) ? v + kv.Value : kv.Value;
        return merged;
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
}

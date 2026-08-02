using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Known, hand-authored combination results (the worked examples in Game Idea.md).
/// These take precedence over the generic <see cref="CombinationEngine"/> rules so
/// signature combos get their intended name, flavor, and stats. Anything not listed
/// here falls through to the generic engine.
/// </summary>
public static class RecipeBook
{
    private static readonly Dictionary<(string, string), Func<Combinable>> Recipes = new()
    {
        [Key("fireball", "ice-shard")] = () => new Spell
        {
            Id = "frostfireball", Name = "Frostfireball", Rarity = Rarity.Uncommon,
            BaseDamage = 12, Range = 3.2f, Cooldown = 20, ManaCost = 14,
            Attributes = { GameAttribute.Fire, GameAttribute.Ice, GameAttribute.Frost },
            Description = "Shoots a blue fireball that deals both fire and ice damage."
        },
        [Key("bow", "fireball")] = () => new Weapon
        {
            Id = "fire-bow", Name = "Fire Bow", Rarity = Rarity.Uncommon,
            BaseDamage = 10, Range = 4.0f, Cooldown = 16, Animation = AttackAnimation.Ranged,
            HandsRequired = 2,
            Attributes = { GameAttribute.Fire },
            Description = "Shoots fire arrows."
        },
        [Key("bow", "shield-generator")] = () => new Weapon
        {
            Id = "bubble-bow", Name = "Bubble Bow", Rarity = Rarity.Rare,
            BaseDamage = 8, Range = 4.0f, Cooldown = 18, Animation = AttackAnimation.Ranged,
            HandsRequired = 2,
            Attributes = { GameAttribute.Arcane },
            Description = "Arrows trap enemies in a shield bubble on hit."
        },
        [Key("dense-musculature", "fireball")] = () => new Ability
        {
            Id = "fire-fist", Name = "Fire Fist", Rarity = Rarity.Rare,
            Attributes = { GameAttribute.Fire },
            Modifiers = { ["MeleeFireDamage"] = 1f },
            Description = "Melee attacks deal fire damage."
        },
        [Key("fireball", "mana-circuitry")] = () => new Ability
        {
            Id = "fire-affinity", Name = "Fire Affinity", Rarity = Rarity.Rare,
            Attributes = { GameAttribute.Fire },
            Modifiers = { ["FireSpellDamagePct"] = 0.2f, ["FireSpellCostPct"] = -0.2f },
            Description = "Fire spells deal more damage and cost less mana."
        },
        [Key("dagger", "sword")] = () => new Weapon
        {
            Id = "rapier", Name = "Rapier", Rarity = Rarity.Uncommon,
            BaseDamage = 9, Range = 1.4f, Cooldown = 14, Animation = AttackAnimation.Quick,
            Attributes = { GameAttribute.Sharp },
            Description = "A dexterity-based blade: longer reach than a dagger, faster than a sword."
        },
    };

    public static bool TryGet(Combinable a, Combinable b, out Combinable result)
    {
        if (Recipes.TryGetValue(Key(a.Id, b.Id), out var factory))
        {
            result = factory();
            return true;
        }
        result = null!;
        return false;
    }

    // Order-independent key so (a,b) and (b,a) match the same recipe.
    private static (string, string) Key(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}

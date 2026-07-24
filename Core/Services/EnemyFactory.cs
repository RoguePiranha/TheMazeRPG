using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Generates enemies as full characters: a race, a class, and a level, from which their
/// stats (class starting distribution + per-level growth, scaled by race effectiveness) and
/// their gear (class starting loadout → attack) are derived. Damage then flows from those,
/// so a Warrior enemy is a tanky bruiser and a Mage enemy is a squishy caster automatically.
/// </summary>
public static class EnemyFactory
{
    // How common each class is among regular enemies (higher = more frequent).
    private static readonly Dictionary<string, int> RegularClassWeights = new()
    {
        ["Warrior"] = 4,
        ["Rogue"] = 4,
        ["Archer"] = 3,
        ["Mage Apprentice"] = 2,
        ["Priest"] = 2,
        ["Bard"] = 1,
        ["Wanderer"] = 1,
    };

    // Bosses favor the rarer / more specialized class lines.
    private static readonly Dictionary<string, int> BossClassWeights = new()
    {
        ["Mage Apprentice"] = 4,
        ["Priest"] = 4,
        ["Bard"] = 3,
        ["Wanderer"] = 2,
        ["Archer"] = 2,
        ["Rogue"] = 1,
        ["Warrior"] = 1,
    };

    /// <summary>Level range for regular enemies on a floor. Boss uses the top of this + 1.</summary>
    public static (int min, int max) LevelRange(int floor) => (floor, floor + 2);

    /// <summary>A random regular enemy for a floor (weighted class, random race, random level in range).</summary>
    public static Enemy RandomRegular(int floor, CharacterDataService cds, Random rng)
    {
        var (min, max) = LevelRange(floor);
        int level = rng.Next(min, max + 1);
        return Create(PickWeighted(RegularClassWeights, rng), PickRace(cds, rng), level, isBoss: false, cds, rng);
    }

    /// <summary>The floor boss: rarer class, top level for the floor.</summary>
    public static Enemy RandomBoss(int floor, CharacterDataService cds, Random rng)
    {
        int level = LevelRange(floor).max + 1;
        return Create(PickWeighted(BossClassWeights, rng), PickRace(cds, rng), level, isBoss: true, cds, rng);
    }

    /// <summary>Build an enemy of a specific class/race/level.</summary>
    public static Enemy Create(string className, string raceName, int level, bool isBoss,
        CharacterDataService cds, Random rng)
    {
        cds.Classes.TryGetValue(className, out var classData);
        cds.Races.TryGetValue(raceName, out var race);

        // Effective stat = (class starting value + class growth*(level-1) + a small even growth),
        // scaled by the race's effectiveness for that stat, then rounded. (Race effectiveness is
        // baked in here so combat formulas can use the stored values directly.)
        int Stat(string name)
        {
            int start = classData?.StartingStats.GetValueOrDefault(name, 1) ?? 1;
            int growth = classData?.StatGrowth.GetValueOrDefault(name, 0) ?? 0;
            int even = (level - 1) / 3; // non-primary stats still rise slowly with level
            float grown = start + growth * (level - 1) + even;
            float eff = race?.Effectiveness.GetValueOrDefault(name, 1f) ?? 1f;
            return Math.Max(1, (int)MathF.Round(grown * eff));
        }

        int str = Stat("Strength");
        int con = Stat("Constitution");
        int agi = Stat("Agility");
        int dex = Stat("Dexterity");
        int intel = Stat("Intelligence");
        int wis = Stat("Wisdom");
        int cha = Stat("Charisma");

        // Primary attack from the class's starting loadout (light attack).
        var attacks = AttackFactory.ToAttacks(AttackFactory.GetStartingLoadout(className));
        var primary = attacks.Count > 0 ? attacks[0] : null;

        // Derived combat values (Constitution → health/defense; a small flat attack scales with level).
        int maxHp = 30 + con * 5 + level * 6;
        if (isBoss) maxHp = (int)(maxHp * 2.2f);

        var enemy = new Enemy
        {
            Level = level,
            Class = className,
            Race = raceName,
            Type = className,
            IsBoss = isBoss,
            Strength = str,
            Constitution = con,
            Agility = agi,
            Dexterity = dex,
            Intelligence = intel,
            Wisdom = wis,
            Charisma = cha,
            MaxHp = maxHp,
            Hp = maxHp,
            Attack = 2 + level,
            Defense = 1 + con / 3 + level / 2,
            CurrentAttack = primary,
            AttackRange = primary?.Range ?? 1.0f,
            // Enemies attack a bit slower than an optimized hero.
            AttackSpeed = (primary?.Cooldown ?? 25) + 15,
            NoiseOffsetX = rng.NextDouble() * 100,
            NoiseOffsetY = rng.NextDouble() * 100,
            Radius = isBoss ? 0.45f : 0.35f,
        };
        return enemy;
    }

    private static string PickWeighted(Dictionary<string, int> weights, Random rng)
    {
        int total = weights.Values.Sum();
        int roll = rng.Next(total);
        foreach (var kv in weights)
        {
            roll -= kv.Value;
            if (roll < 0) return kv.Key;
        }
        return weights.Keys.First();
    }

    private static string PickRace(CharacterDataService cds, Random rng)
    {
        var races = cds.Races.Keys.ToList();
        return races.Count > 0 ? races[rng.Next(races.Count)] : "Human";
    }
}

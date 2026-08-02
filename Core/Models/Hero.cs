using System;
using System.Collections.Generic;
using System.Linq;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents the player's hero character
/// </summary>
public class Hero
{
    // Basic Info
    public string Name { get; set; } = "Wanderer";
    public string Class { get; set; } = "Wanderer";
    public string Race { get; set; } = "Human";
    public int Level { get; set; } = 1;
    
    // Visual
    public string ClassColor { get; set; } = "#808080"; // Outer ring color
    public string RaceColor { get; set; } = "#FFC0CB";  // Inner circle color
    
    // Class data reference for stat growth
    public CharacterClass? ClassData { get; set; }
    
    // Core Stats (based on game design doc)
    public int Strength { get; set; } = 1;      // Melee Damage, Carry Limits, Knockback
    public int Constitution { get; set; } = 1;   // Health, Defense, Resistances
    public int Agility { get; set; } = 1;        // Movement Speed, Dodge, Stealth
    public int Dexterity { get; set; } = 1;      // Attack Speed, Accuracy, Crit Rate
    public int Intelligence { get; set; } = 1;   // Magic Damage, Cooldown, Mana
    public int Wisdom { get; set; } = 1;         // Magic Resist, Healing, Faith
    public int Charisma { get; set; } = 1;       // NPC Interaction, Followers

    // Manual stat allocation: every level grants StatPointsPerLevel points the player assigns by
    // hand (GameState.SpendStatPoint) from the stats screen — there is no class auto-allocation.
    // Classes differ by their starting stats, not by per-level growth.
    public const int StatPointsPerLevel = 5;
    public int UnspentStatPoints { get; set; }

    // Racial effectiveness multipliers (set from race). Base stats above are what the character
    // sheet displays; the Effective* values below are what derived formulas use.
    // See Info/Racial Effectiveness.md.
    public float StrengthEffectiveness { get; set; } = 1f;
    public float ConstitutionEffectiveness { get; set; } = 1f;
    public float AgilityEffectiveness { get; set; } = 1f;
    public float DexterityEffectiveness { get; set; } = 1f;
    public float IntelligenceEffectiveness { get; set; } = 1f;
    public float WisdomEffectiveness { get; set; } = 1f;
    public float CharismaEffectiveness { get; set; } = 1f;

    // Effective attributes (base × effectiveness), kept as floats — not rounded here.
    public float EffectiveStrength => Strength * StrengthEffectiveness;
    public float EffectiveConstitution => Constitution * ConstitutionEffectiveness;
    public float EffectiveAgility => Agility * AgilityEffectiveness;
    public float EffectiveDexterity => Dexterity * DexterityEffectiveness;
    public float EffectiveIntelligence => Intelligence * IntelligenceEffectiveness;
    public float EffectiveWisdom => Wisdom * WisdomEffectiveness;
    public float EffectiveCharisma => Charisma * CharismaEffectiveness;
    
    // Derived Stats
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Experience { get; set; }
    public int ExperienceToNext { get; set; } = 100;
    
    // Resources for attacks
    public int CurrentStamina { get; set; }
    public int MaxStamina { get; set; } = 100;
    public int CurrentMana { get; set; }
    public int MaxMana { get; set; } = 100;
    public int CurrentFaith { get; set; }
    public int MaxFaith { get; set; } = 100;
    public int StaminaRegen { get; set; } = 2;  // Per tick
    public int ManaRegen { get; set; } = 1;      // Per tick
    public int FaithRegen { get; set; } = 1;     // Per tick
    public int HealthRegen { get; set; } = 0;    // HP per tick (10 ticks = 1 second)
    
    // Use floats for smooth sub-grid movement
    public float X { get; set; }
    public float Y { get; set; }
    // Collision radius in tiles (used for hitbox-based interactions)
    public float Radius { get; set; } = 0.35f;
    
    // Grid position for collision checks
    public int GridX => (int)Math.Round(X);
    public int GridY => (int)Math.Round(Y);
    
    public bool IsAlive => CurrentHp > 0;
    
    // Combat state
    public bool InCombat { get; set; }
    public int AttackSpeed { get; set; } = 30; // Ticks between attacks
    public int AttackCooldown { get; set; }

    // Attack system. Loadout is retained as the save-compatible name for optional slotted spells;
    // class techniques are derived from class/level and physical gear lives in Equipment.
    public List<Combinable> Loadout { get; set; } = new();
    public List<Attack> Attacks { get; set; } = new();
    public Attack? CurrentAttack { get; set; }

    // Inventory = carried gear that is not worn, held, or slotted as a spell.
    public List<Combinable> Inventory { get; set; } = new();
    public int HotbarCapacity { get; set; } = 4;

    public Dictionary<EquipmentSlot, Combinable> Equipment { get; set; } = new();

    // Granted by the future skill system. Class affinities and learned training are checked
    // independently so either one is enough to use a weapon without unfamiliarity penalties.
    public HashSet<WeaponType> WeaponTraining { get; set; } = new();

    public int EquipmentDefenseBonus => Equipment.Values.Sum(item => item switch
    {
        Armor armor => armor.DefenseBonus,
        Item accessory => accessory.DefenseBonus,
        _ => 0
    });

    public int EquippedWeaponDamage
    {
        get
        {
            int main = Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon mainWeapon
                ? mainWeapon.BaseDamage : 0;
            int off = Equipment.GetValueOrDefault(EquipmentSlot.OffHand) is Weapon offWeapon
                ? Math.Max(1, offWeapon.BaseDamage / 2) : 0;
            return main + off;
        }
    }

    // Overworld: raw/refined materials (ore, ingots, ...), keyed by material id. Stackable/
    // countable, unlike Inventory's unique Combinables. Written only via
    // GameState.AddHeroResource, which validates the id against MaterialDataService — this stays
    // a plain dictionary here since Hero (a Model) doesn't depend on Services.
    public Dictionary<string, int> Resources { get; set; } = new();

    // Overworld currency, earned by selling items at a Stall.
    public int Gold { get; set; } = 0;

    // Elemental affinity profile — drives magic damage, mana cost, resistance, and learnable
    // spell tier per element, and grows with use (see AffinityService). Seeded from race+class.
    public Affinities Affinities { get; set; } = new();

    // Animation state for combat movement
    public float AnimationOffsetX { get; set; }
    public float AnimationOffsetY { get; set; }
    
    public void GainExperience(int amount)
    {
        // Charisma boosts experience gains (effective value)
        int bonusXP = (int)(amount * (1.0f + EffectiveCharisma * 0.05f));
        Experience += bonusXP;
        while (Experience >= ExperienceToNext)
        {
            LevelUp();
        }
    }
    
    private void LevelUp()
    {
        Level++;
        Experience -= ExperienceToNext;
        // Quadratic XP curve per Levels and Stats.xlsx: XP to reach level L = 100 * L^2.
        ExperienceToNext = 100 * Level * Level;
        
        // Stat gains per level (influenced by effective core stats)
        int hpGain = 10 + (int)EffectiveConstitution + (Level % 5 == 0 ? 10 : 0); // Bonus HP every 5 levels
        MaxHp += hpGain;
        CurrentHp = MaxHp; // Full heal on level up

        Attack += 2 + (int)(EffectiveStrength / 2) + (Level % 3 == 0 ? 2 : 0); // Bonus attack every 3 levels
        Defense += 1 + (int)(EffectiveConstitution / 3) + (Level % 4 == 0 ? 2 : 0); // Bonus defense every 4 levels

        // Unlock new attack at milestones
        if (Level == 5)
        {
            Attacks.Add(new Attack { Id = "power-strike", Name = "Power Strike", Damage = 18, Range = 1.2f, Cooldown = 28, Animation = AttackAnimation.Heavy, CritChance = 0.12f, Description = "A heavy blow with bonus crit." });
        }
        if (Level == 10)
        {
            Attacks.Add(new Attack { Id = "quick-jab", Name = "Quick Jab", Damage = 12, Range = 1.0f, Cooldown = 16, Animation = AttackAnimation.Quick, CritChance = 0.18f, Description = "A rapid jab with high crit chance." });
        }
        if (Level == 15)
        {
            // Basic "X Bolt" magic attack (naming convention: Mana Bolt / Fire Bolt / Ice Bolt).
            // Distinct id from the Mage's "arcane-blast" so visuals/behavior keyed off Attack.Id
            // don't conflate it with that AoE spell.
            Attacks.Add(new Attack { Id = "mana-bolt", Name = "Mana Bolt", Damage = 22, Range = 2.0f, Cooldown = 32, Animation = AttackAnimation.Magic, CritChance = 0.10f, Description = "A ranged magic attack." });
        }

        // Core stats are no longer auto-allocated by class. Each level grants a pool of points
        // the player assigns by hand from the stats screen (GameState.SpendStatPoint). ClassData's
        // StatGrowth table is intentionally unused here now (classes differ by starting stats).
        UnspentStatPoints += StatPointsPerLevel;

        // Satisfying level-up feedback (animation/sound placeholder)
        // TODO: Trigger level-up animation and sound effect in UI layer
    }
}

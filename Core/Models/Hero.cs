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

    // Compatibility default. Runtime class definitions split this budget between automatic
    // class-weighted gains and points the player assigns from the character screen.
    public const int StatPointsPerLevel = 4;
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

    // Persisted slot-based class, profession, and skill progression.
    public ProgressionState Progression { get; set; } = new();

    // Animation state for combat movement
    public float AnimationOffsetX { get; set; }
    public float AnimationOffsetY { get; set; }
    
}

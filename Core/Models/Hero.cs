using System;
using System.Collections.Generic;

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
    
    // Derived Stats
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Experience { get; set; }
    public int ExperienceToNext { get; set; } = 100;
    
    // Use floats for smooth sub-grid movement
    public float X { get; set; }
    public float Y { get; set; }
    
    // Grid position for collision checks
    public int GridX => (int)Math.Round(X);
    public int GridY => (int)Math.Round(Y);
    
    public bool IsAlive => CurrentHp > 0;
    
    // Combat state
    public bool InCombat { get; set; }
    public int AttackSpeed { get; set; } = 30; // Ticks between attacks
    public int AttackCooldown { get; set; }
    
    // Attack system
    public List<Attack> Attacks { get; set; } = new();
    public Attack? CurrentAttack { get; set; }
    
    // Animation state for combat movement
    public float AnimationOffsetX { get; set; }
    public float AnimationOffsetY { get; set; }
    
    public void GainExperience(int amount)
    {
        Experience += amount;
        while (Experience >= ExperienceToNext)
        {
            LevelUp();
        }
    }
    
    private void LevelUp()
    {
        Level++;
        Experience -= ExperienceToNext;
        ExperienceToNext = (int)(ExperienceToNext * 1.5f);
        
        // Stat gains per level (influenced by core stats)
        int hpGain = 10 + Constitution;
        MaxHp += hpGain;
        CurrentHp = MaxHp; // Full heal on level up
        
        Attack += 2 + (Strength / 2);
        Defense += 1 + (Constitution / 3);
        
        // Increase core stats based on class stat growth
        if (ClassData?.StatGrowth != null)
        {
            if (ClassData.StatGrowth.TryGetValue("Strength", out int strGrowth))
                Strength += strGrowth;
            if (ClassData.StatGrowth.TryGetValue("Constitution", out int conGrowth))
                Constitution += conGrowth;
            if (ClassData.StatGrowth.TryGetValue("Dexterity", out int dexGrowth))
                Dexterity += dexGrowth;
            if (ClassData.StatGrowth.TryGetValue("Agility", out int agiGrowth))
                Agility += agiGrowth;
            if (ClassData.StatGrowth.TryGetValue("Intelligence", out int intGrowth))
                Intelligence += intGrowth;
            if (ClassData.StatGrowth.TryGetValue("Wisdom", out int wisGrowth))
                Wisdom += wisGrowth;
            if (ClassData.StatGrowth.TryGetValue("Charisma", out int chaGrowth))
                Charisma += chaGrowth;
        }
        else
        {
            // Fallback to +1 per stat if no class data
            Strength += 1;
            Constitution += 1;
            Dexterity += 1;
            Agility += 1;
            Intelligence += 1;
            Wisdom += 1;
            Charisma += 1;
        }
    }
}

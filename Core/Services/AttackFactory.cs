using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Manages attack creation and assignment based on class and equipment
/// </summary>
public static class AttackFactory
{
    public static List<Attack> GetStartingAttacks(string className)
    {
        var attacks = new List<Attack>();
        
        switch (className)
        {
            case "Warrior":
                // Light attack - fast, no cost
                attacks.Add(new Attack
                {
                    Name = "Quick Slash",
                    Damage = 6,
                    Range = 1.2f,
                    Cooldown = 15,
                    Animation = AttackAnimation.Melee,
                    Description = "A quick sword strike",
                    CritChance = 0.1f,
                    KnockbackDistance = 0.2f
                });
                // Heavy attack - powerful, uses stamina
                attacks.Add(new Attack
                {
                    Name = "Heavy Cleave",
                    Damage = 15,
                    Range = 1.2f,
                    Cooldown = 40,
                    Animation = AttackAnimation.Heavy,
                    Description = "A devastating sword strike",
                    CritChance = 0.15f,
                    KnockbackDistance = 0.5f,
                    StaminaCost = 25
                });
                break;
                
            case "Mage":
            case "Mage Apprentice":
                // Light attack - basic magic
                attacks.Add(new Attack
                {
                    Name = "Magic Dart",
                    Damage = 5,
                    Range = 3.0f,
                    Cooldown = 18,
                    Animation = AttackAnimation.Magic,
                    Description = "A small bolt of arcane energy",
                    CritChance = 0.1f
                });
                // Heavy attack - powerful spell, uses mana
                attacks.Add(new Attack
                {
                    Name = "Arcane Blast",
                    Damage = 18,
                    Range = 3.5f,
                    Cooldown = 50,
                    Animation = AttackAnimation.Magic,
                    Description = "A devastating arcane explosion",
                    CritChance = 0.25f,
                    ManaCost = 30
                });
                break;
                
            case "Rogue":
                // Light attack - fast strikes
                attacks.Add(new Attack
                {
                    Name = "Quick Stab",
                    Damage = 7,
                    Range = 1.0f,
                    Cooldown = 12,
                    Animation = AttackAnimation.Quick,
                    Description = "A rapid strike",
                    CritChance = 0.2f
                });
                // Heavy attack - backstab, uses stamina
                attacks.Add(new Attack
                {
                    Name = "Devastating Backstab",
                    Damage = 20,
                    Range = 1.0f,
                    Cooldown = 35,
                    Animation = AttackAnimation.Quick,
                    Description = "A lethal strike from the shadows",
                    CritChance = 0.4f,
                    StaminaCost = 20
                });
                break;
                
            case "Cleric":
            case "Priest":
                // Light attack - basic holy damage
                attacks.Add(new Attack
                {
                    Name = "Holy Touch",
                    Damage = 5,
                    Range = 1.5f,
                    Cooldown = 20,
                    Animation = AttackAnimation.Melee,
                    Description = "A blessed strike",
                    CritChance = 0.1f
                });
                // Heavy attack - divine wrath, uses faith
                attacks.Add(new Attack
                {
                    Name = "Divine Wrath",
                    Damage = 16,
                    Range = 2.0f,
                    Cooldown = 45,
                    Animation = AttackAnimation.Magic,
                    Description = "Unleash holy fury",
                    CritChance = 0.15f,
                    FaithCost = 25
                });
                break;
                
            case "Ranger":
            case "Archer":
                // Light attack - quick arrow
                attacks.Add(new Attack
                {
                    Name = "Quick Shot",
                    Damage = 6,
                    Range = 4.0f,
                    Cooldown = 15,
                    Animation = AttackAnimation.Ranged,
                    Description = "A rapid arrow",
                    CritChance = 0.15f
                });
                // Heavy attack - power shot, uses stamina
                attacks.Add(new Attack
                {
                    Name = "Power Shot",
                    Damage = 17,
                    Range = 5.0f,
                    Cooldown = 38,
                    Animation = AttackAnimation.Ranged,
                    Description = "A devastating arrow",
                    CritChance = 0.3f,
                    StaminaCost = 22
                });
                break;
                
            case "Bard":
                // Light attack - sound wave
                attacks.Add(new Attack
                {
                    Name = "Sound Wave",
                    Damage = 5,
                    Range = 2.0f,
                    Cooldown = 18,
                    Animation = AttackAnimation.Magic,
                    Description = "A small sonic burst",
                    KnockbackDistance = 0.3f
                });
                // Heavy attack - sonic boom, uses mana
                attacks.Add(new Attack
                {
                    Name = "Sonic Boom",
                    Damage = 14,
                    Range = 2.5f,
                    Cooldown = 42,
                    Animation = AttackAnimation.Magic,
                    Description = "A powerful shockwave",
                    KnockbackDistance = 0.8f,
                    ManaCost = 20
                });
                break;
                
            default: // Wanderer and others
                // Light attack
                attacks.Add(new Attack
                {
                    Name = "Light Punch",
                    Damage = 4,
                    Range = 1.0f,
                    Cooldown = 15,
                    Animation = AttackAnimation.Melee,
                    Description = "A basic punch",
                    CritChance = 0.05f
                });
                // Heavy attack - uses stamina
                attacks.Add(new Attack
                {
                    Name = "Heavy Strike",
                    Damage = 12,
                    Range = 1.0f,
                    Cooldown = 35,
                    Animation = AttackAnimation.Heavy,
                    Description = "A powerful strike",
                    CritChance = 0.1f,
                    StaminaCost = 18
                });
                break;
        }
        
        return attacks;
    }
}

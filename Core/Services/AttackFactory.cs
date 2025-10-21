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
                attacks.Add(new Attack
                {
                    Name = "Slash",
                    Damage = 8,
                    Range = 1.2f,
                    Cooldown = 25,
                    Animation = AttackAnimation.Melee,
                    Description = "A powerful sword strike",
                    CritChance = 0.1f,
                    KnockbackDistance = 0.3f
                });
                attacks.Add(new Attack
                {
                    Name = "Parry",
                    Damage = 3,
                    Range = 1.0f,
                    Cooldown = 40,
                    Animation = AttackAnimation.Quick,
                    Description = "Block and counter",
                    ParryChance = 0.5f
                });
                break;
                
            case "Mage":
            case "Mage Apprentice":
                attacks.Add(new Attack
                {
                    Name = "Magic Missile",
                    Damage = 7,
                    Range = 3.0f,
                    Cooldown = 30,
                    Animation = AttackAnimation.Magic,
                    Description = "A bolt of arcane energy",
                    CritChance = 0.15f
                });
                break;
                
            case "Rogue":
                attacks.Add(new Attack
                {
                    Name = "Backstab",
                    Damage = 10,
                    Range = 1.0f,
                    Cooldown = 20,
                    Animation = AttackAnimation.Quick,
                    Description = "A quick strike from the shadows",
                    CritChance = 0.25f
                });
                attacks.Add(new Attack
                {
                    Name = "Poison Dart",
                    Damage = 5,
                    Range = 2.5f,
                    Cooldown = 35,
                    Animation = AttackAnimation.Ranged,
                    Description = "A ranged poison attack"
                });
                break;
                
            case "Cleric":
            case "Priest":
                attacks.Add(new Attack
                {
                    Name = "Holy Smite",
                    Damage = 7,
                    Range = 1.5f,
                    Cooldown = 28,
                    Animation = AttackAnimation.Melee,
                    Description = "Divine judgment",
                    CritChance = 0.1f
                });
                break;
                
            case "Ranger":
            case "Archer":
                attacks.Add(new Attack
                {
                    Name = "Bow Shot",
                    Damage = 8,
                    Range = 4.0f,
                    Cooldown = 22,
                    Animation = AttackAnimation.Ranged,
                    Description = "A precise arrow",
                    CritChance = 0.2f
                });
                attacks.Add(new Attack
                {
                    Name = "Quick Strike",
                    Damage = 6,
                    Range = 1.0f,
                    Cooldown = 18,
                    Animation = AttackAnimation.Quick,
                    Description = "A fast melee attack"
                });
                break;
                
            case "Bard":
                attacks.Add(new Attack
                {
                    Name = "Sonic Blast",
                    Damage = 6,
                    Range = 2.0f,
                    Cooldown = 26,
                    Animation = AttackAnimation.Magic,
                    Description = "A wave of sound",
                    KnockbackDistance = 0.5f
                });
                break;
                
            default: // Wanderer and others
                attacks.Add(new Attack
                {
                    Name = "Unarmed Strike",
                    Damage = 5,
                    Range = 1.0f,
                    Cooldown = 30,
                    Animation = AttackAnimation.Melee,
                    Description = "A basic punch or kick",
                    CritChance = 0.05f
                });
                break;
        }
        
        return attacks;
    }
}

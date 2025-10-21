namespace TheMazeRPG.Core.Models;

/// <summary>
/// Represents an enemy in the maze
/// </summary>
public class Enemy
{
    public float X { get; set; }
    public float Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Level { get; set; } = 1;
    
    // Stats
    public int Strength { get; set; } = 1;
    public int Constitution { get; set; } = 1;
    public int Agility { get; set; } = 1;
    public int Dexterity { get; set; } = 1;
    
    // For Perlin-based movement
    public double NoiseOffsetX { get; set; }
    public double NoiseOffsetY { get; set; }
    
    public bool IsAlive => Hp > 0;
    public string Type { get; set; } = "Slime";
    public string Class { get; set; } = "Brute"; // Enemy class type
    
    // Combat state
    public bool InCombat { get; set; }
    public int AttackSpeed { get; set; } = 40; // Ticks between attacks
    public int AttackCooldown { get; set; }
    public float AttackRange { get; set; } = 1.0f; // How close they need to be to attack
    
    // Smooth movement for combat
    public float TargetX { get; set; }
    public float TargetY { get; set; }

    // Animation state for attack movement
    public float AnimationOffsetX { get; set; }
    public float AnimationOffsetY { get; set; }
}

namespace TheMazeRPG.Core.Models;

/// <summary>Color/emphasis category for a floating combat text.</summary>
public enum FloatingTextKind
{
    EnemyDamage,  // damage the hero deals — white
    HeroDamage,   // damage the hero takes — red
    Dodge,        // an evaded hit — cyan
    LevelUp,      // green, larger
    Heal          // restored HP — green
}

/// <summary>
/// A short-lived piece of world-space text ("14", "Dodge!", "LEVEL UP!") that rises and fades
/// over an entity when combat happens to it. Render-only feedback — nothing in the sim reads it.
/// </summary>
public class FloatingText
{
    public const int MaxAge = 32; // ticks (~3.2s at 10 tps)

    public string Text = "";
    public float X;           // world tiles
    public float Y;
    public FloatingTextKind Kind;
    public int Age;

    public bool Expired => Age >= MaxAge;
    /// <summary>1 → 0 over the lifetime (renderer uses this for fade + rise).</summary>
    public float LifeT => 1f - Age / (float)MaxAge;
}

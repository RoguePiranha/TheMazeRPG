namespace TheMazeRPG.Core.Models;

/// <summary>What an ambient critter is (drives look + behavior numbers).</summary>
public enum CritterKind
{
    Rat,   // dungeon: darts between pauses, flees the hero
    Bat,   // dungeon: drifts continuously, never pauses
    Dog,   // town: ambles between long pauses
    Cat    // town: same, slower and smaller
}

/// <summary>
/// A purely ambient creature — rats and bats scurrying through the dungeon, a dog and a cat
/// living in the town. No HP, no combat, no interaction: they exist to make spaces feel
/// inhabited. Updated from their own RNG stream so they never perturb gameplay determinism,
/// and only spawned at all when GameState.EnableAmbience is on (the live game, not headless demos).
/// </summary>
public class Critter
{
    public CritterKind Kind;
    public float X, Y;
    public float TargetX, TargetY;
    public int PauseTicks;

    public float Speed => Kind switch
    {
        CritterKind.Rat => 0.14f,   // quick darts
        CritterKind.Bat => 0.10f,
        CritterKind.Dog => 0.05f,
        _ => 0.045f                 // Cat
    };
}

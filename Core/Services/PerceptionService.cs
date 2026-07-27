using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Chances for noticing hidden features (traps) and disarming them. Formulas are deliberately
/// written as <c>base + statTerm + abilityBonus</c> so future passive abilities (Detect Traps,
/// Disarm Traps) and proficiencies can plug into the <c>abilityBonus</c> hooks without touching
/// the core math. Perception is driven by Wisdom, disarm by Dexterity (owner's call — for now).
/// All values are provisional/tunable.
/// </summary>
public static class PerceptionService
{
    // Perception (spotting a hidden trap). Rolled per tick while the hero is within range with LOS,
    // so the chance accumulates as they approach — closer is easier, deeper floors are stealthier.
    private const float SpotBasePerTick = 0.006f;   // ~ a slow baseline notice rate up close
    private const float SpotPerWisdom = 0.0015f;    // each effective Wisdom point helps
    private const float SpotFloorPenalty = 0.0004f; // deeper traps are better hidden
    public const float PerceptionRadius = 4.0f;     // tiles within which a trap can be noticed

    /// <summary>Per-tick chance the perceiver notices a hidden feature at the given distance/floor.
    /// Scales down with distance across <see cref="PerceptionRadius"/>.</summary>
    public static float SpotChancePerTick(float perceiverWisdom, float distance, int floor)
    {
        if (distance > PerceptionRadius) return 0f;
        float proximity = 1f - (distance / PerceptionRadius); // 1 adjacent → 0 at the edge
        float raw = (SpotBasePerTick + perceiverWisdom * SpotPerWisdom - floor * SpotFloorPenalty)
                    * proximity
                    + DetectAbilityBonus();
        return System.Math.Clamp(raw, 0f, 0.5f);
    }

    // Disarm (removing a spotted trap). One roll per attempt.
    private const float DisarmBase = 0.35f;
    private const float DisarmPerDexterity = 0.03f;
    private const float DisarmFloorPenalty = 0.02f;

    /// <summary>Chance a disarm attempt succeeds. Failure is handled by the caller (may spring it).</summary>
    public static float DisarmChance(float perceiverDexterity, int floor)
    {
        float raw = DisarmBase + perceiverDexterity * DisarmPerDexterity - floor * DisarmFloorPenalty
                    + DisarmAbilityBonus();
        return System.Math.Clamp(raw, 0.05f, 0.95f);
    }

    /// <summary>Chance a *failed* disarm actually triggers the trap (vs. a harmless fumble).</summary>
    public const float DisarmFailSpringChance = 0.5f;

    // --- Extension hooks: future Detect/Disarm abilities and proficiencies add their bonuses here.
    private static float DetectAbilityBonus() => 0f;
    private static float DisarmAbilityBonus() => 0f;
}

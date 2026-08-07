using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Curves and constants for enemy perception (Planning note 11 §2 — the awareness core, landed
/// ahead of the sneak mode that will consume it). Replaces binary 360° detection: an enemy SEES
/// through a facing cone with line of sight, HEARS all around, and escalates Unaware →
/// Suspicious → Alert instead of flipping to combat the moment a range check passes.
///
/// Stat philosophy shared with PerceptionService: Wisdom perceives (a wise enemy's awareness
/// climbs faster), Agility will hide when sneaking lands. Tuning contract (note 11): cone +
/// footstep hearing together ≈ the old instant detection for a non-sneaking hero at close range —
/// walking up to something's face still gets noticed fast; what changed is walls, backs, and
/// distance actually mattering.
/// </summary>
public static class AwarenessService
{
    /// <summary>Full width of the vision cone, radians (≈120°).</summary>
    public const float VisionConeRad = 2.1f;

    /// <summary>Awareness gained per second by a clearly seen, adjacent, unhidden target.</summary>
    public const float SightGainPerSecond = 60f;

    /// <summary>Awareness lost per second with no stimulus this tick.</summary>
    public const float DecayPerSecond = 8f;

    /// <summary>Inside this range the target is simply noticed — you can't be unaware of someone
    /// brushing against you, whatever direction you were facing.</summary>
    public const float BumpRange = 1.5f;

    public const float SuspiciousThreshold = 30f;
    public const float AlertThreshold = 70f;

    /// <summary>Non-combat sounds never alert on their own — they cap just under Alert, so a
    /// footstep turns heads and starts an investigation but only sight (or violence) commits.</summary>
    public const float HearingCap = 69f;
    public const float HearingGain = 35f;

    /// <summary>Walls muffle: a sound with no line of sight to the listener carries to only this
    /// fraction of its radius. Cheap v1 occlusion — no per-sound BFS.</summary>
    public const float WallMuffle = 0.6f;

    /// <summary>How long an investigator stands at the noise looking around before giving up
    /// (~3 s at 30 tps), and how far one walk step sound carries.</summary>
    public const int InvestigateSweepTicks = 90;
    public const float StepRadius = 4.0f;
    public const float DashRadius = 7.0f;
    public const float DoorRadius = 5.0f;
    public const float AttackRadius = 5.0f;
    public const float StruckScreamRadius = 6.0f;
    public const float TrapRadius = 8.0f;
    public const int StepIntervalTicks = 5;

    // ---- Sneak mode (note 11 §3, the player half) ----

    /// <summary>Sneak movement speed as a share of walking speed.</summary>
    public const float SneakSpeedMultiplier = 0.55f;

    /// <summary>Sneak steps: quieter and rarer than walking's 4.0-radius-every-5.</summary>
    public const float SneakStepRadius = 1.5f;
    public const int SneakStepIntervalTicks = 8;

    /// <summary>Gear weight on step noise: each equipped Heavy piece multiplies the radius, each
    /// Light piece shaves it, clamped so gear can neither silence a warhorse nor ring like a
    /// cathedral. When armor becomes real equipment its weight class dominates this term.</summary>
    public const float HeavyGearNoise = 1.4f;
    public const float LightGearNoise = 0.85f;
    public const float MinGearNoise = 0.6f;
    public const float MaxGearNoise = 2.0f;

    /// <summary>Unaware target struck: note 07's backstab ×1.6 with the auto-crit ×1.5 on top —
    /// one devastating opening. The blow's own noise then wakes the neighbourhood, so it's one
    /// free hit, not a massacre loop (unless one hit kills, which is the assassin fantasy
    /// working as intended).</summary>
    public const float StealthStrikeMultiplier = 2.4f;

    /// <summary>Product of the noise attributes across worn/held gear.</summary>
    public static float GearNoiseMultiplier(IEnumerable<Combinable> loadout)
    {
        float multiplier = 1f;
        foreach (var piece in loadout)
        {
            if (piece.HasAttribute(GameAttribute.Heavy)) multiplier *= HeavyGearNoise;
            if (piece.HasAttribute(GameAttribute.Light)) multiplier *= LightGearNoise;
        }
        return Math.Clamp(multiplier, MinGearNoise, MaxGearNoise);
    }

    /// <summary>
    /// How much of the hero's visibility sneaking hides: 0 = fully visible (not sneaking),
    /// up to 0.9 for a high-Agility, lightly-geared crouch. Agility hides (the counterpart of
    /// Wisdom perceiving); clanking gear gives some of it back.
    /// </summary>
    public static float StealthFactor(bool sneaking, float agility, float gearNoiseMultiplier)
    {
        if (!sneaking) return 0f;
        float gearPenalty = Math.Max(0f, (gearNoiseMultiplier - 1f) * 0.5f);
        return Math.Clamp(0.5f + agility * 0.02f - gearPenalty, 0f, 0.9f);
    }

    /// <summary>Is the target inside the watcher's facing cone?</summary>
    public static bool InVisionCone(float facingRad, float fromX, float fromY, float toX, float toY)
    {
        float toTarget = MathF.Atan2(toY - fromY, toX - fromX);
        float diff = MathF.Abs(toTarget - facingRad);
        if (diff > MathF.PI) diff = 2f * MathF.PI - diff;
        return diff <= VisionConeRad / 2f;
    }

    /// <summary>
    /// Sight-driven awareness gain for one tick: close targets register almost immediately, ones
    /// at the edge of vision take a few seconds of standing in view; wise watchers are quicker;
    /// big targets (SizeScale) are easier to see; a sneaking target (stealthFactor 0..0.9, see
    /// <see cref="StealthFactor"/>) climbs proportionally slower — an unhidden target is simply
    /// fully visible.
    /// </summary>
    public static float SightGainPerTick(
        float distance, float visionRange, int watcherWisdom, float targetSizeScale,
        float stealthFactor, int ticksPerSecond)
    {
        float closeness = Math.Clamp(distance / MathF.Max(0.01f, visionRange), 0f, 1f);
        float distanceFactor = 1.0f + (0.25f - 1.0f) * closeness;      // 1.0 close → 0.25 at the edge
        float perception = 0.8f + watcherWisdom * 0.02f;
        float sizeFactor = Math.Clamp(targetSizeScale, 0.6f, 1.6f);
        return SightGainPerSecond * distanceFactor * (1f - stealthFactor) * perception * sizeFactor
               / Math.Max(1, ticksPerSecond);
    }
}

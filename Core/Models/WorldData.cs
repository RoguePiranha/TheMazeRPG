using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// A world's frozen identity, written once at creation to Saves/Worlds/{WorldId}/world.json and
/// never rewritten (owner ruling 2026-08-05). Characters live inside a world; the world outlives
/// every one of them.
///
/// The generated region — tiles, rooms, POI features, NPC roster — is PR 6's payload and lands as
/// additional fields here. It belongs in the *world* save rather than a per-town file: the starting
/// region is one region of a world, and world-level facts (the day high-water mark, fallen heroes,
/// dead NPCs) need a home that a town-scoped file couldn't provide.
/// </summary>
public class WorldData
{
    public int Version { get; set; } = 1;

    /// <summary>Identifies this world's folder on disk (Saves/Worlds/{WorldId}/).</summary>
    public string WorldId { get; set; } = "";

    /// <summary>Player-supplied display name for the worlds picker.</summary>
    public string Name { get; set; } = "";

    public WorldGenOptions Options { get; set; } = new();

    /// <summary>The seed generation actually used. Identical to Options.Seed unless validation
    /// forced a retry (note 08: failed grammar asserts regenerate with seed+1, bounded) — recording
    /// it means a world can always be reproduced exactly, not just approximately.</summary>
    public int EffectiveSeed { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Listing info for one world — what the worlds picker shows without deserializing the full world
/// (which, once PR 6 lands, carries an entire region's tile grid). Same role SaveSummary plays for
/// character slots.
/// </summary>
public class WorldSummary
{
    public string WorldId { get; set; } = "";
    public string Name { get; set; } = "";
    public WorldSize Size { get; set; } = WorldSize.Medium;
    public Hostility Hostility { get; set; } = Hostility.Normal;
    public int Seed { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Furthest day any character in this world has reached — the world's own elapsed time
    /// for display purposes. Each character still keeps their own clock (see WorldDelta).</summary>
    public int Day { get; set; } = 1;

    public int LivingCharacters { get; set; }
    public int FallenCharacters { get; set; }

    public string DescriptionLine => $"{Size} · {Hostility} · Day {Day} · seed {Seed}";

    public string PopulationLine => FallenCharacters > 0
        ? $"{LivingCharacters} living, {FallenCharacters} fallen"
        : $"{LivingCharacters} living";
}

using System.Text.Json.Serialization;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Scales the generated region, not the number of settlements (owner ruling 2026-08-05) — multiple
/// towns need travel/multi-map infrastructure, which is v2. Drives region dimensions, population,
/// and forest depth via the profile table in Data/Config/worldgen.json.
/// </summary>
public enum WorldSize
{
    Small,
    Medium,
    Large
}

/// <summary>
/// A multiplier profile applied over tunables that already exist, rather than a separate difficulty
/// system — see WorldProfile. Names are the owner's (2026-08-05).
/// </summary>
public enum Hostility
{
    Peaceful,
    Normal,
    Hostile
}

/// <summary>
/// Where a newly created character comes into existence. Dungeon is the canonical opening: the
/// origin story (Implementation Plan §0a) is a newly-sentient being who comes into existence inside
/// the dungeon, which is what justifies the blank-slate start. Town is the alternative for players
/// who'd rather begin as a resident. Resolved to a position by feature lookup, never coordinates,
/// so the generated region (PR 6) can add anchors as data.
/// </summary>
public enum StartLocation
{
    Dungeon,
    Town
}

/// <summary>
/// The player's choices at world creation, serialized verbatim into world.json and never changed
/// afterward — they're the recipe the world was generated from (owner ruling 2026-08-05: worlds are
/// generated at creation time, then frozen).
///
/// Deliberately absent: resource richness. Abundance is predetermined by the region's environment
/// (mountain band → iron-rich mine, forest depth → wood), authored in the region template rather
/// than dialed by the player. Future knobs (magic prevalence, faction density, day length) are new
/// fields plus profile rows — no surgery.
/// </summary>
public class WorldGenOptions
{
    /// <summary>Shown and enterable in the creation UI, so worlds are shareable by seed.</summary>
    public int Seed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorldSize Size { get; set; } = WorldSize.Medium;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Hostility Hostility { get; set; } = Hostility.Normal;

    public WorldGenOptions Clone() => new()
    {
        Seed = Seed,
        Size = Size,
        Hostility = Hostility
    };
}

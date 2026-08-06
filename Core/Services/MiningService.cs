using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>What breaking one tile of rock gave you.</summary>
public sealed class MiningYield
{
    public Dictionary<string, int> Materials { get; } = new();
    public bool StruckGem { get; set; }

    public void Add(string materialId, int amount)
    {
        if (amount <= 0) return;
        Materials[materialId] = Materials.GetValueOrDefault(materialId) + amount;
    }

    public bool IsEmpty => Materials.Count == 0;
}

/// <summary>
/// The rules for digging rock out of the world (owner ruling 2026-08-06: mining is destroying
/// stone for ore and gems, not standing at a wall pressing a button).
///
/// Anything stone can be broken — mountainside, cave rock, town walls, the walls of the dungeon
/// itself — with two exceptions carved out as <see cref="TileType.Bedrock"/>: the map's rim, and
/// the dungeon's threshold. The dungeon does not occupy physical space inside the mountain; it is a
/// separate dimensional space reached through that threshold, so no amount of digging into the
/// mountainside can ever break into it.
/// </summary>
public static class MiningService
{
    /// <summary>Base work, in ticks, to break one tile of each kind. Masonry is slower than natural
    /// rock: it was cut and fitted to stay put.</summary>
    private const int StoneTicks = 90;
    private const int VeinTicks = 120;
    private const int MasonryTicks = 200;

    /// <summary>Chance a plain stone tile happens to hold a little ore anyway.</summary>
    private const double StrayOreChance = 0.08;

    /// <summary>Chance an ore vein also gives up a gem. Rare, and the reason to work a seam out
    /// rather than take the first tile and leave.</summary>
    private const double VeinGemChance = 0.06;

    public static bool CanMine(TileType tile) =>
        tile is TileType.Stone or TileType.OreVein or TileType.Wall;

    /// <summary>Can this specific cell be mined? Bedrock never can, and neither can anything that
    /// isn't rock or masonry in the first place.</summary>
    public static bool CanMine(Maze maze, int x, int y) =>
        maze.InBounds(x, y) && CanMine(maze.Tiles[x, y]);

    /// <summary>
    /// How long breaking this tile takes. Strength does the work and the mining skill supplies the
    /// technique, so both shorten it; nothing makes it instant.
    /// </summary>
    public static int WorkTicks(TileType tile, float effectiveStrength, int miningSkillLevel)
    {
        int baseTicks = tile switch
        {
            TileType.OreVein => VeinTicks,
            TileType.Wall => MasonryTicks,
            _ => StoneTicks
        };

        float strengthFactor = 1f / (1f + effectiveStrength * 0.05f);
        float skillFactor = 1f / (1f + miningSkillLevel * 0.08f);
        return Math.Max(12, (int)MathF.Round(baseTicks * strengthFactor * skillFactor));
    }

    /// <summary>
    /// What the broken tile leaves behind. Plain rock is mostly rubble; a vein pays in ore and
    /// occasionally a gem. Masonry yields cut stone and never ore — someone already took the ore
    /// out of it before it was a wall.
    /// </summary>
    public static MiningYield Roll(TileType tile, Random random, int depthFromSurface)
    {
        var yield = new MiningYield();

        switch (tile)
        {
            case TileType.OreVein:
            {
                // Deeper seams are richer, which is what makes pushing the tunnel on worthwhile.
                int ore = 2 + random.Next(2) + Math.Min(3, depthFromSurface / 25);
                yield.Add("iron-ore", ore);
                yield.Add("stone", 1);
                if (random.NextDouble() < VeinGemChance + Math.Min(0.06, depthFromSurface / 1000.0))
                {
                    yield.Add("rough-gem", 1);
                    yield.StruckGem = true;
                }
                break;
            }

            case TileType.Wall:
                yield.Add("stone", 1 + random.Next(2));
                break;

            default:
                yield.Add("stone", 1 + random.Next(2));
                if (random.NextDouble() < StrayOreChance)
                    yield.Add("iron-ore", 1);
                break;
        }

        return yield;
    }

    /// <summary>
    /// How loud breaking this is, in tiles. Mining is not a quiet activity anywhere, but hammering
    /// on the dungeon's own walls carries — see GameState's dungeon reaction.
    /// </summary>
    public static float NoiseRadius(TileType tile, bool inDungeon)
    {
        float radius = tile == TileType.Wall ? 14f : 10f;
        return inDungeon ? radius * 1.8f : radius;
    }
}

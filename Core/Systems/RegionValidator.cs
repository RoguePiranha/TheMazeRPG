using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// The grammar every generated region must satisfy (Starting Region.md). Validation is load-bearing
/// here in a way it isn't for hand-authored content: a player's world is generated once and frozen,
/// with no opportunity to fix it afterwards, so a region that breaks the spec must be rejected and
/// regenerated rather than shipped.
/// </summary>
public static class RegionValidator
{
    public static IReadOnlyList<string> Validate(Maze maze)
    {
        var errors = new List<string>();
        var region = maze.Region;
        if (region == null)
        {
            errors.Add("Region layout metadata is missing");
            return errors;
        }

        // --- The points of interest the loop depends on ---
        var entrance = maze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.DungeonEntrance);
        var mine = maze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.MineEntrance);
        var smithy = maze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.Smithy);
        var stalls = maze.Features.Where(f => f.Type == MazeFeatureType.Stall).ToList();

        if (entrance == null) errors.Add("No dungeon entrance");
        if (mine == null) errors.Add("No mine entrance");
        if (smithy == null) errors.Add("No smithy");
        if (stalls.Count < 1) errors.Add("No market stall");
        if (errors.Count > 0) return errors;

        // --- Everything the player must reach has to be reachable from the dungeon mouth ---
        // This is the single most important check: the whole town loop (arrive → mine → craft →
        // sell → dive again) is worthless if any leg of it is walled off.
        var arrival = FindWalkableNear(maze, entrance!.X, entrance.Y);
        if (arrival == null)
        {
            errors.Add("Dungeon entrance has no walkable tile beside it");
            return errors;
        }

        var reachable = maze.BfsDistancesFrom(arrival.Value.x, arrival.Value.y);
        foreach (var (label, feature) in new (string, MazeFeature?)[]
                 {
                     ("mine", mine), ("smithy", smithy), ("stall", stalls[0])
                 })
        {
            if (feature == null) continue;
            if (!IsReachable(maze, reachable, feature.X, feature.Y))
                errors.Add($"The {label} is not reachable from the dungeon entrance");
        }

        // --- The walls actually enclose the town, with gates as the way through ---
        if (region.Gates.Count < 2)
            errors.Add($"Town has {region.Gates.Count} gate(s), expected at least 2");
        foreach (var gate in region.Gates)
        {
            if (!maze.IsWalkable(gate.X, gate.Y))
                errors.Add($"Gate at ({gate.X},{gate.Y}) is not walkable");
            if (!IsReachable(maze, reachable, gate.X, gate.Y))
                errors.Add($"Gate at ({gate.X},{gate.Y}) is not reachable from the dungeon entrance");
        }

        // --- The mine is outside the walls (Starting Region.md) ---
        if (mine != null && region.InsideWalls(mine.X, mine.Y))
            errors.Add("The mine is inside the town walls");

        // --- Water: intake strictly upstream of the outfall, so the town doesn't drink its sewage ---
        bool intakeUpstream = region.RiverFlowsPositive
            ? IntakeCoordinate(region) < OutfallCoordinate(region)
            : IntakeCoordinate(region) > OutfallCoordinate(region);
        if (!intakeUpstream)
            errors.Add("Water intake is not upstream of the sewage outfall");

        // --- Buildings: real interiors, reachable doors ---
        if (region.Buildings.Count < 6)
            errors.Add($"Only {region.Buildings.Count} buildings were placed");
        if (region.Buildings.Count(b => b.HasTrapdoor) != 1)
            errors.Add("Exactly one home must hide the trapdoor");

        foreach (var building in region.Buildings)
        {
            if (!maze.InBounds(building.DoorX, building.DoorY))
            {
                errors.Add($"Building {building.Id} has an out-of-bounds door");
                continue;
            }
            if (!maze.IsTraversable(building.DoorX, building.DoorY))
                errors.Add($"Building {building.Id}'s door is not traversable");
            else if (!IsReachable(maze, reachable, building.DoorX, building.DoorY))
                errors.Add($"Building {building.Id}'s door is not reachable from the dungeon entrance");

            // A building has to be a place, not a shape: its interior must be standable floor and
            // actually walk-in-able from the street through its own door.
            if (!maze.IsWalkable(building.InteriorX, building.InteriorY))
                errors.Add($"Building {building.Id} has no walkable interior");
            else if (!reachable.ContainsKey((building.InteriorX, building.InteriorY)))
                errors.Add($"Building {building.Id}'s interior cannot be entered");
        }

        // --- The map's rim stays solid ---
        for (int x = 0; x < maze.Width; x++)
        {
            if (maze.IsWalkable(x, 0) || maze.IsWalkable(x, maze.Height - 1))
            {
                errors.Add("The region boundary is open on the north or south edge");
                break;
            }
        }
        for (int y = 0; y < maze.Height; y++)
        {
            if (maze.IsWalkable(0, y) || maze.IsWalkable(maze.Width - 1, y))
            {
                errors.Add("The region boundary is open on the east or west edge");
                break;
            }
        }

        return errors;
    }

    private static int IntakeCoordinate(RegionLayout region) =>
        region.MountainEdge is RegionEdge.North or RegionEdge.South ? region.IntakeX : region.IntakeY;

    private static int OutfallCoordinate(RegionLayout region) =>
        region.MountainEdge is RegionEdge.North or RegionEdge.South ? region.OutfallX : region.OutfallY;

    /// <summary>Reachable if the cell itself is on the walked graph, or something beside it is —
    /// a feature may sit on a tile you stand next to rather than on.</summary>
    private static bool IsReachable(Maze maze, Dictionary<(int x, int y), int> reachable, int x, int y)
    {
        if (reachable.ContainsKey((x, y))) return true;
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            if (reachable.ContainsKey((x + dx, y + dy))) return true;
        _ = maze;
        return false;
    }

    private static (int x, int y)? FindWalkableNear(Maze maze, int x, int y)
    {
        if (maze.IsWalkable(x, y)) return (x, y);
        for (int radius = 1; radius <= 4; radius++)
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (maze.IsWalkable(x + dx, y + dy))
                        return (x + dx, y + dy);
        return null;
    }
}

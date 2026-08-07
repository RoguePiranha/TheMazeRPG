using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// The town's daily life, v1 (Planning note 09, PR 8 — deliberately schedules-first, per Starting
/// Region.md's own recommendation). A deterministic roster of townsfolk is derived from the world
/// seed — same world, same people, forever (note 08 stage 9) — and each of them walks a
/// phase-of-day schedule: home, work, the square at midday, social hours in the evening, bed at
/// night, with guards splitting day and night patrol shifts.
///
/// NPCs are scenery with intent, not actors in the combat sense: they never block movement, carry
/// no HP, and use no shared RNG (the roster is built from its own seeded stream; the per-tick
/// update is RNG-free), so headless demos and gameplay determinism are untouched. Trade, barks,
/// crime, and the combat shadow all land later on this same Goal seam (note 09 PR 9).
/// </summary>
public static class NpcService
{
    private const float WalkPerUpdate = 0.1f;   // every-other-tick updates → ~1.5 tiles/s at 30 tps
    private const int RepathsPerTick = 6;       // ration A* so a phase change ripples, not spikes
    private const int PathNodeBudget = 60000;   // A* cap; regions run to ~215k cells

    /// <summary>
    /// How far (Chebyshev tiles) someone will walk for the midday meal at the square. Tiles are
    /// metres and the midday band is one game hour, so the square draws whoever can make the
    /// round trip inside it — everyone else eats where they stand. This is scale honesty, not a
    /// shortcut: a smith whose forge sits by the far wall genuinely can't lunch across town.
    /// </summary>
    private const int MiddayCatchment = 45;

    /// <summary>Occupations household extras can hold — the trades are assigned one-per-premise.</summary>
    private static readonly string[] GivenNameStarts =
        { "Al", "Bran", "Cor", "Dain", "Ed", "Fen", "Gar", "Hild", "Ilsa", "Jor", "Kel", "Lena", "Mar", "Nils", "Ottil", "Per", "Runa", "Sten", "Tilda", "Ulf", "Vera", "Wil", "Ysol", "Zeb" };
    private static readonly string[] GivenNameEnds =
        { "a", "an", "bert", "da", "dric", "e", "fried", "ga", "hard", "i", "ke", "la", "mund", "na", "o", "rik", "sa", "ta", "us", "win" };
    private static readonly string[] Surnames =
        { "Ashdown", "Briarwood", "Coppersmith", "Dunmore", "Eastfield", "Fernsby", "Greywater", "Hollowell", "Ironridge", "Kettleworth", "Longbarrow", "Millbrook", "Northgate", "Oakhurst", "Pennyworth", "Quarry", "Riverstead", "Stonebridge", "Thistlewood", "Underhill" };

    /// <summary>
    /// Build (or rebuild) the town's roster on the current overworld map. Deterministic in the
    /// world's effective seed; a plain field (no region — headless fallbacks, towngen overrides)
    /// gets no population.
    /// </summary>
    public static void PopulateTown(GameState game)
    {
        game.Npcs.Clear();
        var maze = game.CurrentMaze;
        var region = maze.Region;
        if (region == null || region.Buildings.Count == 0) return;

        int seed;
        try
        {
            seed = WorldService.Load(game.WorldId)?.EffectiveSeed ?? region.Seed;
        }
        catch
        {
            seed = region.Seed;
        }

        var random = new Random(seed ^ 0x7A11);
        var homes = region.Buildings.Where(b => b.Kind == BuildingKind.Home).ToList();
        if (homes.Count == 0) return;

        // The trades, one per premise that actually got built. Guards exist regardless of
        // whether their station found a lot — the patrol is the job, the building is a perk.
        var jobs = new List<(NpcOccupation occupation, Building? workplace)>();
        foreach (var building in region.Buildings)
        {
            NpcOccupation? occupation = building.Kind switch
            {
                BuildingKind.Smithy => NpcOccupation.Smith,
                BuildingKind.Alchemist => NpcOccupation.Alchemist,
                BuildingKind.Carpenter => NpcOccupation.Carpenter,
                BuildingKind.Temple => NpcOccupation.Priest,
                BuildingKind.TrainingSchool => NpcOccupation.Trainer,
                BuildingKind.Tavern => NpcOccupation.Tavernkeep,
                _ => null
            };
            if (occupation != null) jobs.Add((occupation.Value, building));
        }
        var guardStation = region.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.GuardStation);
        for (int i = 0; i < 4; i++) jobs.Add((NpcOccupation.Guard, guardStation));
        foreach (var stall in maze.Features.Where(f => f.Type == MazeFeatureType.Stall))
            jobs.Add((NpcOccupation.Merchant, null));

        // Households (note 08: {1:20%, 2:40%, 3:25%, 4:15%}), assigned around the job list so
        // every trade lives somewhere and the rest of the town fills in as laborers, elders and
        // children.
        int jobIndex = 0;
        int npcIndex = 0;
        var school = region.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.TrainingSchool);
        var waterTower = region.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.WaterTower);
        int guardsMade = 0;

        foreach (var home in homes)
        {
            int roll = random.Next(100);
            int householdSize = roll < 20 ? 1 : roll < 60 ? 2 : roll < 85 ? 3 : 4;

            for (int member = 0; member < householdSize; member++)
            {
                var npc = new Npc
                {
                    Id = $"npc-{seed}-{npcIndex}",
                    Name = $"{GivenNameStarts[random.Next(GivenNameStarts.Length)]}{GivenNameEnds[random.Next(GivenNameEnds.Length)]} {Surnames[random.Next(Surnames.Length)]}",
                    Race = random.Next(100) < 75 ? "Human" : random.Next(2) == 0 ? "Elf" : "Orc",
                    HomeBuildingId = home.Id,
                    JitterMinutes = random.Next(-20, 21),
                    Salt = npcIndex,
                    WalkSpeedScale = 0.85f + npcIndex % 8 * 0.04f,
                    NoiseState = (uint)(npcIndex * 2654435761 + seed) | 1
                };

                // The first two members of a household are adults; extras skew young, some old.
                bool adult = member < 2;
                if (adult && jobIndex < jobs.Count)
                {
                    var (occupation, workplace) = jobs[jobIndex++];
                    npc.Occupation = occupation;
                    npc.WorkBuildingId = workplace?.Id ?? -1;
                    if (occupation == NpcOccupation.Guard)
                    {
                        npc.NightShift = guardsMade++ % 2 == 1;
                        // Staggered legs: two guards on the same shift walk different stretches
                        // of the ring, instead of stacking on one tile and marching in lockstep.
                        npc.PatrolLeg = npcIndex;
                    }
                }
                else if (adult)
                {
                    npc.Occupation = NpcOccupation.Laborer;
                    npc.WorkBuildingId = waterTower?.Id ?? -1;
                }
                else
                {
                    npc.Occupation = random.Next(100) < 60 ? NpcOccupation.Child : NpcOccupation.Elder;
                    if (npc.Occupation == NpcOccupation.Child)
                        npc.WorkBuildingId = school?.Id ?? -1;
                }

                ResolveWorkAnchor(npc, npcIndex, maze, region);
                game.Npcs.Add(npc);
                npcIndex++;
            }
        }

        // Everyone starts wherever their day currently has them, so loading a save at 14:00
        // shows a working town rather than sixty people marching out of their front doors.
        foreach (var npc in game.Npcs)
        {
            var phase = game.Clock.PhaseAt(npc.JitterMinutes);
            npc.LastPhase = (int)phase;
            ResolveGoal(npc, phase, maze, region);
            npc.X = npc.TargetX;
            npc.Y = npc.TargetY;
            npc.Path.Clear();
            npc.NeedsPath = false;
        }
    }

    /// <summary>Where this NPC's working hours are spent (fixed at roster time).</summary>
    private static void ResolveWorkAnchor(Npc npc, int index, Maze maze, RegionLayout region)
    {
        var workplace = npc.WorkBuildingId >= 0
            ? region.Buildings.FirstOrDefault(b => b.Id == npc.WorkBuildingId)
            : null;
        if (workplace != null)
        {
            (npc.WorkX, npc.WorkY) = InteriorSpot(workplace, index);
            return;
        }

        if (npc.Occupation == NpcOccupation.Merchant)
        {
            // One merchant per stall, in ring order around the square.
            var stalls = maze.Features.Where(f => f.Type == MazeFeatureType.Stall).ToList();
            if (stalls.Count > 0)
            {
                var stall = stalls[index % stalls.Count];
                // Stand beside the stall, on the square side of it.
                npc.WorkX = stall.X + Math.Sign(region.SquareX - stall.X);
                npc.WorkY = stall.Y + Math.Sign(region.SquareY - stall.Y);
                if (!maze.IsWalkable(npc.WorkX, npc.WorkY))
                    (npc.WorkX, npc.WorkY) = (region.SquareX, region.SquareY);
                return;
            }
        }

        // No workplace of their own: the square is where the day happens.
        (npc.WorkX, npc.WorkY) = SquareSpot(index, maze, region);
    }

    /// <summary>A standable interior cell, spread deterministically so a household or workshop
    /// doesn't stack everyone on the same tile.</summary>
    private static (int x, int y) InteriorSpot(Building building, int salt)
    {
        var cells = new List<(int x, int y)>();
        for (int x = building.X + 1; x < building.Right; x++)
            for (int y = building.Y + 1; y < building.Bottom; y++)
                cells.Add((x, y));
        return cells.Count == 0
            ? (building.InteriorX, building.InteriorY)
            : cells[salt % cells.Count];
    }

    /// <summary>A spot on the plaza, spread around its centre.</summary>
    private static (int x, int y) SquareSpot(int salt, Maze maze, RegionLayout region)
    {
        int half = Math.Max(1, region.SquareSize / 2 - 1);
        var offsets = new (int x, int y)[]
        {
            (0, 0), (2, 1), (-2, -1), (1, -2), (-1, 2), (3, 0), (-3, 1), (0, 3),
            (0, -3), (2, -2), (-2, 2), (3, 3), (-3, -3), (1, 1), (-1, -1), (2, 3)
        };
        var pick = offsets[salt % offsets.Length];
        int x = region.SquareX + Math.Clamp(pick.x, -half, half);
        int y = region.SquareY + Math.Clamp(pick.y, -half, half);
        return maze.IsWalkable(x, y) ? (x, y) : (region.SquareX, region.SquareY);
    }

    /// <summary>
    /// The occupation × phase schedule table (note 09 §3), resolved to a goal and a destination.
    /// </summary>
    private static void ResolveGoal(Npc npc, DayPhase phase, Maze maze, RegionLayout region)
    {
        var home = region.Buildings.FirstOrDefault(b => b.Id == npc.HomeBuildingId);
        (int x, int y) homeSpot = home != null
            ? InteriorSpot(home, npc.Salt)
            : (region.SquareX, region.SquareY);
        int salt = npc.Salt;

        (NpcGoalType goal, (int x, int y) spot) plan;
        if (npc.Occupation == NpcOccupation.Guard)
        {
            // Shifts: the day shift patrols through the working phases, the night shift through
            // evening and night; both bracket their patrol with meals and sleep at home.
            bool onShift = npc.NightShift
                ? phase is DayPhase.Evening or DayPhase.Night
                : phase is DayPhase.WorkAM or DayPhase.Midday or DayPhase.WorkPM;
            plan = onShift
                ? (NpcGoalType.Patrol, PatrolWaypoint(npc, maze, region))
                : phase is DayPhase.Morning or DayPhase.Midday or DayPhase.Evening
                    ? (NpcGoalType.Meal, homeSpot)
                    : (NpcGoalType.Sleep, homeSpot);
        }
        else
        {
            (NpcGoalType goal, (int x, int y) spot) daytime = npc.Occupation switch
            {
                NpcOccupation.Elder => (NpcGoalType.Meal, homeSpot),
                NpcOccupation.Child => npc.WorkBuildingId >= 0
                    ? (NpcGoalType.School, (npc.WorkX, npc.WorkY))
                    : (NpcGoalType.Socialize, SquareSpot(salt, maze, region)),
                _ => (NpcGoalType.Work, (npc.WorkX, npc.WorkY))
            };

            plan = phase switch
            {
                DayPhase.Morning => (NpcGoalType.Meal, homeSpot),
                // Midday: the square for those in its catchment; everyone else eats in place.
                // Children stay at school and elders at home either way.
                DayPhase.Midday when npc.Occupation is not NpcOccupation.Child and not NpcOccupation.Elder &&
                                     Math.Max(Math.Abs(daytime.spot.x - region.SquareX),
                                              Math.Abs(daytime.spot.y - region.SquareY)) <= MiddayCatchment
                    => (NpcGoalType.Meal, SquareSpot(salt, maze, region)),
                DayPhase.Midday => (NpcGoalType.Meal, daytime.spot),
                DayPhase.Evening => (NpcGoalType.Socialize, EveningSpot(npc, salt, maze, region)),
                DayPhase.Night => (NpcGoalType.Sleep, homeSpot),
                _ => daytime // WorkAM / WorkPM
            };
        }

        npc.Goal = plan.goal;
        npc.TargetX = plan.spot.x;
        npc.TargetY = plan.spot.y;
    }

    /// <summary>Evenings split between the square and the tavern (when the town has one), so both
    /// fill up rather than one venue swallowing the whole population.</summary>
    private static (int x, int y) EveningSpot(Npc npc, int salt, Maze maze, RegionLayout region)
    {
        var tavern = region.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Tavern);
        if (tavern != null && salt % 2 == 1 && npc.Occupation != NpcOccupation.Child)
            return InteriorSpot(tavern, salt);
        return SquareSpot(salt, maze, region);
    }

    /// <summary>The guard round: dungeon mouth → square → each gate and back (note 09 §4.5's ring,
    /// minus the crime response that arrives with PR 9).</summary>
    private static (int x, int y) PatrolWaypoint(Npc npc, Maze maze, RegionLayout region)
    {
        var waypoints = new List<(int x, int y)>();
        var entrance = maze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.DungeonEntrance);
        if (entrance != null) waypoints.Add((entrance.X, entrance.Y));
        waypoints.Add((region.SquareX, region.SquareY));
        foreach (var gate in region.Gates)
        {
            var (dx, dy) = gate.Side switch
            {
                RegionEdge.North => (0, -1),
                RegionEdge.South => (0, 1),
                RegionEdge.East => (1, 0),
                _ => (-1, 0)
            };
            waypoints.Add((gate.X - dx * 2, gate.Y - dy * 2)); // just inside the gate
            waypoints.Add((region.SquareX, region.SquareY));   // back through the middle
        }
        if (waypoints.Count == 0) return (region.SquareX, region.SquareY);
        return waypoints[npc.PatrolLeg % waypoints.Count];
    }

    /// <summary>
    /// Per-tick driver, called from GameState.Tick while the hero is in the overworld. Each NPC
    /// updates every other tick; repaths are rationed so a phase change ripples across the town
    /// over a few ticks instead of stalling one.
    /// </summary>
    public static void Update(GameState game)
    {
        var maze = game.CurrentMaze;
        var region = maze.Region;
        if (region == null || game.Npcs.Count == 0) return;

        int repathBudget = RepathsPerTick;

        for (int i = 0; i < game.Npcs.Count; i++)
        {
            var npc = game.Npcs[i];
            if ((i & 1) != (game.TickCount & 1)) continue;

            // Phase (with personal jitter) decides the goal, consulted only on phase boundaries.
            var phase = game.Clock.PhaseAt(npc.JitterMinutes);
            if ((int)phase != npc.LastPhase)
            {
                npc.LastPhase = (int)phase;
                ResolveGoal(npc, phase, maze, region);
                npc.Path.Clear();
                npc.NeedsPath = true;
            }

            if (npc.NeedsPath && repathBudget > 0)
            {
                repathBudget--;
                npc.NeedsPath = false;
                var path = FindPath(maze, ((int)MathF.Round(npc.X), (int)MathF.Round(npc.Y)),
                    (npc.TargetX, npc.TargetY));
                npc.Path = path ?? new Queue<(int x, int y)>();
                // No route (should not happen on a validated region): stand where they are;
                // the next goal change tries again.
            }

            WalkAlongPath(npc, maze);

            // A guard reaching their waypoint lingers — a look around the post — then walks the
            // next leg of the round. The linger varies per person, so two guards sharing a shift
            // drift apart instead of shadowing each other.
            if (npc.Goal == NpcGoalType.Patrol && npc.Path.Count == 0 && !npc.NeedsPath &&
                MathF.Abs(npc.X - npc.TargetX) < 0.2f && MathF.Abs(npc.Y - npc.TargetY) < 0.2f)
            {
                if (npc.IdleTicks == 0)
                {
                    npc.IdleTicks = 40 + (int)(NextNoise(npc) % 80);
                }
                else if (--npc.IdleTicks == 0)
                {
                    npc.PatrolLeg++;
                    npc.NeedsPath = true;
                    var next = PatrolWaypoint(npc, maze, region);
                    npc.TargetX = next.x;
                    npc.TargetY = next.y;
                }
            }
            // Everyone else at their destination putters: a small wander near the anchor every
            // so often, so a workshop, a square, or a tavern is a place people shift around in
            // rather than a museum of statues (note 09's "stationary loop", finally embodied).
            // Sleepers lie still.
            else if (npc.Path.Count == 0 && !npc.NeedsPath &&
                     npc.Goal is not NpcGoalType.Sleep and not NpcGoalType.Patrol)
            {
                if (npc.IdleTicks > 0)
                {
                    npc.IdleTicks--;
                }
                else
                {
                    uint roll = NextNoise(npc);
                    int wanderX = npc.TargetX + (int)(roll % 5) - 2;
                    int wanderY = npc.TargetY + (int)((roll >> 8) % 5) - 2;
                    bool hopped = false;
                    if (maze.IsWalkable(wanderX, wanderY))
                    {
                        var hop = FindPath(maze, ((int)MathF.Round(npc.X), (int)MathF.Round(npc.Y)),
                            (wanderX, wanderY));
                        if (hop != null && hop.Count > 0 && hop.Count <= 6)
                        {
                            npc.Path = hop;
                            hopped = true;
                        }
                    }
                    // A dud roll (own tile, a wall, out of reach) retries shortly; an actual
                    // stroll earns a proper pause before the next one.
                    npc.IdleTicks = hopped
                        ? 50 + (int)(NextNoise(npc) % 140)
                        : 8 + (int)(NextNoise(npc) % 16);
                }
            }
        }
    }

    /// <summary>Per-person xorshift: personal randomness with no shared RNG stream touched.</summary>
    private static uint NextNoise(Npc npc)
    {
        uint x = npc.NoiseState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        npc.NoiseState = x;
        return x;
    }

    private static void WalkAlongPath(Npc npc, Maze maze)
    {
        if (npc.Path.Count == 0) return;

        var (nx, ny) = npc.Path.Peek();

        // A shut door on the way is opened by walking into it — the same bump-to-open rule as
        // every other mover (owner ruling 2026-08-05). Anything else impassable means the world
        // changed under the path (a mined tile, a new obstacle): recompute next update.
        if (!maze.IsWalkable(nx, ny))
        {
            if (maze.IsDoor(nx, ny) && maze.TryOpenDoor(nx, ny))
            {
                npc.StuckTicks = 0; // opening the door was this update's progress
                return;
            }
            npc.StuckTicks++;
            if (npc.StuckTicks > 8)
            {
                npc.StuckTicks = 0;
                npc.Path.Clear();
                npc.NeedsPath = true;
            }
            return;
        }

        float dx = nx - npc.X;
        float dy = ny - npc.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float step = WalkPerUpdate * npc.WalkSpeedScale;
        if (distance <= step)
        {
            npc.X = nx;
            npc.Y = ny;
            npc.Path.Dequeue();
        }
        else
        {
            npc.X += dx / distance * step;
            npc.Y += dy / distance * step;
        }
        npc.StuckTicks = 0;
    }

    /// <summary>
    /// A* over traversable ground (doors count — they open on a bump). Roads are cheaper than
    /// grass, so townsfolk visibly keep to the streets; doors cost a shade more than floor so a
    /// path indoors is taken for a reason, not as a shortcut through someone's parlour.
    /// </summary>
    private static Queue<(int x, int y)>? FindPath(Maze maze, (int x, int y) start, (int x, int y) goal)
    {
        if (start == goal) return new Queue<(int x, int y)>();
        if (!maze.IsTraversable(goal.x, goal.y)) return null;

        var open = new PriorityQueue<(int x, int y), int>();
        var gScore = new Dictionary<(int x, int y), int> { [start] = 0 };
        var parent = new Dictionary<(int x, int y), (int x, int y)>();
        open.Enqueue(start, 0);
        int expanded = 0;

        while (open.TryDequeue(out var cell, out _))
        {
            if (cell == goal)
            {
                var steps = new List<(int x, int y)>();
                var cursor = goal;
                while (cursor != start)
                {
                    steps.Add(cursor);
                    cursor = parent[cursor];
                }
                steps.Reverse();
                return new Queue<(int x, int y)>(steps);
            }

            if (++expanded > PathNodeBudget) return null;
            int g = gScore[cell];

            foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var next = (x: cell.x + dx, y: cell.y + dy);
                if (!maze.IsTraversable(next.x, next.y)) continue;

                var tile = maze.Tiles[next.x, next.y];
                int stepCost = tile == TileType.Road ? 10 : tile == TileType.Floor ? 12 : 14;
                int tentative = g + stepCost;
                if (tentative >= gScore.GetValueOrDefault(next, int.MaxValue)) continue;

                gScore[next] = tentative;
                parent[next] = cell;
                int heuristic = 10 * (Math.Abs(goal.x - next.x) + Math.Abs(goal.y - next.y));
                open.Enqueue(next, tentative + heuristic);
            }
        }

        return null;
    }
}

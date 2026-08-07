using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>What a townsperson does for a living. Drives their daily schedule, their work anchor,
/// and (later, note 09 PR 9) their trade flows and combat shadow class.</summary>
public enum NpcOccupation
{
    Smith,
    Alchemist,
    Carpenter,
    Priest,
    Trainer,
    Guard,
    Tavernkeep,
    Merchant,
    Laborer,
    Child,
    Elder
}

/// <summary>What an NPC is currently trying to do with their day (Planning note 09 §3).</summary>
public enum NpcGoalType
{
    Sleep,
    Work,
    Meal,
    Socialize,
    School,
    Patrol
}

/// <summary>
/// A peaceful townsperson — parallel to <see cref="Enemy"/>, deliberately not a subclass of it
/// (note 09): an NPC's day is schedules and errands, not aggro and cooldowns. v1 is
/// schedules-first — no needs simulation, no combat, no trade; those plug into the same Goal seam
/// later. NPCs never block movement and nothing blocks them; they are the town visibly living.
///
/// The roster is derived deterministically from the world seed (same world, same people, forever —
/// note 08 stage 9), so nothing here needs persisting: a stable <see cref="Id"/> is enough for the
/// world delta to remember the dead when that lands.
/// </summary>
public class Npc
{
    public string Id = "";
    public string Name = "";
    public string Race = "Human";
    public NpcOccupation Occupation;

    /// <summary>Building ids on the region layout; -1 when the anchor isn't a building (a
    /// merchant's stall, a guard's patrol).</summary>
    public int HomeBuildingId = -1;
    public int WorkBuildingId = -1;

    /// <summary>Where this NPC's workday actually happens: a building interior, a stall-side
    /// tile, or (guards) the current patrol waypoint.</summary>
    public int WorkX;
    public int WorkY;

    public float X;
    public float Y;

    /// <summary>Guards split shifts: half the roster patrols by day, half by night (note 09).</summary>
    public bool NightShift;

    /// <summary>Personal schedule offset in game-minutes (±20), so the town staggers through
    /// phase changes instead of turning on its heel as one body.</summary>
    public int JitterMinutes;

    /// <summary>Stable per-person scatter key (the roster index). Used to spread standing spots —
    /// never string hashes, which .NET randomizes per process and would move everyone's favourite
    /// corner between runs.</summary>
    public int Salt;

    /// <summary>The last phase a goal was resolved for, so the schedule is consulted on phase
    /// boundaries rather than every update. -1 = never.</summary>
    public int LastPhase = -1;

    public NpcGoalType Goal = NpcGoalType.Sleep;

    /// <summary>Remaining steps of the current walk, cell centres in walk order.</summary>
    public Queue<(int x, int y)> Path = new();

    /// <summary>Where the current goal wants them standing.</summary>
    public int TargetX;
    public int TargetY;

    /// <summary>Guards: index of the patrol waypoint currently walked toward.</summary>
    public int PatrolLeg;

    /// <summary>Consecutive updates spent unable to advance along the path. Diagnostic — the
    /// TEST_TOWNLIFE stuck assertion reads this.</summary>
    public int StuckTicks;

    /// <summary>Set when the goal changed and a fresh path hasn't been computed yet. Repaths are
    /// rationed per tick, so this can stay pending for a few ticks after a phase change.</summary>
    public bool NeedsPath;
}

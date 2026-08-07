namespace TheMazeRPG.Core.Services;

/// <summary>The shape of a townsperson's day (Planning note 09). Schedules key off these bands
/// rather than raw hours so one table serves every occupation.</summary>
public enum DayPhase
{
    Morning,   // 6-8: wake, breakfast at home
    WorkAM,    // 8-12
    Midday,    // 12-13: meal, many at the square
    WorkPM,    // 13-18
    Evening,   // 18-22: social hours
    Night      // 22-6: asleep (guards on night shift excepted)
}

/// <summary>
/// World time-of-day. Owner ruling 2026-08-05: 15 real-minute days + 15 real-minute nights —
/// a 30-real-minute full cycle, so 1440 game minutes / 1800 real seconds = 0.8 game-minutes per
/// real second. Daylight runs 6:00–18:00. The clock ticks only while the sim runs (pausing the
/// game pauses time) and keeps running during dungeon dives — a dive costs a slice of the day.
/// </summary>
public class WorldClock
{
    public const float GameMinutesPerRealSecond = 0.8f;

    /// <summary>New heroes come into existence at 08:00 on Day 1.</summary>
    public double TotalGameMinutes { get; set; } = 8 * 60;

    public int Day => (int)(TotalGameMinutes / 1440) + 1;
    public float Hour => (float)(TotalGameMinutes % 1440) / 60f;
    public bool IsNight => Hour < 6f || Hour >= 18f;

    public string TimeDisplay
    {
        get
        {
            // Integer math on whole minutes — float division through Hour truncates 14:20 to 14:19.
            int totalMinutes = (int)TotalGameMinutes;
            int hh = totalMinutes % 1440 / 60;
            int mm = totalMinutes % 60;
            return $"Day {Day}, {hh:00}:{mm:00}";
        }
    }

    /// <summary>0 = full daylight, 1 = deep night; smooth ramps across dawn (5–7) and dusk (17–19).
    /// Drives the town's night tint (the dungeon has no sky).</summary>
    public float Darkness
    {
        get
        {
            float h = Hour;
            if (h >= 7f && h < 17f) return 0f;                     // day
            if (h >= 5f && h < 7f) return (7f - h) / 2f;           // dawn: fading out
            if (h >= 17f && h < 19f) return (h - 17f) / 2f;        // dusk: fading in
            return 1f;                                             // night
        }
    }

    public DayPhase Phase => PhaseAt(0);

    /// <summary>
    /// The phase as seen by someone whose personal day runs a little early or late. NPC schedules
    /// pass their per-person jitter here so the town wakes, eats, and sleeps in a stagger rather
    /// than moving in lockstep at each phase boundary.
    /// </summary>
    public DayPhase PhaseAt(float jitterMinutes)
    {
        double minutes = TotalGameMinutes + jitterMinutes;
        float h = (float)(((minutes % 1440) + 1440) % 1440) / 60f;
        return h switch
        {
            >= 6f and < 8f => DayPhase.Morning,
            >= 8f and < 12f => DayPhase.WorkAM,
            >= 12f and < 13f => DayPhase.Midday,
            >= 13f and < 18f => DayPhase.WorkPM,
            >= 18f and < 22f => DayPhase.Evening,
            _ => DayPhase.Night
        };
    }

    /// <summary>Advance by one simulation tick.</summary>
    public void AdvanceTick(int ticksPerSecond) =>
        TotalGameMinutes += GameMinutesPerRealSecond / System.Math.Max(1, ticksPerSecond);
}

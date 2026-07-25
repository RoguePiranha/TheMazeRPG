using System;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Lightweight listing info for one save slot — what the "Continue" saves picker shows without
/// needing to deserialize (and hand around) the full SaveData (loadout/inventory/etc).
/// </summary>
public class SaveSummary
{
    public string SaveId { get; set; } = "";
    public string HeroName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string RaceName { get; set; } = "";
    public int Level { get; set; }
    public double PlaytimeSeconds { get; set; }
    public DateTime SavedAtUtc { get; set; }

    public string PlaytimeDisplay
    {
        get
        {
            var span = TimeSpan.FromSeconds(PlaytimeSeconds);
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{span.Minutes}m {span.Seconds}s";
        }
    }
}

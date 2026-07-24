using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

public class CharacterClass
{
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#808080";
    public Dictionary<string, int> StatModifiers { get; set; } = new();
    public Dictionary<string, int> StartingStats { get; set; } = new();
    public Dictionary<string, int> StatGrowth { get; set; } = new();
}

public class CharacterRace
{
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>
    /// Per-attribute effectiveness multipliers (see Info/Racial Effectiveness.md). These do NOT
    /// change base/displayed stats; they scale how effectively a stat translates into results:
    /// EffectiveAttribute = BaseAttribute × Effectiveness. Defaults to 1.0 when absent.
    /// </summary>
    public Dictionary<string, float> Effectiveness { get; set; } = new();
}

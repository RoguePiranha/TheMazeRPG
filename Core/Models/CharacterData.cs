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
    public Dictionary<string, int> StatModifiers { get; set; } = new();
}

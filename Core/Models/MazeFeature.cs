namespace TheMazeRPG.Core.Models;

/// <summary>
/// Special features that can appear in the maze
/// </summary>
public enum MazeFeatureType
{
    Stairs,
    Chest,
    Shrine
}

public class MazeFeature
{
    public int X { get; set; }
    public int Y { get; set; }
    public MazeFeatureType Type { get; set; }
    public bool IsUsed { get; set; }
}

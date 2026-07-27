using Avalonia;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.UI.Controls;

/// <summary>
/// What a right-click landed on, plus where (canvas-relative) to anchor the context menu. Exactly
/// one of <see cref="Feature"/> / <see cref="Corpse"/> is set.
/// </summary>
public class InteractTarget
{
    public MazeFeature? Feature { get; init; }
    public Enemy? Corpse { get; init; }
    public Point ScreenPoint { get; init; }
}

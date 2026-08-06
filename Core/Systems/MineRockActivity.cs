using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Working a single tile of rock until it breaks. Unlike the old MineOreActivity — which stood at a
/// feature and produced ore from nowhere — this destroys a specific tile of the world and gives you
/// what was in it (owner ruling 2026-08-06).
///
/// The tile is remembered by position rather than by reference because the terrain grid holds
/// values, not objects; the tile is re-checked on completion in case something else changed it
/// while the hero was swinging.
/// </summary>
public class MineRockActivity : Activity
{
    public override string Name => "Mining";

    private readonly int _x;
    private readonly int _y;
    private readonly TileType _expected;
    private readonly int _duration;

    public MineRockActivity(int x, int y, TileType expected, int durationTicks)
    {
        _x = x;
        _y = y;
        _expected = expected;
        _duration = System.Math.Max(1, durationTicks);
    }

    public override void OnStart(GameState gameState)
    {
        TicksRemaining = _duration;
        gameState.LogMessage(
            _expected == TileType.OreVein ? "You set to work on the seam." : "You start breaking rock.",
            MessageKind.System);
    }

    public override void OnTick(GameState gameState)
    {
        TicksRemaining--;
        // Noise leaks the whole time you're working, not only when the rock finally gives.
        if (TicksRemaining % 20 == 0)
            gameState.RaiseMiningNoise(_x, _y, _expected);
    }

    public override void OnFinish(GameState gameState) => gameState.CompleteRockMining(_x, _y, _expected);
}

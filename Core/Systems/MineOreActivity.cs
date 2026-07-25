using System;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Mining a resource at the Overworld's MineEntrance. Unlike ChestOpenActivity, this holds no
/// feature reference — the mine is a reusable location, not a one-shot pickup — so it only
/// knows the material/amount/duration it was configured with. Like CraftActivity, "what happens
/// next" is a caller-supplied continuation, not baked in — this activity shouldn't know about
/// the scripted OverworldGoal sequence (a flagged placeholder that real player input replaces).
/// </summary>
public class MineOreActivity : Activity
{
    public override string Name => "Mining Ore";

    private readonly string _materialId;
    private readonly int _amount;
    private readonly int _duration;
    private readonly Action<GameState>? _onComplete;

    public MineOreActivity(string materialId, int amount, int durationTicks, Action<GameState>? onComplete = null)
    {
        _materialId = materialId;
        _amount = amount;
        _duration = Math.Max(1, durationTicks);
        _onComplete = onComplete;
    }

    public override void OnStart(GameState gameState) => TicksRemaining = _duration;

    public override void OnTick(GameState gameState) => TicksRemaining--;

    public override void OnFinish(GameState gameState)
    {
        gameState.AddHeroResource(_materialId, _amount);
        var name = MaterialDataService.Instance.Materials.TryGetValue(_materialId, out var def)
            ? def.Name : _materialId;
        gameState.LogMessage($"Mined {_amount}x {name}", MessageKind.Loot);
        _onComplete?.Invoke(gameState);
    }
}

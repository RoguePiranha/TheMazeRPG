using System;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Opening a chest: a fixed-duration activity tied to one specific MazeFeature. Chests are
/// one-shot/consumed (unlike mining/crafting locations, which are reusable), so holding the
/// feature reference here is the correct, pragmatic exception to activities generally not
/// knowing about map features.
/// </summary>
public class ChestOpenActivity : Activity
{
    public override string Name => "Opening Chest";

    private readonly MazeFeature _feature;
    private readonly int _duration;

    public ChestOpenActivity(MazeFeature feature, int durationTicks)
    {
        _feature = feature;
        _duration = Math.Max(1, durationTicks);
    }

    public override void OnStart(GameState gameState)
    {
        TicksRemaining = _duration;
        _feature.IsOpening = true;
        _feature.OpeningTicks = 0;
        _feature.LightRadius = 0f;
    }

    public override void OnTick(GameState gameState)
    {
        TicksRemaining--;
        _feature.OpeningTicks++;
        _feature.OpenProgress = Math.Min(1f, _feature.OpeningTicks / (float)_duration);
        _feature.LightRadius = _feature.OpenProgress * 2.0f;
    }

    public override void OnFinish(GameState gameState)
    {
        _feature.IsOpening = false;
        _feature.IsUsed = true;
        gameState.GrantChestRewards();
    }

    public override void OnCancel(GameState gameState)
    {
        _feature.IsOpening = false;
    }
}

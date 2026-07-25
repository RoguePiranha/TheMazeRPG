using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// Runs one recipe: consumes its input resources, then produces either more resources
/// ("material" output, e.g. smelting) or a real Combinable ("weapon" output, e.g. forging),
/// fed through the same GameState.AcquireLoot pipeline dungeon loot uses. Takes a caller-
/// supplied onComplete continuation rather than hardcoding what happens next (e.g. chaining
/// into a second recipe, or advancing the OverworldGoal) — keeps this activity generic/reusable
/// instead of baked to one specific recipe chain.
/// </summary>
public class CraftActivity : Activity
{
    public override string Name => _recipe.Name;

    private readonly RecipeDef _recipe;
    private readonly int _duration;
    private readonly Action<GameState> _onComplete;

    public CraftActivity(RecipeDef recipe, int durationTicks, Action<GameState> onComplete)
    {
        _recipe = recipe;
        _duration = Math.Max(1, durationTicks);
        _onComplete = onComplete;
    }

    public override void OnStart(GameState gameState) => TicksRemaining = _duration;

    public override void OnTick(GameState gameState) => TicksRemaining--;

    public override void OnFinish(GameState gameState)
    {
        // Verify all inputs are actually available before consuming any — otherwise a recipe
        // fired without enough resources would silently drive counts negative. The continuation
        // still runs on failure so a chained sequence (e.g. the scripted OverworldGoal flow)
        // can't wedge mid-chain.
        foreach (var kv in _recipe.Inputs)
        {
            if (gameState.Hero.Resources.GetValueOrDefault(kv.Key, 0) < kv.Value)
            {
                GameLog.Debug($"CraftActivity '{_recipe.Name}': missing {kv.Value}x {kv.Key} — nothing crafted.");
                _onComplete(gameState);
                return;
            }
        }

        foreach (var kv in _recipe.Inputs)
        {
            gameState.AddHeroResource(kv.Key, -kv.Value);
        }

        if (_recipe.OutputType == "weapon")
        {
            // AcquireLoot logs the "Equipped/Found ..." message itself.
            for (int i = 0; i < _recipe.OutputAmount; i++)
            {
                var item = CraftedItemCatalog.Build(_recipe.OutputId);
                if (item != null) gameState.AcquireLoot(item);
            }
        }
        else
        {
            gameState.AddHeroResource(_recipe.OutputId, _recipe.OutputAmount);
            var name = MaterialDataService.Instance.Materials.TryGetValue(_recipe.OutputId, out var def)
                ? def.Name : _recipe.OutputId;
            gameState.LogMessage($"{_recipe.Name}: +{_recipe.OutputAmount}x {name}", MessageKind.Loot);
        }

        _onComplete(gameState);
    }
}

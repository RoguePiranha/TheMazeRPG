using System;
using System.Collections.Generic;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Per-frame, UI-thread copy of every mutable collection the render pass enumerates. The custom
/// draw op runs on Avalonia's render thread while GameState.Tick keeps mutating these lists on the
/// UI thread; enumerating the live lists there dies intermittently with "Collection was modified".
/// Captured once per frame in GameCanvas.Render (UI thread), so the render thread only ever walks
/// frozen lists. Shallow copies on purpose: a torn read of an entity's fields is a one-frame
/// visual blip, but the list structure itself can no longer change mid-enumeration.
/// </summary>
public sealed class RenderSnapshot
{
    /// <summary>The maze reference captured once, so one frame can't mix two floors' grids if a
    /// transition swaps CurrentMaze mid-render.</summary>
    public required Maze Maze { get; init; }

    public required List<Enemy> Enemies { get; init; }
    public required List<Projectile> Projectiles { get; init; }
    public required List<Critter> Critters { get; init; }
    public required List<MazeFeature> Features { get; init; }
    public required List<FloatingText> FloatingTexts { get; init; }
    public required List<HitEffect> HitEffects { get; init; }
    public required List<GameMessage> Messages { get; init; }

    // Hotbar contents resolved on the UI thread: HotbarAttackAt walks Hero.Attacks/Loadout, which
    // RefreshAttacks rebuilds during play — another live enumeration the render thread must not do.
    public required Attack?[] HotbarAttacks { get; init; }
    public required Combinable?[] HotbarConsumables { get; init; }
    public required int[] HotbarConsumableCounts { get; init; }

    public static RenderSnapshot? Capture(GameState gameState)
    {
        var maze = gameState.CurrentMaze;
        if (maze == null || gameState.Hero == null) return null;

        int slots = Math.Max(1, gameState.Hero.HotbarCapacity);
        var attacks = new Attack?[slots];
        var consumables = new Combinable?[slots];
        var counts = new int[slots];
        for (int i = 0; i < slots; i++)
        {
            attacks[i] = gameState.HotbarAttackAt(i);
            consumables[i] = attacks[i] == null ? gameState.HotbarConsumableAt(i) : null;
            counts[i] = consumables[i] != null ? gameState.HotbarConsumableCount(i) : 0;
        }

        return new RenderSnapshot
        {
            Maze = maze,
            Enemies = new List<Enemy>(gameState.Enemies),
            Projectiles = new List<Projectile>(gameState.Projectiles),
            Critters = new List<Critter>(gameState.Critters),
            Features = new List<MazeFeature>(maze.Features),
            FloatingTexts = new List<FloatingText>(gameState.FloatingTexts),
            HitEffects = new List<HitEffect>(gameState.HitEffects),
            Messages = new List<GameMessage>(gameState.Messages.Messages),
            HotbarAttacks = attacks,
            HotbarConsumables = consumables,
            HotbarConsumableCounts = counts,
        };
    }
}

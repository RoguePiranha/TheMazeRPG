using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Core.Systems;

/// <summary>
/// A multi-tick task the hero is committed to, such as mining ore or crafting.
/// Lives in Core/Systems (not Core/Models) because it is behavior that
/// mutates GameState, not plain data — Models in this codebase stay pure data.
///
/// The base class deliberately has no reference to a MazeFeature or location — most activities
/// (mining/crafting/selling) are reusable at a location, unlike a one-shot chest, so the trigger
/// site decides *when* to start one, not the activity itself. A concrete activity MAY hold its
/// own feature reference if it genuinely needs one. The point is that the
/// base class doesn't force that dependency on everyone.
///
/// Only one activity can be active at a time (see GameState.CurrentActivity/StartActivity).
/// Callbacks take the owning GameState (not just Hero) — GameState.Hero is one property away,
/// and most activities need broader context anyway (current floor, the shared RNG, AcquireLoot's
/// equip/inventory logic, CodexService), so passing bare Hero would just mean immediately needing
/// a second parameter for every non-trivial activity.
/// </summary>
public abstract class Activity
{
    public abstract string Name { get; }

    /// <summary>Ticks remaining until OnFinish fires. Set by the concrete activity in OnStart.</summary>
    public int TicksRemaining { get; protected set; }

    public bool IsComplete => TicksRemaining <= 0;

    /// <summary>Called once when the activity begins.</summary>
    public virtual void OnStart(GameState gameState) { }

    /// <summary>Called once per game tick while the activity is active (including the final tick).</summary>
    public abstract void OnTick(GameState gameState);

    /// <summary>Called once when TicksRemaining reaches 0 — apply the activity's result here.</summary>
    public abstract void OnFinish(GameState gameState);

    /// <summary>Called if the activity is interrupted before completion (e.g. combat starts).</summary>
    public virtual void OnCancel(GameState gameState) { }
}

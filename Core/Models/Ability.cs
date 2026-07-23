using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// A passive ability (Game Idea.md: "Players start with 1 passive ability"). Applies
/// modifiers rather than producing an attack. Persists across runs and can be merged.
/// </summary>
public class Ability : Combinable
{
    public override CombinableKind Kind => CombinableKind.Ability;

    /// <summary>
    /// Flat or multiplicative modifiers this passive grants, keyed by a stat/effect name
    /// (e.g. "SpellCooldownPct", "ManaRegen", "DodgeChance"). Interpretation is up to the
    /// system that consumes abilities; kept generic so merged abilities can stack effects.
    /// </summary>
    public Dictionary<string, float> Modifiers { get; set; } = new();
}

using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// A character's elemental affinity profile — a 0–100 rating per <see cref="MagicElement"/>.
/// <see cref="Neutral"/> is the baseline every element sits at until it's raised or lowered;
/// at neutral all affinity effects are identity (no bonus or penalty), so a character whose
/// whole profile is neutral behaves exactly as if affinity didn't exist. See AffinityService
/// for the curves that turn these ratings into damage / mana-cost / resistance / tier effects.
/// </summary>
public class Affinities
{
    /// <summary>The rating an unset element sits at; the point where all effects are identity.</summary>
    public const float Neutral = 20f;
    public const float Min = 0f;
    public const float Max = 100f;

    // Only non-neutral entries are stored; Get returns Neutral for anything absent.
    public Dictionary<MagicElement, float> Values { get; set; } = new();

    public float Get(MagicElement element)
    {
        if (element == MagicElement.None) return Neutral;
        return Values.TryGetValue(element, out var v) ? v : Neutral;
    }

    public void Set(MagicElement element, float value)
    {
        if (element == MagicElement.None) return;
        Values[element] = System.Math.Clamp(value, Min, Max);
    }

    /// <summary>Add (or subtract) to an element's rating, clamped to [Min, Max].</summary>
    public void Add(MagicElement element, float delta) => Set(element, Get(element) + delta);

    public Affinities Clone()
    {
        var copy = new Affinities();
        foreach (var kv in Values) copy.Values[kv.Key] = kv.Value;
        return copy;
    }
}

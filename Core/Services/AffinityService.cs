using System.Collections.Generic;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// How one element relates to another, driving the specialization tradeoff: raising affinity in
/// an element bleeds a little into its harmonious partners and drains its opposed ones.
/// From Magic_System_Reference.md §12.
/// </summary>
public enum ElementRelationship
{
    Neutral,
    Harmonious,
    Opposed
}

/// <summary>
/// Turns a character's elemental <see cref="Affinities"/> ratings into concrete effects: outgoing
/// damage power, mana-cost, incoming resistance, learnable spell tier, and grow-with-use
/// development. All curves are anchored to the provisional balance values in
/// Magic_System_Reference.md §12 and are identity at <see cref="Affinities.Neutral"/> (20), so a
/// neutral character is unaffected. Numbers are deliberately tunable — see the doc's open items.
/// </summary>
public static class AffinityService
{
    // --- Effect curves (piecewise-linear around the neutral point of 20) --------------------
    // Endpoints from §12: specialized (~100) vs antithetical (~0), identity at neutral (20).

    /// <summary>Outgoing-damage multiplier for a caster's affinity to the attack's element.
    /// 0.75 at 0 → 1.0 at 20 → 1.25 at 100.</summary>
    public static float PowerMultiplier(float affinity) =>
        1f + Slope(affinity, belowPer100: 0.25f, abovePer100: 0.25f);

    /// <summary>Mana-cost multiplier for a caster's affinity to the attack's element.
    /// 1.60 at 0 → 1.0 at 20 → 0.80 at 100 (cheaper the more affine you are). Note the sign: cost
    /// goes DOWN above neutral and UP below it, the inverse of power/resistance.</summary>
    public static float ManaCostMultiplier(float affinity) =>
        1f - Slope(affinity, belowPer100: 0.60f, abovePer100: 0.20f);

    /// <summary>Fraction of incoming damage of an element removed by the target's affinity to it.
    /// −0.30 (extra vulnerability) at 0 → 0 at 20 → +0.40 (resistance) at 100.</summary>
    public static float Resistance(float affinity) =>
        Slope(affinity, belowPer100: 0.30f, abovePer100: 0.40f);

    /// <summary>Linear interpolation of an effect that is 0 at neutral, `-below` scaled at 0, and
    /// `+above` scaled at 100. `belowPer100`/`abovePer100` are the magnitudes reached at the ends
    /// expressed per full 0→100 span, so the slope on each side is consistent.</summary>
    private static float Slope(float affinity, float belowPer100, float abovePer100)
    {
        float a = System.Math.Clamp(affinity, Affinities.Min, Affinities.Max);
        if (a >= Affinities.Neutral)
        {
            float t = (a - Affinities.Neutral) / (Affinities.Max - Affinities.Neutral); // 0..1
            return t * abovePer100;
        }
        else
        {
            float t = (Affinities.Neutral - a) / (Affinities.Neutral - Affinities.Min); // 0..1
            return -t * belowPer100;
        }
    }

    // --- Spell tier gating ------------------------------------------------------------------

    /// <summary>Highest spell tier a character may learn in an element given their affinity.
    /// Tier 0 = Charm, 1 = Common/Tier 1, etc. Thresholds are provisional. Not yet wired to an
    /// actual spell-learning flow — exposed for when spell research exists.</summary>
    public static int LearnableTier(float affinity) => affinity switch
    {
        < 20f => 0,   // Charm only
        < 45f => 1,   // Common / Tier 1
        < 70f => 2,
        < 90f => 3,
        _ => 4
    };

    public static bool CanLearn(Affinities affinities, MagicElement element, int tier) =>
        LearnableTier(affinities.Get(element)) >= tier;

    // --- Relationship graph (§12) -----------------------------------------------------------

    private static readonly Dictionary<MagicElement, (MagicElement[] harmonious, MagicElement[] opposed)> Relationships = new()
    {
        [MagicElement.Fire] = (new[] { MagicElement.Air }, new[] { MagicElement.Water, MagicElement.Ice }),
        [MagicElement.Ice] = (new[] { MagicElement.Water }, new[] { MagicElement.Fire }),
        [MagicElement.Water] = (new[] { MagicElement.Ice }, new[] { MagicElement.Fire, MagicElement.Lightning }),
        [MagicElement.Lightning] = (new[] { MagicElement.Air }, new[] { MagicElement.Earth, MagicElement.Water }),
        [MagicElement.Air] = (new[] { MagicElement.Fire, MagicElement.Lightning, MagicElement.Sonic }, new[] { MagicElement.Earth }),
        [MagicElement.Earth] = (System.Array.Empty<MagicElement>(), new[] { MagicElement.Air, MagicElement.Lightning }),
        [MagicElement.Poison] = (new[] { MagicElement.Death }, System.Array.Empty<MagicElement>()),
        [MagicElement.Life] = (System.Array.Empty<MagicElement>(), new[] { MagicElement.Death }),
        [MagicElement.Death] = (new[] { MagicElement.Poison }, new[] { MagicElement.Life }),
        [MagicElement.Light] = (System.Array.Empty<MagicElement>(), new[] { MagicElement.Shadow }),
        [MagicElement.Shadow] = (System.Array.Empty<MagicElement>(), new[] { MagicElement.Light }),
        [MagicElement.Sonic] = (new[] { MagicElement.Air }, System.Array.Empty<MagicElement>()),
        // Mana is broadly compatible; Arcane/Void have no ordinary elemental opposites here.
    };

    public static ElementRelationship RelationshipBetween(MagicElement a, MagicElement b)
    {
        if (a == b) return ElementRelationship.Neutral;
        if (Relationships.TryGetValue(a, out var rel))
        {
            foreach (var h in rel.harmonious) if (h == b) return ElementRelationship.Harmonious;
            foreach (var o in rel.opposed) if (o == b) return ElementRelationship.Opposed;
        }
        return ElementRelationship.Neutral;
    }

    // --- Grow-with-use ----------------------------------------------------------------------

    private const float GainPerCast = 0.35f;      // affinity gained in the cast element
    private const float SpillHarmonious = 0.10f;  // small bleed into harmonious partners
    private const float DrainOpposed = 0.20f;     // specialization pulls opposed elements down

    /// <summary>The affinity at which a caster is considered a committed *elementalist* in an
    /// element. Only past this point does further practice of that element erode its opposites —
    /// casual/dabbling magic use never lowers your affinity to other elements. (Tunable; sits at
    /// the tier-2 boundary, i.e. "clearly leaned in, not just dabbling".)</summary>
    public const float ElementalistThreshold = 45f;

    /// <summary>True once <paramref name="element"/> has been developed to specialist level.</summary>
    public static bool IsElementalist(Affinities affinities, MagicElement element) =>
        affinities.Get(element) >= ElementalistThreshold;

    /// <summary>
    /// Practice: a character casting a spell of <paramref name="element"/> raises their affinity to
    /// it (diminishing as it nears the max) and nudges harmonious partners up a little. The
    /// specialization tradeoff — draining *opposed* elements — only applies once the caster has
    /// actually become an Elementalist in this element (affinity ≥ <see cref="ElementalistThreshold"/>),
    /// so ordinary magic use never lowers your affinity to other elements. No-op for non-elemental
    /// attacks (<see cref="MagicElement.None"/>).
    /// </summary>
    public static void OnElementCast(Affinities affinities, MagicElement element)
    {
        if (element == MagicElement.None) return;

        // A committed elementalist's practice erodes opposites — so check specialist status BEFORE
        // this cast's gain, so crossing the threshold doesn't retroactively drain on the same cast.
        bool specialized = IsElementalist(affinities, element);

        // Diminishing gain: slower the closer this element is to the cap.
        float current = affinities.Get(element);
        float headroom = (Affinities.Max - current) / (Affinities.Max - Affinities.Neutral); // 1 at neutral → 0 at cap
        affinities.Add(element, GainPerCast * System.Math.Max(0.15f, headroom));

        if (Relationships.TryGetValue(element, out var rel))
        {
            foreach (var h in rel.harmonious) affinities.Add(h, SpillHarmonious);
            if (specialized)
                foreach (var o in rel.opposed) affinities.Add(o, -DrainOpposed);
        }
    }

    // --- Convenience for combat -------------------------------------------------------------

    /// <summary>Reduce incoming damage by the target's resistance to the given element (or amplify
    /// it if the target is vulnerable, i.e. below-neutral affinity). No-op for non-elemental hits.</summary>
    public static int ApplyResistance(int damage, Affinities targetAffinities, MagicElement element)
    {
        if (element == MagicElement.None || damage <= 0) return damage;
        float scaled = damage * (1f - Resistance(targetAffinities.Get(element)));
        return System.Math.Max(1, (int)System.Math.Round(scaled));
    }

    /// <summary>Effective mana cost of an attack for a caster, after their affinity discount.</summary>
    public static int EffectiveManaCost(Affinities affinities, MagicElement element, int baseCost)
    {
        if (baseCost <= 0) return baseCost;
        return System.Math.Max(1, (int)System.Math.Round(baseCost * ManaCostMultiplier(affinities.Get(element))));
    }

    /// <summary>Seed an affinity profile from data-driven leanings (class/race "affinities" maps),
    /// keyed by MagicElement name and added over the neutral baseline. Unknown names are skipped.</summary>
    public static void SeedFrom(Affinities affinities, params IReadOnlyDictionary<string, float>?[] sources)
    {
        foreach (var src in sources)
        {
            if (src == null) continue;
            foreach (var kv in src)
            {
                if (System.Enum.TryParse<MagicElement>(kv.Key, ignoreCase: true, out var element)
                    && element != MagicElement.None)
                {
                    affinities.Add(element, kv.Value);
                }
            }
        }
    }
}

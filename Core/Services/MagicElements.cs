using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Maps an attack to its <see cref="MagicElement"/>, mirroring <see cref="AttackVisuals"/>. The
/// stable attack id is authoritative; a name-keyword fallback then colors future/combined spells
/// that aren't explicitly mapped yet. The name fallback is cosmetic-only (element color/flavor),
/// so — unlike behavior lookups — a loose name match here is acceptable.
/// </summary>
public static class MagicElements
{
    public static MagicElement For(Attack attack)
    {
        switch (attack.Id)
        {
            case "mana-bolt": return MagicElement.Mana;
            case "magic-dart": return MagicElement.Mana;   // "Mana Dart" — the basic ordered-mana bolt
            case "arcane-blast": return MagicElement.Arcane;
            case "divine-wrath":
            case "holy-touch": return MagicElement.Holy;
            case "sound-wave":
            case "sonic-boom": return MagicElement.Sonic;
            case "fireball": return MagicElement.Fire;
            case "ice-shard": return MagicElement.Ice;
        }

        // Cosmetic name fallback (element color only) for spells without an explicit mapping.
        string n = attack.Name.ToLowerInvariant();
        if (Has(n, "fire", "flame", "burn", "ember", "inferno")) return MagicElement.Fire;
        if (Has(n, "ice", "frost", "freeze", "glacial", "chill")) return MagicElement.Ice;
        if (Has(n, "poison", "venom", "toxic", "blight")) return MagicElement.Poison;
        if (Has(n, "lightning", "thunder", "shock", "storm", "spark")) return MagicElement.Lightning;
        if (Has(n, "water", "tidal", "aqua", "wave", "torrent")) return MagicElement.Water;
        if (Has(n, "holy", "divine", "radiant", "light", "sacred")) return MagicElement.Holy;
        if (Has(n, "death", "necro", "grave", "decay")) return MagicElement.Death;
        if (Has(n, "shadow", "dark", "void", "umbral")) return MagicElement.Shadow;
        if (Has(n, "earth", "stone", "rock", "quake")) return MagicElement.Earth;
        if (Has(n, "air", "wind", "gust", "gale")) return MagicElement.Air;
        if (Has(n, "arcane")) return MagicElement.Arcane;
        if (Has(n, "mana", "magic")) return MagicElement.Mana;
        return MagicElement.None;
    }

    private static bool Has(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
            if (haystack.Contains(needle)) return true;
        return false;
    }
}

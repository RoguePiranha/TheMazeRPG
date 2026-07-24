using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Maps an attack to its projectile <see cref="VisualStyle"/> using the stable attack id,
/// with a sensible fallback by animation. This replaces the old approach of matching
/// substrings of the display name (which silently broke when names didn't match, e.g. the
/// Archer's "Quick Shot"/"Power Shot" rendering as a plain line instead of an arrow).
/// </summary>
public static class AttackVisuals
{
    public static VisualStyle For(Attack attack) => attack.Id switch
    {
        "quick-slash" => VisualStyle.SwordArc,
        "quick-stab" or "quick-jab" => VisualStyle.QuickSlash,
        "devastating-backstab" => VisualStyle.Backstab,
        "quick-shot" or "power-shot" => VisualStyle.Arrow,
        "magic-dart" or "mana-bolt" => VisualStyle.MagicComet,
        "arcane-blast" => VisualStyle.ArcaneRing,
        "divine-wrath" => VisualStyle.MagicMissile,
        "sound-wave" or "sonic-boom" => VisualStyle.Sonic,
        "holy-touch" => VisualStyle.HolyStrike,
        "light-punch" => VisualStyle.ImpactBurst,
        _ => FallbackFor(attack.Animation)
    };

    // Any unmapped attack (including future/merged ones) still gets a sensible effect.
    private static VisualStyle FallbackFor(AttackAnimation animation) => animation switch
    {
        AttackAnimation.Ranged => VisualStyle.Arrow,
        AttackAnimation.Magic => VisualStyle.MagicComet,
        AttackAnimation.Heavy => VisualStyle.HeavyArc,
        AttackAnimation.Quick => VisualStyle.QuickSlash,
        _ => VisualStyle.Blade
    };
}

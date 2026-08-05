# 06 — Agility Time Dilation (PR 4b)

**Owner design 2026-08-04:** past a certain Agility, the player stops speeding up on screen — the world slows down around them instead. High-Agility entities still appear to move (near-)normally; low-Agility ones slow, relative to the player's perception. Rationale: uncapped hero speed becomes unplayable (and physically unsound — projectile tunneling) at high Agility.

Runs entirely on the note 05 `TimeScale` substrate — this PR is one formula, one cap, one screen cue.

## Mechanic

```csharp
// Core/Services/TempoService.cs  (static, like AffinityService — all tunables here)
public static class TempoService
{
    public const float DilationThreshold = 20f;   // A*: effective Agility where dilation begins ⚠ owner tunable
    public const float TempoPerAgility  = 0.05f;  // matches existing speed curves (MovementSystem 0.08*(1+Agi*0.05))
    public const float MinWorldScale    = 0.35f;  // world never freezes ⚠ owner tunable

    public static float Tempo(float effectiveAgility) => 1f + TempoPerAgility * effectiveAgility;

    /// On-screen speed multiplier for the HERO's own movement: grows to the cap, then flat.
    public static float HeroScreenSpeedScale(float heroAgi) =>
        Tempo(Math.Min(heroAgi, DilationThreshold)) / Tempo(0);

    /// TimeScale factor for every OTHER entity (feeds RecomputeTimeScale, note 05).
    public static float DilationScale(float entityAgi, float heroAgi)
    {
        if (heroAgi <= DilationThreshold) return 1f;
        return Math.Clamp(Tempo(entityAgi) / Tempo(heroAgi), MinWorldScale, 1f);
    }
}
```

Properties (worth asserting, they ARE the design):
- Hero at/below A*: identical to today. **Zero change for every existing character** — floor-1 heroes have low Agility.
- Enemy Agility ≈ hero Agility → ratio ≈ 1 → appears normal. ✓ owner spec.
- Low-Agility entity → crawls toward `MinWorldScale`. ✓
- Two high-Agility duelists barely notice each other's dilation — relative tempo is what renders.
- Continuous at the threshold (no pop): at `heroAgi = A*` the ratio is `Tempo(e)/Tempo(A*) ≥ 1` for `e ≥ A*` → clamps to 1; below-A* entities were *already* slower via their own movement stats, the dilation just extends that curve.

## Integration points

1. **Hero movement**: `MovementSystem.MoveHeroByDirection` — wherever hero speed derives from Agility, use `HeroScreenSpeedScale` in place of the raw curve (auto-mode hero movement same). This *is* the cap.
2. **World**: `RecomputeTimeScale` (note 05) calls `DilationScale(entity.EffectiveAgility, Hero.EffectiveAgility)` as its first factor — enemies, their cooldowns, their wander cadence all inherit it.
3. **Projectiles**: already owner-scaled by note 05 → a slowed archer's arrow drifts, dodgeable by walking. The Matrix moment is free.
4. **Hero-frame things stay hero-frame**: hero cooldowns/regen/dash unchanged — acting more often *relative to the world* is the whole payoff.
5. **NPCs in town** (post note 09): schedules tick in world-clock time (unscaled); only their *visible walking* slows. A dilated hero watches the town in amber, nobody teleports. Guard combat responses use scaled movement like any enemy.
6. **Composition**: dilation multiplies with Chill/terrain factors and shares the same 0.35 floor (the clamp in `RecomputeTimeScale` is final authority). Pause and turn-based mode freeze the tick driver — orthogonal, verify no double-application on resume.

## UX

- **Screen cue** while `heroAgi > A*` and any on-screen entity is scaled < 0.9: slight desaturation + 2% vignette (reuse the low-HP vignette plumbing, MazeRenderer.DrawLowHealthVignette pattern) — reads as a power, not a bug.
- HUD: small "⌛ ×0.6" world-tempo readout next to the AUTO/MANUAL indicator (shows the *average* scale of visible enemies).
- Message log, first activation per run: "The world seems to slow around you." (System kind.)

## Balance guardrails

- Attack cooldowns of *enemies* scale, so a maxed-Agility hero effectively gains DPS-taken reduction ≈ world scale. That's intended (Agility's promised dodge/speed fantasy) but monitor: if it eclipses Constitution builds, raise `MinWorldScale` toward 0.5 before touching the curve shape.
- `RollDodge` (GameState.cs, base 3% ±2%/point) stays as-is — dilation is perceptual/positional advantage; passive dodge remains its own axis.
- **Owner ruling 2026-08-05 — player-controlled, combat-default:** dilation defaults to **active in combat only** (`Hero.InCombat || hostile visible` gates `DilationScale`) so the world feels natural outside fights. A settings entry exposes it: `DilationMode` (**InCombat** default / Always / Off) and `DilationStrength` (0–100% slider, lerping between no-effect and the full Agility-derived scale) — players set their dilation *level*, but never past what their Agility has earned; the Agility curve is the cap, the slider only dials down. Persisted in settings, not the save.

## TEST_TEMPO

Headless, fixed seeds:
1. Hero Agi 10 (below A*): all entity scales = 1; hero speed matches pre-PR formula exactly (regression identity).
2. Hero Agi 40 vs enemy Agi 5: enemy scale = clamp(1.25/3.0) = 0.42; its projectile advances at 0.42×; its 30-tick cooldown takes ~71 ticks.
3. Hero Agi 40 vs enemy Agi 35: scale ≈ 0.92 — "appears normal" band.
4. Hero on-screen displacement per tick identical at Agi 20 vs Agi 60 (the cap).
5. Chill + dilation compose multiplicatively and clamp at 0.35.
6. Pause/resume and mode toggles leave scales unchanged.
Full regression suite (low-Agility defaults ⇒ zero drift) + owner hands-on for feel and the two ⚠ tunables.

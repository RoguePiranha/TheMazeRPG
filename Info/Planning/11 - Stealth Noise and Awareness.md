# 11 — Stealth: Sneaking, Noise, and Awareness (PR 5b)

**Owner addition 2026-08-04.** Design reference: [../Stealth System.md](../Stealth%20System.md). Three pieces: a sneak movement mode, a noise/sound event model, and a hidden two-way perception system replacing binary detection. Ships after PR 5 (the stealth-strike payoff composes with the note-07 backstab), before the NPC PRs (guards inherit all of it).

**The structural change:** enemies currently have effectively 360° sight — `EnemyCanSeeHero` (GameState.cs:1757-1771) brute-forces 8 cones at 45°. Stealth requires **facing-based vision cones + hearing**. Tuning contract: cone + footstep hearing together ≈ today's detection for a *non-sneaking* hero — sneaking is where behavior diverges. `TEST_STEALTH` asserts this equivalence explicitly.

## 1. Noise events

```csharp
// Core/Models/SoundEvent.cs — transient, emitted into GameState.SoundsThisTick, consumed by
// the awareness pass the same tick, then cleared. Never rendered, never persisted.
public readonly record struct SoundEvent(float X, float Y, float Radius, SoundKind Kind, bool FromHero);
public enum SoundKind { Step, Combat, Door, Work, Alarm }
```

Emission sites and radii (one static table `NoiseTable`, all tiles, all tunable):

| Source | Radius | Site |
|---|---|---|
| Walk step | 4.0 | movement code, every 5 ticks while moving |
| Sneak step | 1.5 | every 8 ticks while moving |
| Dash | 7.0 | `TryDash` |
| Melee swing | 5.0 / Ranged shot 3.0 / Spell cast 6.0 | `SpawnHeroProjectile` + enemy equivalent |
| Explosion payload (note 07) | 10.0 | on detonation |
| Door bump-open | 5.0 | note 02 bump path |
| Chest opening | 4.0, re-emitted every second of the channel | `ChestOpenActivity.OnTick` |
| Mining / crafting | 8.0 | activity ticks (town + future dungeon ore) |
| Trap springing | 8.0 | `TriggerTrap` |

**Gear noise multiplier** on Step sounds: `stepMult = Π over equipped Loadout attributes — Heavy ×1.4, Light ×0.85` (clamped 0.6–2.0). When armor becomes real equipment its weight class dominates this term (note the hook, don't build armor here).

**Occlusion (v1, cheap):** effective radius = `LOS(listener, source) ? r : r × 0.6`. No per-sound BFS — walls muffle, that's enough fidelity for now.

## 2. Awareness (per enemy — and per NPC when note 09 lands)

```csharp
// On Enemy (hoisted to the shared actor surface later):
public float Awareness;                       // 0..100, decays
public float FacingRad;                       // set from movement dir each tick it moves
public (float x, float y)? InvestigateTarget;
public AwarenessState State => Awareness < 30 ? Unaware : Awareness < 70 ? Suspicious : Alert;
```

Per-tick pass (replaces the binary `EnemyCanSeeHero` gate in `CheckCombat`):

```csharp
// SIGHT — facing cone (VisionConeRad = 2.1f ≈ 120°), range 7.5 (unchanged), LOS required:
if (InCone(enemy, hero) && HasLOS && dist <= VisionRange)
{
    float distF   = Lerp(1.0f, 0.25f, dist / VisionRange);            // close = fast
    float stealth = Hero.IsSneaking
        ? Math.Clamp(0.5f + Hero.EffectiveAgility * 0.02f - GearNoisePenalty(), 0f, 0.9f)
        : 0f;                                                          // not sneaking = fully visible
    float percep  = 0.8f + enemy.EffectiveWisdom * 0.02f;              // Wis = perception (symmetry w/ PerceptionService)
    Awareness += SightGainPerSec /*60*/ * distF * (1 - stealth) * percep / TickRate;
}
if (dist <= 1.5f) Awareness = 100;                                     // bumped into

// HEARING — 360°, from SoundsThisTick:
foreach sound where EffectiveDist(enemy, sound) <= sound.Radius * (Hero.IsSneaking && sound.Kind==Step ? 1f : 1f):
    Awareness = Max(Awareness, sound.Kind == SoundKind.Combat ? 100 : Min(69, Awareness + 35));
    InvestigateTarget = (sound.X, sound.Y);                            // location leak, not identity

// DECAY: -8/sec when no stimulus this tick; Suspicious→Unaware resumes wander.
```

- **Non-sneak equivalence:** walking emits 4-tile steps every 0.5 s and sight gain is `stealth=0` → detection inside ~5 tiles lands within a couple of ticks of today's instant flip. Behind-the-back walking still alerts via steps. Only *sneaking* opens the gap.
- **Suspicious behavior:** goal-walk to `InvestigateTarget` at wander speed ×1.3 (reuse `MoveEnemyTowardTarget`), facing-sweep ±60° for ~3 s on arrival, then decay out. Room packs: a Combat-kind sound alerts the *whole pack* (pack members share the event — one scream wakes the room).
- **Alert = existing combat path unchanged** (pursuit window, last-known-position chase, all of it). On pursuit expiry (GameState.cs:734-738) drop to Suspicious at `_enemyLastKnownHeroPos` instead of straight to wander — continuity for free.
- Guardian arenas: Guardian spawns Alert (it's a set-piece; no cheesing the gate).

## 3. Hero-side: sneak mode + stealth strike

- **Input:** `C` toggles sneak (**confirmed toggle**, owner ruling 2026-08-05). Works in Manual mode; Auto ignores it v1. `Hero.IsSneaking`; speed ×0.55 in `MoveHeroByDirection`; dash breaks sneak.
- **Sneak overlay (owner ruling 2026-08-05: "sneaking should show enemy vision and show visually how much sound the player is making"):** while sneaking, the renderer draws (1) **enemy vision cones** — translucent arcs (facing ± cone half-angle, out to `VisionRange`, LOS-clipped so they don't paint through walls) for every currently-rendered enemy, tinted by that enemy's awareness state (gray→yellow→red); and (2) a **noise ring** around the hero — a circle at the current step-sound radius (gear multiplier included) that pulses on each emitted step, so Heavy armor is *visibly* loud. Both vanish the instant sneak toggles off; both are render-only (`MazeRenderer`, no sim reads). Classic Commandos/Shadow Tactics affordances — the hidden rolls stay hidden, but the *geometry* of stealth is honest.
- **Stealth strike:** in the damage path (both target-locked and directional), if target `State == Unaware` → treat as backstab (note 07 positional bonus applies regardless of angle) **+ auto-crit**. ⚠ owner open Q3 on stacking. The attack's own Combat sound then wakes neighbors — one free hit, not a massacre loop (unless one hit kills, which is the assassin fantasy working as intended).
- **HUD feedback (the "hidden" in hidden perception — states, never numbers):** eye icon bottom-HUD: closed/gray = unseen by all, half/yellow = someone Suspicious, open/red = Alert. Per-enemy overhead pips: **?** yellow (Suspicious), **!** red (Alert) — reuse the trap-spot `_heroAlertTicks` pop pattern (GameState.cs:1447 area). Message log on first Suspicious ("Something heard you.") and Alert.
- **TimeScale interplay:** awareness gain/decay tick in *world* frame; a dilated (note 06) sneaking hero is genuinely ghostlike — intended Agility-build payoff, noted in balance watch.

## 4. Integration notes

- `PerceptionService` stays hero→world (traps); this system is world→hero. Keep them separate services (`AwarenessService` for curves/constants) but **share the stat philosophy**: Wisdom perceives, Agility hides, abilities plug in via `+ abilityBonus` hooks exactly like PerceptionService's (Detect Traps ↔ future Keen Senses / Silent Step abilities).
- NPC/guard reuse (note 09): guards get the same awareness component; crime detection = Combat sounds + sight of assault; night hooks (reduced `VisionRange` by clock phase) land with the world clock. The dark-temple infiltration and Thief/Assassin unlock triggers consume `IsSneaking`/Unaware states.
- Renderer: no sound rendering v1 (radii visible only under a `DEBUG_NOISE` overlay env var, matching `DEBUG_LOS`).

## TEST_STEALTH

Fixed seeds: (1) non-sneak equivalence — walking hero at 4 tiles in/out of cone detected within 3 ticks of the pre-PR baseline; (2) sneaking at 5 tiles in cone: awareness slope ratio vs. walking matches formula; Agility 20 vs 5 slopes differ per curve; (3) behind wall: zero sight gain, muffled hearing (×0.6) verified at the boundary radius; (4) step sound → enemy walks to within 1 tile of emission point, sweeps, decays back to wander; (5) Combat sound alerts full room pack; (6) stealth strike from Unaware applies backstab+crit exactly once, neighbors go Alert next tick; (7) Heavy-gear hero heard farther than Light-gear (radius math); (8) high-Wis enemy out-detects low-Wis (slope assert); (9) Guardian spawns Alert. Full regression — `TEST_SIM` 17/17 must hold (auto-play never sneaks, equivalence contract covers the rest).

# 05 — Status Effects + Per-Entity TimeScale (PR 4)

The enabler for combat identity (note 07), agility dilation (note 06), and terrain slows (note 04). Today the codebase has **zero** status/DoT/stun/slow code (grep-verified) — the only persistent combat states are dash i-frames and screen shake.

## 1. Model

```csharp
// Core/Models/StatusEffect.cs
public enum StatusType { Burn, Chill, Freeze, Shock, Toxin, Stagger, Blind, Slow, Regen }

public class StatusEffect
{
    public StatusType Type;
    public float Magnitude;        // meaning per type: DoT dmg/sec, slow fraction, accuracy penalty…
    public int TicksRemaining;
    public int Stacks = 1;
    public MagicElement SourceElement = MagicElement.None;   // resistance + rendering tint
}
```

Both `Hero` and `Enemy` get `List<StatusEffect> Statuses = new()`. (When note 09 introduces a shared `Actor` surface, `Statuses` and `TimeScale` are exactly what it hoists.)

## 2. The TimeScale substrate — build this first, statuses ride on it

One multiplier, one authority, consumed everywhere an entity advances through time:

```csharp
// On Hero and Enemy:
public float TimeScale { get; private set; } = 1f;

// GameState, once per tick per entity — the ONLY writer:
void RecomputeTimeScale(entity)
{
    float s = 1f;
    s *= DilationScale(entity);                        // note 06; returns 1 until that PR
    foreach (var st in entity.Statuses)
        if (st.Type is StatusType.Chill or StatusType.Slow)
            s *= 1f - Math.Min(0.6f, st.Magnitude * st.Stacks);
    s *= TerrainScale(entity);                          // water 0.7 / mud 0.85, note 04
    entity.TimeScale = Math.Clamp(s, MinTimeScale /*0.35*/, 1f);
    if (entity.Statuses.Any(st => st.Type is StatusType.Freeze or StatusType.Stagger
                                  || (st.Type == StatusType.Shock && st.TicksRemaining > 0)))
        entity.TimeScale = 0f;                          // hard stops are absolute
}
```

**Consumption points (each is a one-line change — multiply the per-tick delta):**
- Movement: `MovementSystem.MoveEnemyTowardTarget` / `MoveEnemySmoothRandom` speed × `enemy.TimeScale`; `MoveHeroByDirection` × `Hero.TimeScale`.
- Attack cooldowns: cooldown counters decrement by `TimeScale` per tick instead of 1 (make the counters `float` — they're tick ints today).
- Projectiles: `Projectile.OwnerTimeScale` snapshot at spawn, **re-synced each tick from the living owner** (so chilling an archer mid-flight slows their arrow); position advance × it. Hero-owned projectiles use `Hero.TimeScale`.
- Regen: enemy-side regen (if any) × scale; hero regen stays real-time.
- Enemy AI cadence (wander repick timers, pursuit windows) × scale.

**Explicitly not scaled:** the tick driver itself, UI, activity durations (chest opening etc. — hero-frame), and NPC *schedules* (world-clock time, note 09).

## 3. Application + resistance

```csharp
// GameState — single entry point, called from projectile hits (ProcessProjectileCollisions)
// and later from terrain/abilities:
public void ApplyStatus(object target, StatusType type, float magnitude,
                        float seconds, MagicElement element)
{
    float resist = ResistanceFor(target, element);      // AffinityService.Resistance — exists
    if (type is StatusType.Freeze or StatusType.Shock or StatusType.Stagger)
        if (_random.NextDouble() < resist) return;      // hard CC: resist = avoid outright
    int ticks = (int)(GameSettings.SecondsToTicks(seconds) * (1f - 0.5f * resist));

    var existing = target.Statuses.FirstOrDefault(s => s.Type == type);
    if (existing != null)
    {
        if (StackingTypes.Contains(type)) { existing.Stacks = Math.Min(MaxStacks[type], existing.Stacks + 1); }
        existing.TicksRemaining = Math.Max(existing.TicksRemaining, ticks);   // refresh, don't sum
        existing.Magnitude = Math.Max(existing.Magnitude, magnitude);
    }
    else target.Statuses.Add(new StatusEffect { ... });

    // Chill escalation: 3 Chill stacks → consume them, apply Freeze 0.8s (once per 6s per target)
}
```

## 4. Per-tick processor

`GameState.Tick()`, right after `UpdateProjectilesAndEffects()`: for every entity — DoT types deal `Magnitude / TickRate` damage per tick (accumulate fractional, apply on ≥1, same idiom as the regen accumulators); decrement `TicksRemaining`; drop expired; then `RecomputeTimeScale`. DoT kills route through `HandleEnemyDefeated` / the hero-death path so XP/permadeath stay correct. Statuses clear on floor transition (`StartNewFloor`) for the hero and die with their enemy.

## 5. v1 table (data, one static dictionary — numbers are starting points)

| Status | Element source | Magnitude | Duration | Stacks | Effect |
|---|---|---|---|---|---|
| Burn | Fire | 3 dmg/s | 3 s | no | DoT, refresh on re-apply |
| Chill | Ice | 0.15 slow | 4 s | ×3 | TimeScale −15%/stack; 3 stacks → Freeze |
| Freeze | Ice (escalation) | — | 0.8 s | no | TimeScale 0; broken early by taking a hit |
| Shock | Lightning | — | 0.5 s | no | TimeScale 0 (interrupt); 6 s per-target immunity after |
| Toxin | Poison | 1.5 dmg/s | 6 s | ×5 | DoT × stacks — ramps, rewards pressure |
| Stagger | Earth | — | 0.4 s | no | TimeScale 0 + applied alongside knockback |
| Blind | Air | 0.30 | 3 s | no | Accuracy −30 (feeds `RollDodge`'s attacker-accuracy input) |
| Slow | Water | 0.20 slow | 3 s | no | TimeScale −20% |
| Regen | Life/Light | heal/s | n | no | inverse DoT (Priest kit, note 07) |

Hard-CC on the **hero** halves duration (feel guard); never chains (immunity windows above).

## 6. Rendering + log

- Entity tint pulse by strongest status (Burn orange, Chill pale blue, Toxin green, Shock yellow flicker, Freeze solid pale) + tiny stack pips under the HP bar.
- `MessageLog`: apply/expire lines for the hero only (Combat kind); enemy applications only on first stack (spam guard).

## 7. Turn-based mode — world-waits (owner ruling 2026-08-05; PR 4c)

The long-promised additive precision tool (Implementation Plan §0a), confirmed in-milestone with the **world-waits** model: time flows only while the player acts — Superhot/CDDA feel, zero input redesign.

- `GameState.IsTurnBased` toggle (`T` key; HUD indicator beside AUTO/MANUAL). When ON and the player is idle — no held move keys, no dash in flight, no attack just fired, no activity channeling — `Tick()` skips the entire simulation body (enemies, projectiles, cooldowns, statuses, regen, the hero's own timers: *everything* freezes, or ranged attackers would eat free cooldowns). While the player moves or acts, the sim runs normally for exactly those ticks.
- Implementation is a gate, not a TimeScale write: `if (IsTurnBased && !PlayerActedThisTick) return;` early in `Tick()` after input processing — composes trivially with pause (separate flag), dilation (which operates *within* running ticks), and the world clock (frozen while the world waits; the town doesn't age while you line up a shot — deliberate, it's a precision tool, not a time machine). Auto mode + turn-based is a no-op combination; entering Auto clears the toggle.
- Message-log hint on first toggle. `TEST_TURNBASED`: idle N ticks → zero state drift (hash the sim state); held movement advances enemies/projectiles proportionally; a fired projectile completes its flight only across acting ticks; toggling off resumes real-time exactly.

## TEST_STATUS

Headless: Burn ticks exact damage over duration then expires; Chill×3 → Freeze fires once and respects immunity; Toxin stacks multiply DoT; Shock interrupts an enemy mid-cooldown (attack lands later than an unshocked control); Blind lowers hit rate over 500 shots (statistical band); TimeScale floor holds (0.35 with everything stacked); DoT kill grants XP + underdog multiplier; statuses clear on floor change. Full regression suite (auto-combat paths apply no statuses yet — zero behavior change until note 07 wires payloads).

# 07 — Attack Behavior Payloads + Kit Identity + Spell Content (PR 5, PR 7)

Today attacks differ almost purely by numbers: 5 `AttackAnimation` physics archetypes (CombatSystem.cs:273-305), one special-cased AoE (`attack.Id == "arcane-blast"`, CombatSystem.cs:302), and the ArcaneRing expanding hitbox (GameState.cs:867-871). `KnockbackDistance`/`ParryChance` are authored but never read. This PR makes behavior *data*.

## 1. Behavior payload (PR 5)

```csharp
// Core/Models/AttackBehavior.cs — one POCO carried by Weapon/Spell, cloned into Attack via ToAttack()
public class AttackBehavior
{
    // On-hit status
    public StatusType? OnHitStatus;
    public float StatusChance = 1f, StatusMagnitude, StatusSeconds;

    // Projectile shape/behavior
    public int   PierceCount;        // enemies passed through (consumes 1 per hit)
    public int   ChainCount;         // re-target nearest enemy within ChainRange on hit, dmg ×ChainFalloff
    public float ChainRange = 3f, ChainFalloff = 0.7f;
    public float ExplodeRadius;      // AoE at impact point, dmg ×ExplodeFalloff at edge
    public float ExplodeFalloff = 0.5f;
    public int   MultiShot = 1;      // simultaneous projectiles
    public float SpreadDeg = 15f;    // fan for MultiShot
    public float HomingTurnRate;     // rad/tick toward nearest visible enemy; 0 = none
    public Geometry Shape = Geometry.Bolt;   // Bolt | Cone | Beam  (v1: Cone/Beam = short-lived shaped hitboxes)
    public float ConeAngleDeg = 60f, BeamLength = 5f;

    public float Knockback;          // ABSORBS Attack.KnockbackDistance (delete the dead field)
    public float LifestealPct;       // Shadow identity
}
```

**Consumption:**
- `SpawnHeroProjectile` / enemy spawn: copy payload onto `Projectile` (or a reference — payloads are immutable at runtime).
- `ProcessProjectileCollisions` (GameState.cs): on hit → roll `OnHitStatus` via `ApplyStatus` (note 05); `PierceCount-- > 0` → don't despawn; `ChainCount` → respawn toward next target with falloff; `ExplodeRadius` → radial damage query + `HitEffect` ring; `Knockback` → displace target along projectile heading, per-axis wall-clamped (reuse `TryNudgeHeroAxis`/`NudgeEnemyAxis` idiom, GameState wall-push fix); `LifestealPct` → heal owner.
- Projectile update: `HomingTurnRate` steers velocity; `Cone/Beam` spawn as N sub-hitboxes or a swept segment for 2–4 ticks (cheapest correct thing; refine visuals later).
- **Delete** the `arcane-blast` id check and the ArcaneRing radius special case — arcane-blast's payload becomes `ExplodeRadius 1.2` (+ keep the ring visual via `AttackVisuals`).
- Enemy attacks flow through the same payload path (their `Combinable`-projected attacks already exist) — a Mage enemy's fireball burns you. Guardian payloads scale with the same data.

## 2. Kit identity pass (PR 5, same PR — it's just data + the payloads)

| Class | Change | Payload |
|---|---|---|
| Warrior | Quick Slash → short Cone (feels like an arc); Heavy Cleave gets its authored knockback honored + Stagger | `Shape=Cone 70°`; `Knockback 0.5, OnHit Stagger 0.4s` |
| Rogue | **Positional backstab**: if hero-to-target angle is within 60° of target's facing-away vector → dmg ×1.6 (check in damage resolution, not payload — positions exist) | Devastating Backstab ×2.0 from behind instead |
| Archer | Power Shot pierces | `PierceCount 2` |
| Mage | Mana Dart unchanged (the reliable bolt); Arcane Blast = explode payload; fireball/ice-shard get element statuses | fireball `ExplodeRadius 0.9, Burn`; ice-shard `Chill ×1` |
| Priest | Holy Touch heals self 20% of damage dealt; Divine Wrath small explode + Regen self | `LifestealPct 0.2` / `ExplodeRadius 0.8, Regen(self) 2/s 3s` |
| Bard | Sound Wave/Sonic Boom knockback finally real + Shock chance on Boom | `Knockback 0.3/0.8` (authored values), Boom `Shock 15%` |
| Wanderer | Heavy Strike knockback + small Stagger (brawler identity) | `Knockback 0.4, Stagger 0.3s 30%` |

Element identity defaults (applied wherever an attack's `MagicElement` is set and no explicit payload overrides): Fire→Burn, Ice→Chill, Lightning→Shock+`ChainCount 1`, Poison→Toxin, Earth→Knockback+Stagger, Air→Blind+push, Water→Slow, Shadow→`LifestealPct 0.15`, Light/Holy→bonus vs. (future) undead tag + minor self-Regen. One table in `MagicElements` next to the palette.

## 3. Spell content: Tier-0 charms → Tier-1 (PR 7)

New `Data/Spells/spells.json`, loaded by a `SpellDataService` (static singleton, same pattern as `MaterialDataService`). Schema per entry:

```json
{
  "id": "ember", "name": "Ember", "tier": 0, "element": "Fire",
  "damage": 4, "range": 2.5, "cooldown": 14, "manaCost": 4,
  "animation": "Magic", "visual": "MagicComet",
  "behavior": { "onHitStatus": "Burn", "statusChance": 0.5, "statusMagnitude": 2, "statusSeconds": 2 },
  "evolvesTo": "fire-bolt", "learnRequiresAffinityTier": 0
}
```

Content: the Magic doc §9 lines — Mana Dart→Mana Bolt (exists, gains the upgrade), Ember→Fire Bolt, Frost→Ice Bolt (Chill), Sting→Poison Spray (Cone+Toxin), Spark→Lightning Bolt (Chain 1), Mend→Rejuvenate (self-Regen), Wither→Death Ray (Beam+lifesteal), Gleam→Light Ray (Beam), Rock Throw→Stone Shard (Knockback), Gust→Wind Blade (push+Blind), Pulse→Sonic Blast (Knockback AoE), Null→Void Bolt (flat, high dmg, no status — Void's identity comes later). 12 lines × 2 tiers ≈ 24 entries, all expressible with §1 payloads — that's the acceptance test of the payload design.

Entry paths: `LootService` pool gains Tier-0 charms (low floors) and Tier-1 (floor 6+); the **trainer** (note 09 §4.4) sells/teaches gated by `AffinityService.CanLearn` — the first real consumer of the dormant tier gate (AffinityService.cs:69-79).

`CombinableCatalog`'s hardcoded fireball/ice-shard migrate into the JSON (ids preserved so visuals/element maps hold).

## Verification

- `TEST_BEHAVIOR`: per-payload asserts — pierce hits exactly N+1 targets in a line; chain jumps ≤ ChainRange with falloff; explode damages by radius with falloff; knockback displaces and wall-clamps; homing curves within turn-rate; multishot spreads N projectiles; backstab multiplier fires only from behind cone; lifesteal heals owner; every §3 JSON entry loads, projects to `Attack`, and fires without fallback warnings.
- Regression: `TEST_SIM` 17/17 must still resolve — payload-less attacks behave identically; the kit changes will shift TEST_BALANCE numbers, re-baseline them deliberately in the PR (call out deltas in the log).
- GUI smoke + owner feel pass: cleave cone, backstab, pierce, explosions.

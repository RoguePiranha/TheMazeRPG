# 10 — Progression Depth (PR 10, PR 11)

Growth beyond stat points: spells level with use (Int = mastery speed), abilities finally function, rarity means power, and the first slice of the 115-class tree with a cross-character meta store.

## PR 10 — spell leveling, ability effects, rarity power

### 1. Spell/skill leveling with use

`Combinable.Level` exists but is only written by the merge engine. Add use-XP:

```csharp
// Combinable additions
public int UseXp;
public int UseXpToNext => 50 * (Level + 1) * (Level + 1);      // quadratic like hero XP
public const int MaxLevel = 10;

// GameState — award at the two meaningful moments:
//   cast (DeductAttackCost site): +1 XP
//   hit  (ProcessProjectileCollisions, damage > 0): +3 XP  (hits are the real teacher)
void GrantSkillXp(Combinable c, int baseXp)
{
    // OWNER RULING 2026-08-04: Intelligence governs mastery speed (replaces the removed Cha char-XP bonus)
    float masteryRate = 1f + Hero.EffectiveIntelligence * 0.03f;   // tunable
    // (Faith-costed skills: swap in EffectiveWisdom when faith skills level — noted, not built)
    c.UseXp += (int)Math.Round(baseXp * masteryRate);
    while (c.UseXp >= c.UseXpToNext && c.Level < Combinable.MaxLevel) { c.UseXp -= c.UseXpToNext; c.Level++; OnSkillLevelUp(c); }
}
```

- **Level scaling** applied at `ToAttack()` projection (single seam, already exists): `damage × (1 + 0.04·Level)`, `cooldown × (1 − 0.015·Level)`, `resourceCost × (1 − 0.02·Level)` — modest by design; merging stays the big-power move (`CombinationEngine`'s level-min/reset rules untouched).
- **Attribution:** projectiles know their `Attack.Id`; map back to the `Loadout` Combinable by id at the award sites.
- **Evolution choices** (`OnSkillLevelUp` at levels 4, 8): if `spells.json` lists `evolvesTo` (note 07 §3) and affinity tier allows → message-log offer + a choice entry in the inventory screen ("Evolve Ember → Fire Bolt?"); evolving swaps the Combinable (id changes, level resets to 0, Codex records the discovery). Player-driven, never automatic.
- **Persistence:** `UseXp` rides free — Loadout/Inventory already round-trip polymorphically; just add the field. Melee weapon skills level identically (Weapons are Combinables too) — "skills" ≠ only spells.

### 2. Ability effects engine — Dense Musculature finally works

`Ability.Modifiers` (string→float) is currently read by exactly one tooltip. Route it through one stat pipeline:

```csharp
// Hero — ONE choke point for every derived stat (extend the existing Effective* properties):
public float EffectiveStrength =>
    (Strength + GearBonus("Strength")) * StrengthEffectiveness * AbilityMult("StrengthMult");

private float AbilityMult(string key) =>
    EquippedAbilities.Aggregate(1f, (m, a) => m * a.Modifiers.GetValueOrDefault(key, 1f));
```

- Supported keys v1 (validate on load; warn on unknown): `StrengthMult` (Dense Musculature 1.5 ✓), `SpellCooldownPct` (Mana Circuitry −0.1 → applied in `ToAttack` cooldown), `ManaRegen` (+1/s → the regen accumulators), plus `DodgeBonus`, `PerceptionBonus`/`DisarmBonus` — the exact hooks `PerceptionService` left open (`base + statTerm + abilityBonus`, PerceptionService.cs:7,49) for Detect/Disarm Traps abilities.
- **Ability slots**: `Hero.EquippedAbilities` (≤ `AbilitySlots`, from races.json — Human 3 per Game Idea.md; add the field, default 3). Equip at the inventory screen; **combining abilities still requires a Shrine** (`CombinationEngine` location gating already enforces this).
- Recompute pools (`UpdateHeroResourcePools`) on ability equip/unequip, same as stat spends.

### 3. Rarity → power — **IMPLEMENTED 2026-08-05** (with a model ruling)

**Owner ruling: rarity is intrinsic to the definition** — plain Iron gear is Common forever; loot never re-rolls rarity (LootService selects *which* entry drops, rarity-weighted and floor-boosted); rarity climbs only through combining/crafting. Scaling lives in `Core/Models/RarityScaling.cs`, applied at the projection/consumption seams: `ToAttack()` damage ×(1+0.08·tier) and cooldown ×(1−0.03·tier, floored at 0.70); `EquipmentDefenseBonus` ×(1+0.15·tier); consumable effects ×(1+0.25·tier). Verified by `TEST_RARITY`. Sell prices already scaled via `RarityPoints` — untouched.

### TEST_PROGRESSION (PR 10 exit)

Cast N fireballs → level rises, Int 10 hero levels ~30% faster than Int 1 (exact formula assert); leveled spell's projected damage/cooldown match formulas; evolution offer fires at L4 with sufficient affinity and is refused below; Dense Musculature multiplies effective Str 1.5× (and pools recompute); Mana Circuitry shortens projected cooldowns 10%; rarity scaling exact per tier; `UseXp`/`EquippedAbilities` round-trip save/load. Full regression.

## PR 11 — class specialization v1 + meta store

### 4. Action counters

```csharp
// Hero (all persisted): KillsByAnimation (Melee/Ranged/Magic/Quick/Heavy),
// UnarmedKills (attack id light-punch/heavy-strike), SpellsLearnedBeforeL5,
// InnocentKills (note 09), GoldEarned, ElementCasts (per MagicElement — affinity already tracks growth,
// this tracks counts)
```

Increment sites: `HandleEnemyDefeated` (kills, by the killing projectile's attack), trainer purchases, sell path. Cheap ints; Codex mirrors them for display.

### 5. Unlock rules as data — `Data/Classes/specializations.json`

```json
{ "id": "monk",        "name": "Monk",        "baseClasses": ["Priest","Wanderer"],
  "trigger": { "counter": "UnarmedKills", "threshold": 25 },
  "affinitySeed": {}, "statBias": {"Strength":2,"Wisdom":2},
  "loadout": ["light-punch","heavy-strike"], "description": "…" },
{ "id": "spellsword",  "trigger": { "all": [ {"counter":"KillsByAnimation.Melee","threshold":20},
                                             {"counter":"SpellsLearned","threshold":2} ] } },
{ "id": "elementalist","trigger": { "counter": "SpellsLearnedBeforeL5", "threshold": 2 } },
{ "id": "berserker",   "trigger": { "counter": "KillsByAnimation.Heavy", "threshold": 30 } },
{ "id": "thief",       "trigger": { "counter": "TheftCount", "threshold": 5 } },   // real thefts (note 16), not the old GoldEarned proxy
{ "id": "cultist",     "trigger": { "counter": "InnocentKills", "threshold": 1 } }
```

(Necromancer waits for the dark temple; triggers checked lazily once per second, not per tick.)

### 6. Class advancement flow

- Trigger fires → message log + Codex "Class discovered: Monk" + meta-store write (below).
- **Advancing** happens at the **trainer** (note 09): pick an unlocked specialization → `Hero.Class` swaps; **`Level` resets to 1, stats KEEP their values** (the owner's XP ruling anticipated exactly this: `TotalLevelsGained` never resets, so post-advance kills against old-tier enemies grant no underdog bonus — no reset-farming exploit); new class's starting loadout ids granted to inventory; affinity seed applied *additively at half strength* (specialization deepens, never rewrites, earned affinities).
- Level-gated evolutions (10/25/50/75 → Uncommon+) are the same table shape with `{"level": 10}` triggers — **data authoring only later, zero new code** if this schema is respected now.

### 7. Meta store — `Saves/meta.json`

```csharp
// SaveService-adjacent static MetaStore (CodexService pattern — load once, flush on change)
{ "unlockedClasses": ["monk"], "unlockedRaces": [], "seenSpells": ["ember", …] }
```

Unlocked specializations appear as **starting** classes in CharacterSelect (with a ★). Codex remains discovery-record; meta.json is *entitlements* — kept separate deliberately (Codex is per-install stats, meta is cross-run unlocks; both gitignored).

### TEST_CLASSTREE (PR 11 exit)

Punch 25 enemies dead → Monk unlock event + meta.json contains it; advancement at trainer resets Level to 1, keeps stats + `TotalLevelsGained`, grants loadout; underdog math post-advance uses lifetime levels (kill same-level-as-old-self enemy → ×1.0); new character sees Monk at creation; Cultist unlocks off one innocent kill (composes with TEST_TOWNCRIME). Full regression.

# 13 — Armor & Equipment Slots (PR 5c)

**Owner rulings 2026-08-05:** in this milestone; keep the **full slot roster already in the character design** (not a single-slot v1); sources are all three of loot + smithy crafting + merchant stock; enemies and guards wear armor, it affects their defense, and it drops as loot.

## Slots (roster finalized by owner ruling 2026-08-05)

Eight slots: **Head, Chest, Legs, Hands, Feet, RingLeft, RingRight, Amulet** — armor five plus a ring for each hand and an amulet, per the owner's spec. (The code has no equipment slots today — audit: defense derives from stats/level only — so this implements them fresh.)

```csharp
// Hero (and Enemy — shared shape):
public Dictionary<EquipSlot, Item?> Equipment;
public enum EquipSlot { Head, Chest, Legs, Hands, Feet, RingLeft, RingRight, Amulet }
```

Rings/amulet v1 are inert stat/affinity carriers (small bonuses, rarity-scaled, no weight, no noise). They're also the obvious future **enchantment targets** — ring + spell at a Forge is a combine the engine can already express (`CombineLocation` gating included); noting the hook, not building it.

- Armor pieces are `Item` Combinables (the `Armor` carrier rank in `CombinationEngine` already exists) with `Slot`, `DefenseBonus`, `WeightClass` (Heavy/Medium/Light — the Game Idea attribute set), and optional typed resists (`Attributes`: Fire, Ice, … reduce that element's damage, stacking with affinity resistance).
- **Weight matters** (Game Idea rules, finally live): Heavy +defense −move speed (a `TimeScale`-adjacent movement multiplier, NOT TimeScale itself — armor shouldn't slow your attack cooldowns v1) and louder steps (the note 11 `stepMult` term switches from weapon attributes to worn-armor weight, as reserved there). Light is quiet and fast, low defense. Per-piece, summed with caps.
- Defense integration: `EffectiveDefense = statDefense + Σ equipped DefenseBonus × rarityScale` — flows through the existing damage resolution untouched. Elemental resists apply at the same point affinity resistance does.
- Equip/unequip via the inventory screen (new Equipment column beside the hotbar column); combining armor stays Forge-gated (already enforced by `CombineLocation`).

## Sources

- **Loot**: `LootService` pool gains armor entries (floor-scaled rarity as today); enemy drops via corpse inventory.
- **Smithy**: recipe set beyond the one sword — leather set (Light), iron set (Medium→Heavy), per-piece recipes in `recipes.json` (ore/leather inputs; leather arrives with creatures — wolf/boar hides, note 14: the two PRs land adjacent deliberately).
- **Merchants**: armor rows in `merchant_stock.json`, Cha/Opinion pricing per note 09.

## Enemies and guards

- `EnemyFactory` assigns class-appropriate armor (Warrior heavy, Rogue light, casters cloth/none) scaled by level band; their `Defense` derivation switches from the flat formula to stats + worn pieces — **re-baseline `TEST_BALANCE`** deliberately (call out deltas).
- Guards visibly armored (render: outline/trim tint by weight class v1).
- Kill → pieces drop into corpse inventory through the existing loot path (this also closes the old "enemies' actual equipped gear as loot" deferral for armor).

## TEST_ARMOR

Equip/unequip round-trips save/load per slot; defense math exact (piece sum × rarity); typed resist reduces matching element only; Heavy slows movement and raises step-noise radius (compose with note 11 assert); enemy with armor takes measurably less damage than bare control at equal stats; corpse drops equipped pieces; forge-combine of two armor pieces respects location gating. Full regression + re-baselined TEST_BALANCE.

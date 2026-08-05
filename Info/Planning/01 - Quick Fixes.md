# 01 — Phase 0 Quick Fixes (PR 1)

Four small items, one PR. All are bug/gap closures found in the 2026-08-04 audit; 0.4 implements an owner ruling.

## 0.1 Level-unlock attacks silently wiped

**Defect:** `Hero.LevelUp` (Hero.cs:152-166) appends `power-strike` (L5), `quick-jab` (L10), `mana-bolt` (L15) directly to `Hero.Attacks`. But `GameState.RefreshAttacks()` (GameState.cs:1155-1160) rebuilds `Hero.Attacks` **entirely** from `Hero.Loadout`, and is called on every equip/unequip (GameState.cs:192, 203) and on `LoadFrom` (GameState.cs:1694). The unlocks have no backing `Combinable`, so the first equip action or save-load erases them.

**Fix:** unlocks grant real `Combinable`s (respecting the no-auto-equip rule → they land in `Hero.Inventory`, with a message-log line).

1. Add the three unlocks to `CombinableCatalog` as `Weapon`/`Spell` instances with their current stats (18/1.2/28 Heavy crit .12; 12/1.0/16 Quick crit .18; 22/2.0/32 Magic crit .10 — note `mana-bolt` should carry a small mana cost now that it's a real spell; propose 8).
2. `Hero.LevelUp` no longer touches `Attacks`. Instead it records `PendingUnlocks` (list of ids) — because `Hero` has no `GameState` access and the message log lives there.
3. `GameState` drains `PendingUnlocks` right after the XP-award path (`HandleEnemyDefeated`, GameState.cs:~1005): instantiate from catalog → `Hero.Inventory.Add` → `LogMessage(LevelUp, "Unlocked: Power Strike — find it in your inventory")`.
4. Delete the old direct-append block.

**Verify:** extend `TEST_STATS` — level a hero to 5, equip/unequip anything, save+load, assert `power-strike` still exists (in inventory or loadout) and appears exactly once.

## 0.2 Affinities not persisted

**Defect:** `SaveData.cs` has no affinity field; `Hero.Affinities` (grow-with-use, AffinityService.cs:135-154) resets to the class/race seed on every load.

**Fix:**
- `SaveData.Affinities : Dictionary<string, float>?` (string keys = `MagicElement` names, JSON-friendly, null for old saves).
- `SaveService.Save`: copy from `Hero.Affinities`.
- `GameState.LoadFrom` (GameState.cs:1672+): if null → keep the freshly-seeded values (old-save behavior is correct already); else overwrite wholesale.
- Enemy affinities stay unsaved (enemies are transient).

**Verify:** extend `TEST_SAVE` — cast fire spells until Fire affinity > seed, save, load into a fresh `GameState`, assert exact float match.

## 0.3 Duplicated overworld-arrival placement

`GameState.cs:1654-1655` and `1717-1718` both compute "hero stands one tile off the DungeonEntrance." Extract:

```csharp
private void PlaceHeroAtOverworldArrival()
{
    var entrance = CurrentMaze.Features.First(f => f.Type == MazeFeatureType.DungeonEntrance);
    Hero.X = entrance.X + 1;   // one tile off the entrance so the return-trip trigger doesn't refire
    Hero.Y = entrance.Y;
    Hero.TargetX = Hero.X; Hero.TargetY = Hero.Y;
}
```

Both call sites use it. When the town map lands (note 08), only this method changes — arrival becomes "the walkable tile adjacent to the entrance nearest the guard station," resolved by feature lookup, never coordinates.

## 0.4 XP rework (owner ruling 2026-08-04)

**Ruling:** Charisma XP multiplier is out. Bonus XP comes from exactly one source — fighting enemies stronger than yourself, measured against **lifetime levels gained**, because class advancement will later reset `Level` but not accumulated power. Intelligence's XP role is *skill mastery speed* and lands with spell leveling (note 10), not here.

```csharp
// Hero.cs
public int TotalLevelsGained { get; set; } = 0;   // never reset; class advancement resets Level, not this

public void GainExperience(int amount)            // Cha bonus deleted
{
    Experience += amount;
    while (Experience >= ExperienceToNext) LevelUp();   // LevelUp() does TotalLevelsGained++
}
```

```csharp
// GameState.HandleEnemyDefeated (GameState.cs:~1005) — bonus applied at the kill site,
// where the enemy is known; chest XP (flat 25) stays unmodified.
private float UnderdogMultiplier(Enemy enemy)
{
    float challenge = enemy.Level
        + enemy.Tier switch { EnemyTier.Elite => 1.5f, EnemyTier.Boss => 3f, _ => 0f };
    float margin = challenge - Hero.TotalLevelsGained;
    return 1f + Math.Clamp(margin * UnderdogBonusPerLevel, 0f, UnderdogBonusCap);
}
// Tunables (one constant block): UnderdogBonusPerLevel = 0.10f, UnderdogBonusCap = 1.0f
// i.e. +10%/level above your lifetime power, capped at +100%. Never a penalty — clamp floor is 0.
```

`xpGain = (10 + enemy.MaxHp / 4) * enemy.XpMultiplier * UnderdogMultiplier(enemy)` — the tier `XpMultiplier` (1.0/1.5/2.0) stays; it rewards the *kind* of enemy, the underdog term rewards the *matchup*.

**Persistence:** `TotalLevelsGained` added to `SaveData`; on load of an old save default it to `Level` (best available approximation).

**Verify:** extend `TEST_STATS`: (a) Cha 8 hero and Cha 1 hero get identical XP for identical kills; (b) a lifetime-level-2 hero killing a level-7 enemy gets ×1.5; (c) same-level kill gets ×1.0 exactly; (d) `TotalLevelsGained` round-trips and survives a (simulated) `Level` reset.

## Exit criteria

Full regression suite green; the four verifications above pass; no behavior change outside the four items.

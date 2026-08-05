# 12 — World Creation, Save Split, and Legacy (PR 6a — must land BEFORE PR 6)

**Owner rulings 2026-08-05:** (1) worlds are **generated** at world-creation time with player-chosen options; (2) **character death keeps the world** — and NPCs who knew the old hero remember them and may mention them in passing; (3) no resource-richness knob — resource abundance is **predetermined by region/environment**; (4) every world's starting region follows the [Starting Region.md](../Starting%20Region.md) *grammar* — same style, not the same town.

This PR is the foundation the world generator (note 08) writes into. It ships no generation itself — it restructures saves, adds the creation flow, and implements legacy records. **Retrofit cliff:** if PR 6 lands first, every world artifact has to migrate later; this goes first.

## 1. WorldGenOptions

```csharp
// Core/Models/WorldGenOptions.cs — serialized verbatim into world.json
public class WorldGenOptions
{
    public int Seed;                      // player-enterable; random default; SHOWN in UI (shareable-world culture)
    public WorldSize Size = WorldSize.Medium;        // Small | Medium | Large
    public Hostility Hostility = Hostility.Normal;   // Peaceful | Normal | Hostile  (owner ruling 2026-08-05)
    // Deliberately absent: resource richness (owner ruling — environment-determined, note 08 §regions).
    // Future knobs (magic prevalence, faction density, day length) = new fields + profile rows, no surgery.
}
```

**Size profiles** (one table in `Data/Config/worldgen.json`):

| | Small | Medium | Large |
|---|---|---|---|
| Region map | 72×48 | 96×64 | 128×84 |
| Population | ~60 | ~100 | ~150 |
| Forest depth / wilderness ring | thin | per spec | deep |
| POI variety | core set | + variants | + extra homes, second shrine, larger market |

v1 honesty (surfaced in the UI tooltip): Size scales the **region**, not the number of towns — multiple settlements need travel/multi-map infra (v2).

**Hostility profiles** — multiplier sets over tunables that already exist (the payoff of centralizing them):

| Knob (existing home) | Peaceful | Normal | Hostile |
|---|---|---|---|
| `enemyDensity` (note 03) | ×0.7 | ×1.0 | ×1.4 |
| `LevelRange` offset (EnemyFactory) | −1 | 0 | +1 |
| `EliteChance` | 0.10 | 0.18 | 0.28 |
| Trap count cap | −1 | — | +1 |
| Event frequency (offscreen events, when they land) | ×0.5 | ×1.0 | ×1.8 |
| Guard patrol coverage (note 09) | +1 guard | — | −1 guard |
| Wildlife aggression (forest fringe) | passive | mixed | hostile |

`GameSettings` gains a `WorldProfile` layer: base settings ⊕ hostility multipliers, resolved once at world load — game code keeps reading the same properties, unaware.

## 2. Save layout — world/character split

```
Saves/
  meta.json                    ← unchanged: cross-world entitlements (class/race unlocks)
  codex.json                   ← unchanged: per-install discovery/stats
  Worlds/
    {worldId}/
      world.json               ← WorldGenOptions + effective seed + FROZEN generation output
                                  (tiles RLE, rooms, features, npcs — note 08's format)
      delta.json               ← everything that has HAPPENED to this world (below)
      Characters/
        {saveId}.json          ← existing SaveData format + WorldId field
```

- `world.json` is written once at creation and never again (the per-world freeze).
- `delta.json` is the world's memory. v1 contents:

```csharp
public class WorldDelta
{
    public List<string> DeadNpcIds = new();              // moved here from SaveData (it was per-hero; it's world truth)
    public List<LegacyHero> FallenHeroes = new();        // §3
    public Dictionary<string, List<string>> NpcMemories = new();  // npcId → legacy refs (§3)
    // Reserved (documented now, populated by later PRs): tileDeltas (construction/destruction),
    // placedItems (world-item layer), reputation (per-NPC/faction), eventLog (offscreen sim).
}
```

- Migration: **none** (owner ruling 2026-08-05) — pre-split saves are playtests; delete them on first launch of the split build. Compatibility machinery must never be a factor in whether we make updates.

## 3. Legacy: the world remembers (owner ruling)

On character death, permadeath still deletes the **character file** — but first appends:

```csharp
public class LegacyHero
{
    public string Name, Race, Class;
    public int Level, TotalLevelsGained;
    public int DaysLived;                 // from WorldClock
    public string CauseOfDeath;           // "slain by a Goblin Mage on floor 7" — build from the killing blow
    public string DeathLocation;          // "the Dungeon, floor 7" / "the streets of {town}"
    public int Kills, InnocentKills, GoldEarned;   // the counters that shape how they're remembered
}
```

**NPC memory:** every meaningful interaction already has a choke point (talk/trade/train/heal — note 09's `E`-menu actions), and each feeds the per-NPC `Opinion` value (note 09, Cha-accelerated). On death, every NPC at **Acquaintance or above** gets the fallen hero's legacy ref in `NpcMemories`, tagged with their Opinion at the time — so a high-Charisma hero is mourned widely, a stranger is barely mentioned, and a Close friend brings you up unprompted. Bark tone and frequency scale with that stored Opinion (warm and often for friends; curt for acquaintances; "Don't say that name here" overrides everything if the record shows innocent blood).

**Memorial barks** (consumed by note 09's bark system — line added there): NPCs with a memory of a fallen hero occasionally swap a scheduled bark for a memorial one, flavored by the record — trained them: "Old {name} drilled right where you're standing. The dungeon got them in the end."; traded: "{name} used to sell me ore. Savage business, that maze."; `InnocentKills > 0`: "Don't say that name here." Data-driven templates in `Data/NPCs/barks.json` keyed by relationship kind + death cause + reputation tone. New characters walk into a town that talks about the people who came before — the legacy ruling made visible.

## 4. Flow changes

- **Title**: Start a New Game → **Worlds screen** (list existing worlds: name, size/hostility, days elapsed, living/fallen characters; Create New World → options form: seed field, size picker, hostility picker, world name) → **Characters screen** for the selected world (existing creation/continue, scoped to that world's folder).
- `SaveService`/`SavesWindow` re-point at `Worlds/{id}/Characters/`; `ListSaves` gains a world scope; delete-world = delete folder (two-click confirm, same idiom as save delete).
- `GameState` gains `WorldId` + loads `WorldDelta` on start; `HandleHeroDeath` writes the legacy record before deleting the slot.
- Dungeon dives are unaffected (per-dive procedural, seeded per dive as today) — hostility profile scales them via the settings layer.

## TEST_WORLD

Create world (fixed options) → folder structure exact; same seed+options twice → byte-identical `world.json`; two characters coexist in one world; character A dies → legacy record correct (cause/location/counters), character file gone, world intact; character B loads → A's dead NPCs stay dead, memorial bark lookup returns a line for an NPC A traded with and nothing for a stranger; hostility Hostile vs Peaceful → spawn-count assert on generated floors (profile actually applied); pre-split save files are removed cleanly on first launch. Full regression.

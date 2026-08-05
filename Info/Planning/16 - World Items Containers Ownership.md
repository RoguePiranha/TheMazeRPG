# 16 — World Items, Containers, and Ownership (PR 6b)

**Owner ruling 2026-08-05: in this milestone — "otherwise the world feels false."** Items exist in the world, not just in inventories; things belong to people; taking what isn't yours is theft. Consequences beyond witness-makes-guards-hostile wait for the reputation pass; the *substrate* lands now because shops, alchemy, looting homes, and construction all sit on it.

## Model

```csharp
// Core/Models/WorldItem.cs — an item lying in the world
public class WorldItem { public Combinable Item; public float X, Y; public string? OwnerId; }

// Container: a new MazeFeature payload rather than a new entity type — chests already are this shape
public class ContainerContents { public List<Combinable> Items = new(); public string? OwnerId; public bool IsShopShelf; }
```

- `Maze` (well, `GameState`) gains `List<WorldItem> GroundItems`; containers ride on `MazeFeature` (new types: `Shelf`, `Barrel`, `Crate`, `Cupboard` — placed by prefabs, note 08). The dungeon chest becomes the same mechanism with `OwnerId = null` (nobody owns dungeon loot — no behavior change, one code path).
- **Verbs**: Drop (from inventory screen → `WorldItem` at feet), Pick up (E / walk-over prompt), container Open → the existing two-panel loot window (corpse looting UI reused verbatim — it was built generically enough).
- **Ownership**: `OwnerId` = npc id, `"town"`, or null. Buildings own their containers (prefab data assigns the household/shopkeeper). Rendering: no visual difference — you *learn* what's owned by context, but the loot window labels rows "(owned)" so taking is always a knowing act.

## Theft v1

- Taking an owned item (ground or container) = a **theft event**: runs the note 11 witness check (any NPC/guard with LOS + awareness of you at that moment). Witnessed → the note 09 crime flow (alert, guards converge) + the item is flagged stolen. Unwitnessed → you got away with it (reputation memory comes later; the event is still written to the world delta so the future system can back-read it).
- Stolen-flagged items: merchants refuse to buy them (`"I know whose mark that is."`) — the fence is a future dark-economy hook.
- `Hero.TheftCount` counter (Thief specialization trigger upgrade: replace the GoldEarned proxy in note 10's `specializations.json` with actual thefts — cleaner unlock).

## Shops become real shelves

Merchant stock (note 09) moves from a menu-only list to `IsShopShelf` containers in the shop prefab: browsing a shelf shows the stock; **buying** = the transaction UI as before; **taking** = theft via the same path as any owned container. One system, no special case — this is the payoff of doing items-in-world before deepening commerce.

## Persistence

`WorldDelta` (note 12) populates its reserved slots: `placedItems` (WorldItems + container mutations vs. the frozen world.json baseline, stored as diffs), `stolenFlags`. Dungeon ground items are per-dive transient (not saved — consistent with dungeon-as-Trial). Save size guard: diffs only, never full container dumps.

## TEST_WORLDITEMS

Drop → persists in town across save/load, transient in dungeon; pick-up prompt + inventory arrival; container open/transfer via loot window; owned-item take with a witness → crime flow + stolen flag; without witness → no alert, delta records it; merchant refuses stolen goods; shelf buy vs. take paths diverge correctly; TheftCount increments; prefab-assigned ownership loads from world.json. Full regression.

# 03 — Dungeon Generator: Rooms-in-Maze Hybrid (PR 3)

**Owner ruling:** parts roomy, parts mazy. The Nystrom hybrid (rooms + maze flood + connectors) delivers exactly that and keeps the game's maze identity. Reference: Bob Nystrom, "Rooms and Mazes" (the algorithm behind Hauberk).

## Pipeline

`DungeonGenerator.Generate(width, height, floorNumber, seed)` — new class beside `MazeGenerator` (which survives as the corridor-flood step and for open-terrain floors' fallback). All dials from a `DungeonGenConfig` loaded via `GameSettings`.

```
1. RollDims(floorNumber)      → odd W,H scaled by depth
2. PlaceRooms()               → scattered non-overlapping rects
3. FloodCorridors()           → recursive-backtracker (iterative) in remaining space
4. ConnectRegions()           → connectors between regions; room connectors become doors
5. AddLoops()                 → open extra connectors (imperfection)
6. TrimDeadEnds()             → sparseness dial
7. AssertConnectivity()       → BFS over walkable+openable; regen on failure
```

### 1. Dimensions — fully random per floor (owner ruling 2026-08-05)

**No depth scaling.** "There's no reason the floors should get bigger as you go up. You could have one be 100×30 and one be 100×100 right next to each other and that would just be the way it is." Each axis rolls independently, so wild aspect ratios (long galleries, squat warrens, big squares) are a feature:

```csharp
// settings.json → "dungeonGen": { ... }
"minWidth": 31,  "maxWidth": 101,
"minHeight": 21, "maxHeight": 101,

W = OddBetween(cfg.minWidth,  cfg.maxWidth);    // rolled per floor, independent axes
H = OddBetween(cfg.minHeight, cfg.maxHeight);   // no floor-number term anywhere
```

Everything downstream already scales by *area*, not floor number (room attempts, enemy density, chest counts), so variable sizes cost nothing. `floorNumber` still feeds the dead-end **trim dial** (shallow roomier, deep mazier — that ruling stands; depth changes *texture*, never *size*). Honest playtest note: a 101×101 roll on floor 1 makes a long first dive — if that stings in practice, the fix is tightening the bounds (one knob), not reintroducing depth scaling.

### 2. Room placement

```csharp
"roomAttemptsPerKCells": 45,      // attempts = W*H/1000 * this
"roomMinSize": 3, "roomMaxSize": 9,   // odd sizes only, w×h independent; cap h at 7
"roomPadding": 1                       // min wall ring between rooms

for (int i = 0; i < attempts; i++)
{
    int rw = OddBetween(cfg.roomMinSize, cfg.roomMaxSize);
    int rh = OddBetween(cfg.roomMinSize, Math.Min(cfg.roomMaxSize, 7));
    int rx = OddBetween(1, W - rw - 1), ry = OddBetween(1, H - rh - 1);
    if (OverlapsAny(rx, ry, rw, rh, cfg.roomPadding)) continue;   // reject, don't shrink
    CarveRoom(rx, ry, rw, rh);   // Tiles=Floor, Rooms.Add(new Room{...}), region id++
}
```

Odd-aligned rects keep rooms on the same lattice as the maze so connectors are always 1 tile thick. Expected yield: ~8–12 rooms at 41×31, ~20–30 at 81×61.

### 3–5. Flood, connect, loop

- Flood: run the (now-iterative) backtracker from every odd unfilled cell; each flood = one region.
- Connectors: every wall cell with exactly two different regions on opposite sides is a candidate. Spanning-tree connect (union-find): pick random candidates until all regions merge. **A connector adjacent to a room becomes `DoorClosed`; corridor-to-corridor connectors become plain `Floor`** (halls meet halls openly; rooms have doors — this is the "better placement of doors and halls" rule in one sentence).
- Loops: after spanning, open each remaining candidate with probability `"extraConnectorChance": 0.15`. Rooms with 2+ doors and corridor loops both emerge here.

### 6. Dead-end trim — the roomy↔mazy dial

```csharp
"deadEndTrimPasses": depth-scaled — shallow floors trim more (roomier feel),
                     deep floors trim less (mazier feel):
passes = max(0, cfg.trimBase /*8*/ - floor / 2)
// each pass: every Floor cell with 3 wall cardinals → Wall (retract 1 step)
```

At `passes=8` most corridor whiskers vanish → floors read as rooms-and-halls. At `passes≈0` (floor 16+) the full labyrinth stands. One number, exactly the owner's "parts roomy, parts mazy" across the dive.

## Map-aware placement (rewrites `StartNewFloor`, GameState.cs:2225-2314)

Replace the single `emptyCells` pool with the `CellClass`/`Rooms`/BFS trio from note 02. Order matters — vault first (it locks a region), then stairs, then the rest.

- **Hero start:** keep (1,1) area; its room (or corridor pocket) is tagged `Entry`; exclusion = BFS distance < `"startExclusionSteps": 12` (replaces the 5×5 box — a *graph* moat, not a rectangle).
- **Vault** (owner ruling 2026-08-05: **exactly one per gate group, randomly placed** — when a group begins, roll `vaultFloor = groupStart + rng.Next(4)` so one of the 4 floors before each Guardian carries the vault; e.g. one of floors 1–4 for gate 5, one of 6–9 for gate 10): on the rolled floor, pick a room ≥ 3×3 with exactly 1 doorway (fall back: any 1-door room; none → re-roll onto another floor in the group). Its door → `DoorLocked`, `KeyId = "vault-key-{floor}"`. Type→`Vault`. Contents: one chest rolled at `floor + 2` (above-band loot). **Keyholder:** after enemy spawn, pick the enemy whose BFS distance from start is closest to the vault door's distance (a natural "guards the area" feel without pathing constraints), set `enemy.CarriesKeyId`; render a small gold key glint over it; key drops to its corpse inventory on death. `HandleEnemyDefeated` already routes corpse loot.
- **Stairs:** keep far-quartile BFS rule (GameState.cs:2266-2282 logic), but prefer candidates `InRoom` (fall back to any). Tag that room `StairRoom`.
- **Chests:** `1 + floorArea/600` (41×31→3, 81×61→9… cap at `"maxChests": 5`). Candidates: `InRoom` or `DeadEnd`, weighted 3× for dead-ends and 2× for rooms with 1 door (reward pockets). At least one chest must sit *off* the start→stairs shortest path (check: cell not on the BFS parent-chain) so exploration pays.
- **Traps:** count `1 + floor/4` (cap 4), chance per slot 70%. Candidates: `Corridor` within 2 tiles of a `Doorway`, or orthogonally adjacent to a chest (30% of chest sites). Never inside the start moat. Hidden flag as today.
- **Enemies:** count from **area**, not floor: `enemyCount = round(walkableCells * "enemyDensity": 0.018)` (41×31 ≈ 11 — today's floor-8-ish count arrives earlier; tune down if hot, but bigger floors *should* carry more). Distance banding: no spawns < `startExclusionSteps`; 60% of spawns in the far half by BFS distance. **Room packs:** while unassigned enemies ≥ 3 and an unused non-Entry/non-Vault room remains, place a pack of 2–3 in that room (same cell-adjacent tiles); remainder scatter on `Corridor`/`Junction` cells as wanderers.

## Guardian/safe-room interaction

Safe rooms remain hand-built (`GenerateSafeRoomMaze`, GameState.cs:1609) and the 5-floor cadence is orthogonal. **Changed by owner ruling 2026-08-04:** the Guardian door no longer spawns the boss into the safe room (today it does — `SpawnGuardian` places it on the door tile, GameState.cs:1573-1584, with the safe room's 3× regen still active during the fight). It now transitions to a **dedicated open themed arena** built from the Guardian's class/race/affinities — spec and flow in [04 - Open Terrain Floors.md](04%20-%20Open%20Terrain%20Floors.md) §Guardian arenas; it ships with PR 3b since it runs on the theme machinery.

## TEST_MAPGEN (the real one)

Generate 100 floors across depths 1–30 with fixed seeds; assert per floor:
1. Full connectivity: BFS over `IsWalkable ∪ openable doors` reaches every non-wall tile.
2. Stairs BFS distance ≥ far-quartile threshold; stairs reachable.
3. Every door tile has walkable cells on exactly 2 opposite sides.
4. Vault: exactly **one per simulated gate group** (never zero, never two); on its floor: exactly 1 locked door; interior unreachable with doors-closed BFS *excluding* locked; keyholder exists, is outside the vault, and is reachable.
5. Chest/trap/enemy counts within configured bounds; no entity inside the start moat; at least 1 chest off the critical path.
6. Room count within expected band for dims; gen time < 20 ms/floor.
7. Same-seed determinism: two runs produce identical tile arrays.

Plus `TEST_DUNGEON` extension: auto-play hero bumps a door open (tile flips, LOS extends), and a scripted vault run (kill keyholder → key in corpse → loot → open → chest).

## Renderer/perf note

Bigger floors magnify the fog memory (`_seenFloors` per maze) — it's per-cell bools, fine. The culling from note 02 is the actual guard; re-verify frame time on a max-roll 101×101 floor with ~35 enemies before closing the PR.

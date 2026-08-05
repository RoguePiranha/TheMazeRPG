# 02 — Tile / Door / Room Model (PR 2)

The enabler for everything spatial: dungeon rooms, doors, vaults, town buildings, terrain floors. No generator changes in this PR — the existing recursive-backtracker output is re-expressed in the new model bit-for-bit, so the whole regression suite must pass unchanged before PR 3 builds on it.

## Current state (audit)

`Maze` (Core/Models/Maze.cs, 88 lines) is `bool[,] Walls` + `bool[,] Explored` + `List<MazeFeature>` — no tile types, no rooms, no doors anywhere in the codebase (repo-grep verified). `IsWalkable` = bounds + `!Walls` (Maze.cs:53). LOS/fog/renderer all read `Walls` directly.

## 1. TileType

```csharp
// Core/Models/TileType.cs
public enum TileType : byte
{
    Wall = 0,        // default(byte)=Wall keeps "unset = solid" semantics from the carve loop
    Floor,
    DoorClosed,
    DoorOpen,
    DoorLocked,
    // Terrain (consumed by note 04 / note 08; harmless to define now)
    Grass, Road, WaterShallow, WaterDeep, Tree, Mud, Bridge, BuildingFloor,
}
```

`Maze` changes:

```csharp
public TileType[,] Tiles { get; }          // replaces bool[,] Walls
public List<Room> Rooms { get; } = new();

public bool IsWalkable(int x, int y) =>
    InBounds(x, y) && Tiles[x, y] switch
    {
        TileType.Wall or TileType.DoorClosed or TileType.DoorLocked
            or TileType.WaterDeep or TileType.Tree => false,
        _ => true,
    };

public bool BlocksSight(int x, int y) =>   // LOS authority — doors closed/locked block, open doesn't
    !InBounds(x, y) || Tiles[x, y] is TileType.Wall or TileType.DoorClosed
        or TileType.DoorLocked or TileType.Tree;
```

**Migration strategy:** keep a `Walls` compatibility property during the PR (`Walls[x,y] ≡ Tiles[x,y]==Wall`) only long enough to convert call sites, then delete it — grep says the readers are `MazeRenderer`, `MovementSystem`, LOS helpers in `GameState`/`CombatSystem`/`MovementSystem`, and `GetEmptyCells`. Every LOS site must switch from `Walls` to `BlocksSight`, every movement site to `IsWalkable`. `Mud` walkable-but-slow lands in note 04 (needs the TimeScale substrate); until then Mud is plain floor.

## 2. Rooms

```csharp
// Core/Models/Room.cs
public enum RoomType { Normal, Vault, Entry, StairRoom, Building /*town*/, Clearing /*open floors*/ }

public class Room
{
    public int Id;
    public RoomType Type;
    public int X, Y, Width, Height;              // interior rect, excludes surrounding wall ring
    public List<(int x, int y)> Doorways = new();
    public bool Contains(int px, int py) =>
        px >= X && px < X + Width && py >= Y && py < Y + Height;
}
```

Plus a cell classifier the placement code and NPC logic both want (computed on demand, cached per maze):

```csharp
public enum CellClass { InRoom, Corridor, Doorway, DeadEnd, Junction }
// DeadEnd  = walkable with exactly 1 walkable cardinal neighbor
// Junction = walkable with 3+ walkable cardinal neighbors, not in a room
// Doorway  = any Door* tile
```

## 3. Door behavior

- **Unlocked (`DoorClosed`)**: bump-to-open — in `MovementSystem`, when per-axis walkability rejects a move *because the blocking tile is `DoorClosed`*, flip it to `DoorOpen`, consume the move tick (the hero pauses one tick at the threshold — reads as "opening"), log nothing (too chatty). Doors never re-close in v1. ⚠ open decision #1 in the plan — bump-to-open is the recommended default; if the owner prefers `E`, the seam is this same rejection branch raising `NearbyInteractable` instead.
- **`DoorLocked`**: bump does nothing; `E` within `InteractRadius` with the matching key opens it (vault flow, note 03). Locked doors carry `KeyId` — add `Dictionary<(int,int), string> LockedDoorKeys` to `Maze` rather than widening the tile enum.
- **Enemies and doors:** enemies do *not* open doors in v1 — doors are player-paced safety valves (and pursuit already has the 3s window; a closed door legitimately breaks chase). NPCs (note 09) open unlocked doors freely via the same bump path.
- **Fog/LOS:** closed doors render in the wall pass and block `CheckLOS`; opening one immediately extends the visible set — no extra code if `BlocksSight` is the single authority, which is the point of §1.

## 4. Iterative carve

`MazeGenerator.CarveMaze` (MazeGenerator.cs:48) recurses per cell; even at the 101×101 max roll (~5,000 carve cells) recursion would likely survive, but convert now so floor size is never a constraint:

```csharp
private void CarveMazeIterative(Maze maze, int startX, int startY)
{
    var stack = new Stack<(int x, int y)>();
    maze.Tiles[startX, startY] = TileType.Floor;
    stack.Push((startX, startY));
    while (stack.Count > 0)
    {
        var (x, y) = stack.Peek();
        var dirs = ShuffledDirections();               // same 4 dirs, same Fisher-Yates
        bool carved = false;
        foreach (var (dx, dy) in dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (IsInBounds(maze, nx, ny) && maze.Tiles[nx, ny] == TileType.Wall)
            {
                maze.Tiles[x + dx / 2, y + dy / 2] = TileType.Floor;
                maze.Tiles[nx, ny] = TileType.Floor;
                stack.Push((nx, ny));
                carved = true;
                break;
            }
        }
        if (!carved) stack.Pop();
    }
}
```

Note: iterative-with-Peek visits neighbors in the same order as the recursion, but the *sequence of RNG draws differs* from the recursive version (recursion shuffles all 4 dirs once per call; this shuffles on every Peek revisit). To keep identical mazes for a given seed, shuffle once per cell and store the remaining dirs on the stack entry: `Stack<(int x, int y, List<(int,int)> dirs, int next)>`. Do that — regression identity per seed is worth the few lines, and `TEST_MAPGEN` can then assert old-vs-new equivalence directly.

## 5. Renderer

- `DrawMaze` (MazeRenderer.cs:536) switches on `TileType` instead of the wall bool. Doors v1: wall-colored rect with a centered vertical gap + brown `#8B5A2B` slab (closed), rotated open slab against the jamb (open), plus a small gold keyhole dot (locked). Terrain tiles get flat colors for now (Grass `#1a2e1a`, Road `#3a3530`, Water `#16324f`, Tree = dark trunk dot on grass, Mud `#2e2417`) — real art later.
- **Culling:** verify the tile loop clamps to the camera viewport. With `CellSize = 64` and a 900×600 window the visible set is ~15×10 tiles; iterating a 101×101 max-roll floor per frame is ~68× waste. Clamp: `startX = max(0, floor(cameraLeft/CellSize)) … endX = min(Width-1, ceil(cameraRight/CellSize))`, same for Y, applied to the tile pass **and** `DrawFeatures`. Entities/projectiles are few; leave them unculled.

## 6. Save/serialization

None needed — mazes are rebuilt, never saved (`SaveData` stores only `ResumePoint`; audit-confirmed). Door state is per-dive and intentionally transient. The frozen town (note 08) serializes `Tiles` itself, but that's its own loader, not `SaveData`.

## Verification

- `TEST_MAPGEN` v0 (this PR): same-seed maze identical to pre-refactor output (walls ↔ Wall/Floor bijection); a hand-built room-with-door fixture asserts: closed blocks LOS + walk, bump opens, open blocks neither, locked resists bump and opens by key API.
- Full regression suite (the point of the PR is zero behavior change).
- GUI smoke: fog and minimally-styled doors render; frame time unchanged on floor 1 (culling check: also generate one 81×61 floor via debug console `moveplayer dungeon` + a temporary size override).

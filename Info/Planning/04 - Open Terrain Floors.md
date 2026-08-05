# 04 — Open Terrain Floors (PR 3b)

**Owner addition 2026-08-04:** some floors feel like being *outside* — walls only at the border, one huge space, terrain from the floor's theme (forest / swamp / grassland / enemy fortress). This is the long-parked `PerlinNoise.cs` finally getting its consumer.

## Archetype selection

**Owner ruling 2026-08-05 — fully random, two-stage roll, no set pattern.** Roll the floor *type* first (open floors rarer than regular corridor floors), then roll the biome/theme independently. Explicitly **no pity timer and no guaranteed placement** — a gate group may contain zero open floors or several; that's the point.

```csharp
// settings.json → "dungeonGen"
"openFloorChance": 0.20,        // stage 1: type roll per floor, floors 4+ — open stays the rarity
"openFloorMinFloor": 4,
"openThemes": ["Forest", "Swamp", "Grassland"],   // stage 2: theme roll (uniform v1); Fortress added after PR 6

// StartNewFloor:
FloorArchetype archetype =
    CurrentFloor >= cfg.openFloorMinFloor && _random.NextDouble() < cfg.openFloorChance   // stage 1: type
        ? FloorArchetype.OpenTerrain(cfg.openThemes[_random.Next(cfg.openThemes.Count)])  // stage 2: biome
        : FloorArchetype.RoomsAndMaze;
```

(Once note 14 lands, the rolled theme also biases elemental creature variants — Scorched rolls Cinder-heavy fauna.)

`Maze` gains `public string? Theme` — carried for renderer tinting now, and later for Game Idea.md's "chunk environment affects spell power" + Magic doc §13 environmental essence (hooks only; no behavior this PR beyond tile effects below).

## The fBm wrapper PerlinNoise needs

`PerlinNoise.Noise(x,y)` (Core/Services/PerlinNoise.cs:29) is single-octave, range ≈ [-1,1]. Terrain wants fractal Brownian motion — add alongside (don't touch `Noise`):

```csharp
/// Sum of octaves; returns ~[-1,1]. frequency in cells⁻¹.
public double Fbm(double x, double y, double frequency, int octaves,
                  double lacunarity = 2.0, double gain = 0.5)
{
    double sum = 0, amp = 1, norm = 0, f = frequency;
    for (int i = 0; i < octaves; i++)
    {
        sum  += amp * Noise(x * f, y * f);
        norm += amp;
        amp  *= gain;
        f    *= lacunarity;
    }
    return sum / norm;
}
```

## OpenTerrainGenerator

```csharp
public static Maze Generate(int width, int height, int floorNumber, string theme, int seed)
{
    var maze = new Maze(width, height, floorNumber) { Theme = theme };
    var noise  = new PerlinNoise(seed);
    var noise2 = new PerlinNoise(seed ^ 0x5f3759df);   // independent channel (moisture/detail)
    var t = ThemeParams[theme];

    for (int x = 0; x < width; x++)
    for (int y = 0; y < height; y++)
    {
        if (IsBorder(x, y)) { maze.Tiles[x, y] = TileType.Wall; continue; }
        double e = noise.Fbm(x, y, t.Frequency, t.Octaves);          // "elevation"
        double m = noise2.Fbm(x, y, t.Frequency * 0.6, 2);            // "moisture"/detail
        maze.Tiles[x, y] = t.Classify(e, m);
    }
    EnsureTraversable(maze);
    return maze;
}
```

### Theme parameters (starting values — all in one `ThemeParams` table)

| Theme | Frequency | Octaves | Classify(e, m) | Feel |
|---|---|---|---|---|
| **Forest** | 0.10 | 3 | `e > 0.28 → Tree`; `e < -0.45 → WaterShallow` (ponds, ~5%); else `Grass` | ~30% tree cover in clumps with winding sightlines; clearings where e dips |
| **Swamp** | 0.07 | 4 | `e < -0.15 → WaterShallow`; `-0.15..0.05 → Mud`; `e > 0.45 → Tree` (hummocks); else `Grass` | ~35% water in broad pools, mud shores, sparse trees; movement-hostile |
| **Grassland** | 0.13 | 2 | `e > 0.55 → Tree` (lone copses, ~6%); `m < -0.55 → WaterShallow` (a creek's worth); else `Grass` | Wide open, long sightlines, near-zero cover — the ranged/kiting floor |
| **Fortress** *(post-town PR)* | 0.13/2 grassland base | — | Grassland pass, then stamp a walled compound (rooms/doors machinery from PR 2/6) centered at the far BFS quartile, gate facing spawn | Field approach → breach → interior fight |

Frequency intuition at 64px tiles: 0.10 → features every ~10 cells (~1.5 screens); lower = broader blobs, higher = choppier. Octaves add edge detail; Grassland stays at 2 so it reads clean and open.

Tile effects (with note 02/05 in place):
- `Tree`: blocks walk + LOS (the forest's "walls").
- `WaterShallow`: walkable, applies `TimeScale × 0.7` while standing in it (Slow substrate from note 05; until PR 4 lands, water is cosmetic).
- `Mud`: walkable, `TimeScale × 0.85`.
- `WaterDeep`: unwalkable; only if a theme wants hard moats later.

### EnsureTraversable — connectivity guarantee

Noise can strand pockets behind tree/water masses:

1. BFS from hero start over walkable tiles → mark reached.
2. For each unreached walkable pocket (largest first): A*/greedy walk from pocket centroid toward the reached set, converting blocking `Tree→Grass` / `WaterDeep→WaterShallow` along a 1-wide line (a "game trail" / ford — reads naturally).
3. Re-BFS; loop until one component (bounded: each pass strictly grows the component).
4. If total walkable < 55% of interior area, regenerate with `seed+1` (theme rolled too dense; cheap, rare).

## Placement on open floors

Rooms don't exist here, so placement keys off terrain:

- **Clearings** (for `Rooms` consumers): flood-fill maximal open patches ≥ 4×4 → synthesize `Room { Type = Clearing }` entries so the *same* pack-spawn code from note 03 works — enemy packs hold clearings, wanderers roam between.
- **Stairs:** far BFS quartile, prefer a clearing; on Fortress floors the stairs live *inside* the compound (the fortress is the objective).
- **Chests:** clearings + pond/hummock edges (`Grass` adjacent to ≥2 nonwalkable), same counts as note 03.
- **Traps:** only on `Grass` adjacent to Trees ("snares"), halved count — the terrain itself is the hazard.
- **Enemy density** ×1.15 vs. corridor floors (open space is easier to disengage in), and **sightline aggro matters**: with `VisionRange 7.5` unchanged, grassland floors are genuinely more dangerous — that's the intended combat-character difference, verify feel in playtest.

## Renderer

Flat tile colors from note 02 + per-theme ambient tint on the floor pass (Forest `#0d150d`, Swamp `#12140d`, Grassland `#141507` at low alpha). Trees draw trunk-dot + canopy circle at 70% alpha *over* the tile so entities behind read through slightly. Fog rules unchanged (these are still dungeon floors — fully fogged, unlike town).

## Guardian arenas (owner ruling 2026-08-04): the door leads somewhere

**Ruling:** the safe-room Guardian door no longer spawns the boss into the safe room itself — it transitions to a **dedicated open themed arena, themed to that specific Guardian** (class, race, skills/affinities). This also fixes two audit findings: the 3× safe-room regen buff currently stays active *during* the Guardian fight (`IsInSafeRoom` doesn't flip until victory, GameState.cs:563-568), and the shrine remains touchable mid-fight.

### Theme selection — `ArenaThemeSelector.For(guardian)`

Precedence (one data table, `Data/World/arena_themes.json`):
1. **Dominant affinity** ≥ the elementalist threshold (45, `AffinityService`): the element picks the arena — Fire→Scorched Waste (ash Grass tint, burning-tree props, ember vents), Ice→Frozen Fen (frost tint, iced ponds), Poison→Blighted Swamp (swamp params, toxin pools), Lightning→Storm Plateau, Water→Flooded Field, Earth→Boulder Field, Shadow/Death→Ashen Graveyard, Light→Radiant Clearing, Arcane/Mana→Crystal Glade, Sonic→Resonant Canyon. **v1 scope:** these are the three built theme generators (Forest/Swamp/Grassland) re-parameterized + a palette/prop swap per element — not ten bespoke generators. The table maps element → {baseTheme, tint, propSet, densityMods}.
2. **Class archetype** otherwise: Warrior→Colosseum Ring (grassland + pillar ring), Archer/Ranger→Forest (a cover fight), Rogue→Dense Forest (low sightlines), Priest→Radiant Clearing, Bard→Resonant Canyon, Wanderer→open Grassland, Mage→falls through to rule 1 in practice.
3. **Race modifier**: prop/tint layer — Dwarf→stone rubble, Elf→denser trees, Orc→war-camp stakes, Goblin/Kobold→burrow holes, Dragonborn→scorch marks, Tiefling→infernal tint.
4. **Skills shape the ground**: cover density scales with the Guardian's kit — primary attack range ≥ 3 → sparser cover (an artillery Guardian wants open ground; the player has to close under fire); melee Guardian → denser cover (ambush pressure). `coverDensityMult = clamp(1.6 − 0.2 × primaryRange, 0.6, 1.4)`, applied to the theme's tree/prop thresholds. Home-field advantage is the *point* — the arena belongs to the Guardian.

### Flow changes (GameState)

- **Size: deliberately small — a set-piece, not a floor (owner emphasis 2026-08-04).** Default **25×17** (~425 tiles ≈ ⅓ the area of the *smallest* regular floor; regular floors run 41×31 → 81×61), hard cap 31×21; `"arenaWidth"/"arenaHeight"` tunables, never depth-scaled. Fully lit, no fog, and the Guardian spawns in the far clearing already in view from the gate with its warning bark on entry — there is **no finding the boss**, only closing with it. At 64px cells this is ~2 screens wide, ~2 tall: enough ground for the terrain/cover game to matter, nothing to explore.
- `CheckFeatures`' GuardianDoor branch (GameState.cs:1371) → `EnterGuardianArena()` instead of in-place `SpawnGuardian`: `IsInSafeRoom = false` (**regen buff ends at the threshold**), `IsInArena = true` (same mode-flag pattern as `IsInSafeRoom`), `CurrentFloor++` (unchanged math — the fight IS floor 5/10/15), generate the arena (border walls), hero at the west-center **gate** feature, Guardian spawned in the far-east clearing.
- **Retreat is allowed** (standing rule: leaving is never gated): touching the entry gate returns to the safe room — `CurrentFloor--`, safe room restored, and the **Guardian resets to full HP** (anti-door-dance: the safe room's 3× regen would otherwise make fight-retreat-heal-repeat the dominant strategy). Reset-vs-persist flagged as an owner tunable.
- **Victory beat**: `_pendingGuardianVictory` no longer instant-`StartNewFloor()` — it spawns an `ArenaExit` feature (portal glow) at the arena center. The player loots the Guardian's corpse (right-click loot already exists; instant transition today can strand corpse loot), then touches the exit → `StartNewFloor()` → floor 6. Auto-mode walks to the exit after auto-looting (existing `AutoLootNearbyCorpses` covers the corpse).
- `CanSave` stays false in the arena (it already requires "safe room with no Guardian engaged"; add `!IsInArena`).

### TEST_ARENA

Forced-seed Guardians: high-Fire Mage → Scorched Waste params; Elf Archer (no dominant affinity) → Forest with sparse-cover mult; Dwarf Warrior → Colosseum with rubble props. Assert: arena dims ≤ 31×21; Guardian has LOS-unobstructed visibility from the gate tile (never hidden behind a cover mass — reroll prop pass if violated); regen multiplier drops to 1× on arena entry; `CurrentFloor` 4→5 on entry, 5→4 on retreat, victory+exit → 6; retreat resets Guardian HP; shrine unreachable from arena; `ArenaExit` spawns only after death; corpse lootable before exit; `CanSave` false inside. Full regression (`TEST_DUNGEON`'s gate assertions updated to the new flow).

## TEST_TERRAIN

Fixed seeds × 3 themes × 20 floors: assert single walkable component ≥ 55% interior; per-theme tile-share bands (Forest tree 20–40%, Swamp water 25–45%, Grassland open ≥ 85%); stairs/chest/enemy placement constraints hold; determinism per seed; gen+repair time < 25 ms. Plus a scripted swamp walk asserting the water/mud TimeScale once PR 4 lands.

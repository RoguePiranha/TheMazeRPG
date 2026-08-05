# 08 — World & Town Generation: grammar + prefabs, frozen per world (PR 6)

**Reframed 2026-08-05 (owner rulings).** Generation no longer runs at dev time with one output shipped as fixed content — it runs **once at world creation** (seeded by [note 12](12%20-%20World%20Creation%20and%20Legacy.md)'s `WorldGenOptions`), and *that world's* output is frozen into `Saves/Worlds/{id}/world.json`, permanent for the life of the world. [Starting Region.md](../Starting%20Region.md) is the **grammar** every generated starting region satisfies — same style, never the same town. Resource richness is **not** a player knob: it derives from the region's environment (mountain band → iron-rich mine; forest depth → wood; river → reeds/fish reagents later), authored in the region template.

## Architecture

```
WorldGenOptions (note 12)  →  RegionGenerator.Generate(options)  →  validated TownMap DTO
                                     ↓ freeze once
                    Saves/Worlds/{id}/world.json  ←  WorldMapLoader (runtime, read-only)
```

Validation is now **load-bearing** — there is no hand-fix pass on a player's generated world. Failed validation regenerates with `seed+1` (bounded, ~10 tries; effective seed recorded in `world.json`). The dev-time ASCII preview tool survives as `TEST_TOWNGEN=1 SEED=n` for tuning the generator itself.

## The grammar (constraints every starting region satisfies — from Starting Region.md)

Dungeon entrance set into a mountain side with a guard station adjacent; walls around the town, ≥2 gates; mine outside the walls near a gate, further down the mountainside; river on the opposite side from the mountain, water-tower intake **upstream** of the sewage outfall; ~100-meter cleared band outside the walls; forest fringe beyond; town square with market stalls at the road crossing; smithy/alchemist/carpenter, temple, training school; one home with the hidden trapdoor; sewer grate near the river. **What varies per seed:** which edge the mountain occupies, river course, gate positions, road topology, district arrangement, lot placement, building variants, NPC roster. The spec is the sentence; the seed picks the wording.

## Prefabs: hand-authored quality inside generated variety (the CDDA mapgen move)

`Data/World/Prefabs/*.json` — small hand-authored stamps: tile grid (RLE, same alphabet as world.json), door positions, feature anchors, furniture/feature objects, NPC work-anchor tiles.

- Core set: smithy, temple, training school, guard station, alchemist, carpenter, water tower, market stall, **6–8 home variants** (1–4 household sizes, one variant carrying the trapdoor flag), tavern-shaped placeholder.
- Generator places **prefabs on lots** (rotations 0/90/180/270), rather than carving rooms ad hoc — the polish lives in the prefab files, which stay hand-editable forever, and every world gets it.
- Prefab rooms register as `Room { Type = Building }` with their doorways, so interiors-dark-until-entered, NPC anchors, and the cell classifier all work unchanged.

## Pipeline (RegionGenerator — stages unchanged from the original note, now parameterized)

```
1. TerrainBase()   Grass field at Size-profile dims (72×48 / 96×64 / 128×84); mountain band on a
                   seed-chosen edge (Wall rows, 3-6 deep)
2. River()         opposite side: 3-wide WaterShallow, Fbm-jittered course (freq 0.05, oct 2); 1 Bridge;
                   intake/outfall ordering asserted against flow direction
3. WallRing()      inset ring butted to the mountain; 2-3 gates (3-wide Road gaps), seed-placed
4. Roads()         gate↔gate spine through a 9×9 square plaza; spurs to dungeon entrance + bridge
5. Districts()     crafting near a gate, market at the square, temple+school district, homes fill —
                   flood assignment by distance to seed-chosen anchors
6. Lots()          road-frontage lots; PREFAB placement (variant + rotation) instead of ad-hoc carving
7. POIs()          feature anchors from prefabs; mine outside a gate on the mountain side
8. Outside()       cleared band; forest fringe (note 04 Forest params, density fading inward, depth
                   from Size profile); wildlife spawn zones tagged (hostility profile picks temperament)
9. Population()    households {1:20%,2:40%,3:25%,4:15%} to the Size-profile count; occupations from
                   the POI roster; race weights Human-heavy; syllable-table names; seeded → same world,
                   same people, forever
10. Validate()     grammar asserts (below) → freeze
```

**Validation (regen on any failure):** every prefab door road-reachable; every home↔workplace path exists; single walkable component; river breaches the wall only at the outfall notch; intake strictly upstream; mine outside walls; exactly one trapdoor home; square holds ≥4 stalls; population/occupation counts match profile.

## POI roster

As in the original note: DungeonEntrance+guard station, MineEntrance (outside), Smithy, 4–6 Stalls, Alchemist, Carpenter, Temple, TrainingSchool, water tower, SewerGrate, trapdoor home, Gate features. New `MazeFeatureType` members each get an explicit `MazeRenderer` case (the no-default-switch lesson).

## Runtime integration

- `EnterOverworld()` / resume: `WorldMapLoader.Load(worldId)` (cached); hero arrival via the note 01 §0.3 helper ("arrival tile adjacent to DungeonEntrance").
- Interiors same-map, dark-until-entered per-room `Discovered` flag (owner ruling, unchanged).
- Press-E flows keyed by feature type; `TEST_OVERWORLD` resolves POIs by feature lookup — **required** now, coordinates differ per world.
- `DeadNpcIds`/legacy from `WorldDelta` (note 12) applied on load: dead NPCs never spawn; memorial barks active.
- Perf: Large is 128×84 ≈ 10.7k tiles — note 02 culling mandatory; NPC update cost capped by the every-other-tick cadence (note 09).

## Verification

`TEST_TOWNGEN`: 50 worlds across sizes × hostilities × seeds — all validations pass (or regen within bounds), determinism (same options → identical world.json), grammar asserts per world, gen time < 150 ms; ASCII previews spot-checked. `TEST_OVERWORLD` green on three different generated worlds (the loop is layout-independent). `TEST_WORLD` (note 12) covers the save-side. GUI smoke on Small and Large. Owner eyeball: generate a handful of worlds and walk them — the *generator* gets approved now, not one map.

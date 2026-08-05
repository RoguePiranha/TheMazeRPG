# Functional State Plan — Overworld, Dungeon Gen 2.0, Combat Identity, NPCs, Progression

**Authored:** 2026-08-04
**Companion to:** [Implementation Plan.md](Planning/Implementation%20Plan.md) (the running log / source of truth), [Starting Region.md](Starting%20Region.md), [Magic_System_Reference.md](Magic_System_Reference.md), [Class Tree Inventory.md](Class%20Tree%20Inventory.md)
**Per-system development notes with code sketches:** see the [Planning/](Planning/README.md) folder — one note per PR row in §9.
**Scope:** the owner's 2026-08-04 ask — reach a functional state with (1) a real overworld, (2) larger dungeon floors with variation, rooms/doors/halls, and better object placement, (3) continued player-progression build-out, (4) genuinely differentiated spells/attacks, (5) peaceful-NPC goal systems.

This plan is grounded in a fresh full audit of the codebase (generation, combat/progression, and the old Godot prototype in `..\Sample Files`) — findings are cited inline.

---

## 1. Where the code actually is (audit summary)

**Dungeon generation** — `MazeGenerator.cs` is a recursive-backtracker perfect maze: every floor is a fixed 41×31 grid of 1-tile-wide corridors with zero rooms, zero doors, zero loops. "Better placement of doors and halls" is really *building doors and halls for the first time* — a repo-wide grep confirms no room/door/corridor concept exists anywhere. The `Maze` model is a bare `bool[,] Walls` with no tile types. Placement today: 1 stairs (top-quartile BFS distance — the one smart placement), 1 chest (uniform random), ≤1 trap (40% chance), `3 + floor` enemies (uniform random, no distance banding). `floorNumber` is threaded into `Generate()` but never read — a free parameter waiting for depth-varying generation.

**Overworld** — `OverworldGenerator.cs` is a 31×15 empty bordered field with 4 hardcoded features (DungeonEntrance, MineEntrance, Smithy, Stall). The mine→craft→sell loop works via Press-E. Everything in [Starting Region.md](Starting%20Region.md) (walls, gates, river, ~100 residents, temple, training school, town square, guard station, hidden dark temple) is unbuilt.

**Spells/attacks** — 14 class attacks + 6 catalog items, differentiated almost entirely by numbers. Only behavioral axes: 5 `AttackAnimation` projectile-physics archetypes, one special-cased AoE (`arcane-blast`), crit, and 3-way stat scaling. **No status effects exist at all** (grep-verified). No pierce, chain, homing, DoT, beam, cone, multi-shot. `KnockbackDistance` and `ParryChance` are authored on 4 attacks but **never read by any system** — dead fields.

**Progression** — quadratic XP ✓, 5 manual stat points/level ✓, elemental affinities grow with use ✓. Missing: spell/ability leveling (zero code), class evolution (zero code), ability passives (`Ability.Modifiers` is read only by a tooltip — Dense Musculature and Mana Circuitry do literally nothing), rarity is cosmetic for power (only affects sell price + UI color).

**NPCs** — none. `Enemy` is the only non-hero actor; enemy AI has exactly 2 states (wander/pursue). The town's "structures" are feature tiles, not people. Class Tree unlock triggers ("Kill an innocent NPC", "Defend a village") are unimplementable until NPCs exist.

**Bugs found during the audit** (see §2):
1. **Level-unlock attacks are silently wiped.** `Hero.LevelUp` appends power-strike/quick-jab/mana-bolt (L5/10/15) directly to `Hero.Attacks`, but `GameState.RefreshAttacks()` rebuilds `Hero.Attacks` entirely from `Hero.Loadout` on every equip/unequip/load. The unlocks have no backing Combinable → gone on first equip action or save-load.
2. **Affinities are not persisted.** `SaveData` has no affinity field; all grow-with-use elemental progress is lost on save/load.
3. **Duplicated overworld-arrival placement** (`GameState.cs` ~1654 and ~1717) — same hero-positioning logic in two places, will drift when the town map changes.
4. *Design question, not a bug:* `GainExperience` applies the Charisma bonus as `amount × (1 + Cha × 0.05)` — at Cha 8 every kill is worth 1.4×. Confirm that's intended before balancing on top of it.

**What the Godot prototype (`..\Sample Files`) is actually worth**: the context-steering enemy AI in `test_enemy.gd` (16-direction weighted steering, patrol/chase/strafe/aggro-memory — genuinely good, port the *algorithm* for NPC/guard local movement), the roof-fade/interior-reveal pattern (`tranform_area.gd`) as a design reference for building interiors, and the spell-tier naming pattern in `game_data.gd` (bolt→ball, minor/base/greater) which corroborates the Magic doc's §9 tiers. Its spell/combat/NPC code was never functional (three independent bugs prove the spell path never ran once) — nothing to port there.

---

## 2. Phase 0 — Quick fixes (S, do first)

| # | Fix | Where |
|---|---|---|
| 0.1 | Level-unlock attacks: grant them as real Combinables into `Hero.Inventory` (message-log the unlock) instead of raw `Attacks` appends, so `RefreshAttacks` can't wipe them. Respect the no-auto-equip rule. | `Hero.LevelUp`, `AttackFactory` |
| 0.2 | Persist `Hero.Affinities` in `SaveData`/`LoadFrom` (default: reseed from class/race for old saves). | `SaveData.cs`, `SaveService.cs`, `GameState.LoadFrom` |
| 0.3 | Extract the duplicated overworld-arrival placement into one helper (it's about to change anyway in Phase 2). | `GameState.cs` |
| 0.4 | **XP rework (owner ruling 2026-08-04): remove the Charisma XP multiplier entirely.** Bonus XP comes from exactly one place — fighting enemies stronger than yourself. Add `Hero.TotalLevelsGained` (increments on every level-up, **never resets** — class advancement will later reset `Level` but not this) and persist it in `SaveData`. Underdog bonus: `xp × (1 + clamp((enemyChallenge − TotalLevelsGained) × 0.10, 0, 1.0))` where `enemyChallenge = enemy.Level` plus a small Elite/Boss bump — i.e. up to +100% vs. something 10 levels above your lifetime power, and never a penalty (baseline stays 1×). Coefficients tunable in one spot. Intelligence's XP role lands in Phase 5.1 (skill-mastery speed), not here. | `Hero.GainExperience`, `Hero.LevelUp`, `GameState.HandleEnemyDefeated`, `SaveData` |

Exit: regression suite green; a save/load round-trip preserves affinities and unlocked attacks.

---

## 3. Phase 1 — Dungeon Generation 2.0 (L)

*Goal: bigger floors that vary by depth, with rooms, doors, and halls, and placement that understands the map.*

### 1.1 Tile & room model (the enabler)
- Replace `bool[,] Walls` with `TileType[,]` — `Wall, Floor, DoorClosed, DoorOpen, DoorLocked` now; leave room for `Water/Rubble` later. Keep `IsWalkable` as the single walkability authority (closed doors: walkable=false, but *openable*; open doors walkable).
- Doors interact with LOS/fog: closed blocks LOS, open doesn't. Bump-to-open for unlocked doors (no extra input); locked doors need a key + `E`.
- `Maze.Rooms : List<Room>` (`Id, Rect, RoomType`) and a per-cell classification helper (InRoom / Corridor / Doorway / DeadEnd / Junction) — placement and NPC logic both want this.
- Update `MazeRenderer` (door visuals), `MovementSystem`, LOS helpers. **Convert `CarveMaze` to an explicit-stack iterative carve** — recursion depth is bounded by cell count and floors are about to grow.
- **Renderer culling check**: verify the tile loop clamps to the camera viewport before floors get big; add clamping if it iterates the whole grid.

### 1.2 Generator: rooms embedded in a maze (keeps The Maze's identity)
**Owner ruling 2026-08-04: confirmed — parts roomy, parts mazy**, via the hybrid below with a depth/area-varying dial.

Nystrom-style hybrid (rooms + maze + connectors), which is also what SimpleRPG's BSP rooms pointed at:
1. Scatter non-overlapping rooms (sizes ~3×3 to 9×7; count scales with floor area).
2. Flood remaining space with recursive-backtracker corridors (the existing algorithm, on the residual grid).
3. Connect every region via connectors; each room-to-corridor connector becomes a **door**; open ~10–20% extra connectors so floors have loops (no more single-path-everywhere).
4. Optionally trim a fraction of corridor dead-ends (sparseness dial) — deeper floors can be denser/mazier, shallower floors roomier.
- **Floor sizes are fully random, never depth-scaled (owner ruling 2026-08-05)**: each axis rolls independently per floor (bounds ~31–101 × 21–101, tunable) — a 100×30 gallery next to a 100×100 sprawl "is just the way it is." `floorNumber` feeds only the roomy↔mazy trim dial, never size.
- Guarantee: BFS full-connectivity assert after generation (regenerate on failure — should be structurally impossible, but cheap to check).

### 1.2b Floor archetypes: themed open-air floors (owner addition 2026-08-04)
Some floors *feel like being outside* — walls only at the border, one huge open space with terrain driven by a floor theme:
- Generation picks a **floor archetype** per floor: `RoomsAndMaze` (default) or `OpenTerrain(theme)` — themes v1: **Forest** (tree obstacles, clearings), **Swamp** (water patches that slow, mud), **Grassland** (sparse cover, long sightlines), **Fortress** (open ground around a walled structure with rooms/doors — reuses the 1.1/1.2 room machinery as a "building on a field").
- Terrain from noise: **`PerlinNoise.cs` finally gets its consumer** — threshold noise into tree/water/open masks, with a connectivity pass (BFS) to guarantee traversability. This is exactly what the tile-model extensibility in 1.1 (`Water`, obstacle tiles) is for.
- Combat character differs by design: long sightlines and no corridors make these ranged/kiting floors; forests break LOS naturally. Enemy placement uses packs-in-clearings rather than rooms.
- Hooks, not scope, for later: Game Idea.md's "chunk environment affects spell power" (fire weak in swamp rain, etc.) attaches to the theme tag; Magic doc §13 environmental essence likewise. Just carry the theme on `Maze` now.
- Cadence: suggested ~1 in 4 floors past floor 3 rolls an open archetype (tunable; owner may prefer fixed themed depths — flagged in §8).
- **Guardian arenas (owner ruling 2026-08-04)**: the safe-room Guardian door no longer spawns the boss *into the safe room* (the current behavior — including the safe room's 3× regen staying active during the fight). It transitions to a dedicated open themed arena — **deliberately small** (default 25×17, ~⅓ the area of the smallest regular floor; fully lit, Guardian visible from the gate — a set-piece, never a map to search) — **themed to that specific Guardian** — dominant affinity picks an elemental arena (Scorched Waste, Blighted Swamp, …), class archetype otherwise (Colosseum Ring, Forest cover-fight, …), race adds props/tint, and the Guardian's kit shapes cover density (ranged → open ground, melee → dense ambush cover; home-field advantage by design). Retreat through the gate is always allowed (Guardian resets to full HP — anti-door-dance); victory spawns an exit portal so the corpse is lootable before moving on. Details: Planning note 04.

### 1.3 Placement that understands the map
- **Stairs**: keep the far-quartile BFS rule, prefer in-room placement.
- **Chests**: 1–3 scaling with floor area; prefer rooms and dead-ends; distance-banded so at least one is off the critical path.
- **Traps**: count scales (1–4); prefer corridors, doorways, and the tiles *around* chests; never within the start band.
- **Enemies**: density from floor area (not `3 + floor`); BFS-distance banding (no spawns within N steps of start; heavier density in the far half); **room packs** (2–3 enemies clustered in a room) plus corridor wanderers.
- **Locked vault** (the long-deferred feature, now cheap): ~1 per 2–3 floors, a room whose door is `DoorLocked`; the key drops from one designated "keyholder" enemy on that floor (marked visually); vault holds an above-band chest.

### 1.4 Verification
New `TEST_MAPGEN=1`: generate 100 floors across depths; assert connectivity, stairs min-distance, all doors reachable, vault key reachable without entering its vault, room-count bounds, per-floor gen time budget. Extend `TEST_DUNGEON` for door-open and vault-key flows. Full regression suite.

---

## 4. Phase 2 — Generated Worlds & the Starting Town (L)

*Goal (reframed by owner rulings 2026-08-05): worlds are **generated at world creation** from player options, and each world's starting region — built to the [Starting Region.md](Starting%20Region.md) grammar, same style but never the same town — is frozen permanently into that world's save.*

### 2.0 World foundation first (PR 6a — the retrofit cliff; Planning note 12)
- **`WorldGenOptions`**: Seed (visible/enterable), World Size (Small/Medium/Large — scales region dims, population ~60/100/150, forest depth; *not* town count, that's v2), Hostility (Peaceful/Normal/Hostile — a multiplier profile over existing tunables: enemy density, level ranges, elite chance, event frequency, guard coverage, wildlife temperament). **No resource knob** — richness derives from the region's environment (owner ruling).
- **World↔character save split**: `Saves/Worlds/{id}/world.json` (frozen gen output) + `delta.json` (the world's memory) + `Characters/` inside. Title flow: Worlds screen → Characters screen. Old saves auto-migrate into an "Origins" world.
- **Legacy worlds (owner ruling)**: character death deletes the character, never the world. A `LegacyHero` record (name, class, lifetime levels, days lived, cause of death, notable counters) is written to the delta, and NPCs the hero interacted with hold a memory — surfacing later as **memorial barks** ("Old {name} drilled right where you're standing…"). New characters enter a world that talks about the people who came before.

### 2.1 RegionGenerator: grammar + prefabs (PR 6; Planning note 08)
- A seeded `TownGenerator` producing: wall ring with 2 gates; mountain edge with the dungeon entrance + guard station; mine outside the walls near a gate; river strip on the opposite side with intake (water tower) upstream and sewage outfall downstream; main roads gate-to-gate through a town square; building lots along roads.
- Buildings reuse Phase 1's room machinery: walled rects with a road-facing door, simple furnished interiors (feature objects). Interiors live **on the same map**, dark-until-entered via per-room visibility (room metadata makes this cheap) — no sub-map system needed for v1.
- POI set: smithy, alchemist, carpentry shop, temple, training school, town square + market stalls, guard station, water tower, ~35–45 homes (~100 residents at the spec's mixed household sizes), one authored home with the hidden trapdoor (inert until the dark-temple pass), a sewer grate (visual only for now).
- Generation runs at **world creation**, seeded by `WorldGenOptions`; output frozen to `Saves/Worlds/{id}/world.json`, permanent for that world's life. Hand-polish lives in **prefabs** (`Data/World/Prefabs/` — hand-authored building stamps the generator arranges, CDDA-mapgen style), and **validation is load-bearing** (no hand-fix pass exists on a player's world; failed grammar asserts regenerate with seed+1). `TEST_TOWNGEN=1 SEED=n` survives as the generator-tuning ASCII preview.
- Map scale from the Size profile (72×48 / 96×64 / 128×84). Camera/culling already handled in Phase 1.1.

### 2.2 Migration
- Keep Press-E interactions; existing Smithy/Stall/Mine features move onto the new map. `TEST_OVERWORLD` resolves POIs by feature lookup, not hardcoded coordinates.
- Save compatibility: `ResumePoint.OverworldEntrance` re-anchors to the new dungeon-entrance position via the Phase 0.3 helper.

Exit: full loop (dungeon → shrine → town → mine → smithy → stall → dungeon) green on the frozen map; town renders with walls/roads/river/buildings; interiors reveal on entry.

---

## 5. Phase 3 — Combat & spell identity (M–L, can run parallel to Phase 2)

*Goal: attacks differ by what they DO, not just their numbers.*

### 3.1 Status effects first (the enabler everything else references)
- `StatusEffect {Type, Magnitude, TicksRemaining, Stacks}` lists on Hero and Enemy; one per-tick processor in `GameState`; application API with resistance (Con + elemental affinity resistance already computed by `AffinityService`).
- v1 table (SimpleRPG's mapping, already earmarked in Implementation Plan.md): Fire→**Burn** (DoT), Ice→**Chill** (move/attack slow; stacks→brief Freeze), Lightning→**Shock** (brief stun/interrupt), Poison→**Toxin** (stacking DoT), Earth→**Stagger** (knockback+short stun), Air→**Blind** (accuracy down), Water→**Slow**.
- **Build Chill/Slow on a per-entity `TimeScale` multiplier** (scales movement delta, cooldown decrement, and that entity's projectile velocities in one place) rather than ad-hoc stat edits — this same substrate is what the Agility time-dilation system (3.6) runs on.
- Renderer: status chips/tint on entities; message-log entries. `TEST_STATUS` demo.

### 3.2 Behavior payloads (data-driven, replaces special-casing)
Extend `Weapon`/`Spell` → `Attack` projection with generic behavior fields consumed by `CombatSystem`/projectile update:
- `OnHitStatus` (type/chance/magnitude/duration), `PierceCount`, `ChainCount + ChainRange`, `ExplodeRadius` (AoE on impact), `MultiShot + SpreadDeg`, `Homing` (turn rate), geometry v1 (`Cone`/`Beam` as short-lived shaped hitboxes).
- **Wire the already-authored `KnockbackDistance`** (push along projectile heading, wall-clamped) — the data has been sitting there dead.
- Delete the `arcane-blast` id special case — it becomes `ExplodeRadius` data.

### 3.3 Kit identity pass over the existing roster
Each class gets a mechanical signature, not a stat flavor: Warrior — knockback + a real cleave arc; Rogue — positional backstab bonus (positions exist; bonus damage from behind); Archer — pierce at range; Mage — elemental variety with statuses; Priest — holy smite + self-heal component; Bard — AoE push/interrupt (Sonic finally means something). Fireball explodes (small `ExplodeRadius` + Burn); Ice Shard chills.

### 3.4 Spell content expansion
- `Data/Spells/spells.json`: implement Magic_System_Reference §9's Charm→Tier-1 lines (15 element lines: Ember→Fire Bolt, Frost→Ice Bolt, Sting→Poison Spray, Spark→Lightning Bolt, Mend→Rejuvenate, …) as data — now expressible because statuses/behaviors exist.
- These enter the game via loot and via the **trainer learning flow in Phase 4.4** (which finally wires the dormant `AffinityService.CanLearn` tier gate).
- The full rune grammar (§7–8) and research minigame (§11) stay deferred — this phase makes the *content* real; invention comes later.

### 3.6 Agility time dilation (owner addition 2026-08-04)
Past a certain Agility, the hero stops speeding up on screen — instead **the world slows down around them**. High-Agility entities still appear to move near-normally; low-Agility ones crawl, relative to the player's perception.

Concrete mechanic (all tunables in one spot):
- Every actor has a **tempo**: `Tempo(agi) = 1 + k·effectiveAgility` (the same curve that drives move speed today).
- Below the threshold `A*` (proposal: effective Agility ~20): exactly today's behavior — hero's on-screen speed grows with Agility, world runs at scale 1.
- At/above `A*`: the hero's **on-screen** speed is capped at `Tempo(A*)` (also fixes projectile-tunneling physics at silly speeds), and the surplus becomes dilation — every other entity `E` gets `TimeScale(E) = clamp(Tempo(E.agi) / Tempo(hero.agi), 0.35, 1)` applied via the 3.1 substrate.
  - Enemy with Agility ≈ yours → ratio ≈ 1 → appears normal ✓. Low-Agility town guard → crawls ✓.
  - **Projectiles inherit their shooter's TimeScale** — a slow archer's arrow drifts toward you, genuinely dodgeable. This is the payoff fantasy of an Agility build.
  - Floor of 0.35 keeps the world from freezing outright; Chill stacks multiplicatively on top but shares the same clamp.
- Hero-side rates (their own cooldowns/regen) stay in the hero's frame — acting more often *relative to the world* is the point.
- UX: subtle screen cue while dilation is active (slight desaturation or vignette) + a HUD indicator, so it reads as a power, not a bug. NPC schedules (Phase 4) tick in *world* time, so dilation doesn't desync town life.
- Verified via `TEST_TEMPO`: hero at Agi 40 vs a 5-Agi and a 35-Agi enemy — assert screen-speed cap, per-entity scales, projectile slow-down, and that pausing/turn-based mode compose with it.

### 3.7 Stealth: sneaking, noise, and hidden perception (owner addition 2026-08-04)
Three interlocking pieces (full design: [Stealth System.md](Stealth%20System.md); dev note: Planning 11):
- **Sneak mode** (toggle): speed ×0.55, quieter steps, visual detection slowed by Agility (Game Idea.md's long-listed "Stealth Modifier" finally lands).
- **Noise events**: footsteps, gear rattle (Heavy/Light attribute multipliers — armor weight takes over when armor lands), attacks, spells, doors, chest channels, mining — each with a radius, muffled through walls. Sound leaks your *location* to listeners, not your identity.
- **Hidden awareness** replacing binary detection: enemies/NPCs get facing-based vision cones + 360° hearing feeding a per-entity awareness meter (Unaware → Suspicious/investigate → Alert=existing combat path). Wisdom perceives, Agility hides — symmetric with the existing trap-perception philosophy. Rolls are invisible; the player reads states (HUD eye icon, ?/! pips over enemies).
- **Tuning contract**: cone + hearing ≈ today's 360° detection for a non-sneaking hero — stealth is opt-in depth, not a tax on normal play. Stealth strike from Unaware = backstab + auto-crit (composes with 3.3's positional backstab); the strike's noise wakes the room.
- Guards, theft, trespass, night sight-reduction, dark-temple infiltration, and Thief/Assassin unlock triggers all consume this system later.

### 3.8 Armor & equipment slots (owner ruling 2026-08-05; Planning note 13)
Eight slots, finalized 2026-08-05: Head/Chest/Legs/Hands/Feet + RingLeft/RingRight/Amulet. Weight classes finally live: Heavy = defense + slow + loud, Light = quiet + fast. Sources: loot, smithy recipes (leather from creature hides), merchant stock. Enemies and guards wear it, it shapes their defense, and it drops as corpse loot. Rings/amulet are inert stat carriers v1 and future enchantment targets.

### 3.9 Creatures & the bestiary (owner rulings 2026-08-05; Planning note 14)
Broad public-domain roster (town dogs/cats; wild rabbit/deer/boar/wolf/bear; staple rats/bats/spiders/slimes) via a `CreatureFactory` template system with **elemental-essence variants** — Dire versions and elemental forms (Cinder Bear, Frost Wolf) whose variants bias toward the open floor's theme. **Lore rulings:** the cursed races are the goblinoid family — Goblin, Orc, Kobold, plus enemy-only Hobgoblin and Troll (Bugbear/Ogre easy adds) — and dungeon humanoids are cursed races only (Guardians occasionally non-cursed dark-flavored exceptions like a Necromancer). `races.json` carries a `cursed` flag so the spawn filter is data, not a list in code.

### 3.10 Turn-based mode — world-waits (owner ruling 2026-08-05; Planning note 05 §7)
The §0a-promised precision tool, confirmed in-milestone: time flows only while the player acts (Superhot/CDDA feel). A tick-gate on the substrate, not a new engine; freezes everything including the world clock while you think.

Exit: `TEST_STATUS`/`TEST_TEMPO`/`TEST_STEALTH`/`TEST_ARMOR`/`TEST_CREATURES`/`TEST_TURNBASED` + per-behavior asserts; regression clean; each class demonstrably plays differently in a hands-on pass.

---

## 6. Phase 4 — Peaceful NPCs and how they live (L, needs Phase 2)

*Goal: the town is inhabited; residents have homes, jobs, and days — simplified schedules first (Starting Region.md's own recommendation), needs-simulation later.*

### 4.1 World clock (prerequisite)
Tick-derived time-of-day + day counter (e.g. 1 game hour ≈ 30s real → ~12-minute days; tunable in settings). HUD clock in town. (Light tinting by hour is optional polish.)

### 4.2 `Npc` actor + population (generated once, frozen with the town)
- `Npc`: identity (name, race, occupation, age band), `HomeId`, `WorkplaceId`, personality seed. Population (~100) generated by the TownGenerator pass and frozen into `starting_town.json` — households of mixed size per the spec.
- Occupations map to POIs: smith, alchemist, carpenter, priest, teacher, merchants, guards, miners, **the two water-elementalist sewer workers + their guard**, homemakers, children.
- Architecture: give `Hero`/`Enemy`/`Npc` a light shared `Actor` surface (position, stats, activity) rather than a big unification refactor — NPCs need full character sheets anyway (they're race+class characters, same as enemies; `EnemyFactory`'s derivation path is reusable).

### 4.3 Schedule → Goal → Activity (the "goal system")
- Per-NPC `DailySchedule`: time blocks → goals (`Sleep(home)`, `Work(workplace)`, `Meal(home|tavern)`, `Socialize(square)`, `Patrol(route)` for guards, `School(training school)` for children).
- A goal resolves to: BFS path (existing `MovementSystem` machinery) + a generalized **Activity** at the destination (the Activity pattern already exists — generalize it to take an actor, keeping hero activities untouched).
- Local steering: port the context-steering algorithm from the Godot prototype's `test_enemy.gd` (16-direction weighted steering with obstacle rays + separation) so NPCs flow around each other in streets instead of BFS-gridlocking.
- This is deliberately **schedules, not needs** — the emergent-needs simulation (hunger/mood/economy) plugs into the same Goal seam later.

### 4.4 Interaction v1
- **Talk**: occupation- and time-aware one-liner barks (data-driven lines; no dialogue trees yet).
- **Merchants**: the market stalls become NPC-backed buy/sell (extends the existing sell flow with a buy list).
- **Trainer** (training school): teaches Tier-0/1 spells from Phase 3.4, gated by `AffinityService.CanLearn` — the first real wiring of the learning tier system, and the natural gold sink.
- **Priest** (temple): paid healing.
- **Charisma & Opinion (owner ruling 2026-08-05)**: merchant prices scale with Cha; every interaction grows a per-NPC Opinion value at a Cha-accelerated rate — warmer barks, better prices, and more NPCs carrying your memory into the legacy system when you die.

### 4.5 Guards & consequences v1
- Guards patrol (gates ↔ square ↔ dungeon entrance) on schedule; off-shift they're residents.
- Attacking an NPC: victims flee (goal override), guards converge and go hostile (NPCs are full characters, so the existing combat path works against the player).
- Killing an innocent is recorded (Hero counter + Codex) — the **Cultist** class-unlock trigger from Class Tree Inventory.md, consumed in Phase 5.5.

### 4.6 Verification
`TEST_TOWNLIFE=1`: simulate 3 game days headless; assert every NPC is home at night / at work in their block, all goals reached within a tick budget (no stuck pathing), guard patrol coverage, flee-and-respond behavior on a scripted assault.

---

## 7. Phase 5 — Progression depth (M–L)

*Goal: growth systems beyond stat points — spells level, abilities work, classes specialize.*

### 5.1 Spell/ability leveling with use (Game Idea.md's rule)
- Per-`Combinable` XP: casts + hits grant XP; quadratic-ish curve; level scales modestly (+damage / −cooldown / −cost per level). Tooltips show level/XP.
- **Intelligence governs mastery speed (owner ruling 2026-08-04)**: Int multiplies *skill/spell XP gain rate* (e.g. `× (1 + effInt × 0.03)`, tunable) — this is Int's XP role, replacing the removed Charisma character-XP bonus. (Wisdom analog for faith skills when those level — note, don't build yet.)
- **Evolution choices** at level thresholds: data-driven options per spell line following §9 tiers (Mana Dart → Mana Bolt …), gated by both spell level and elemental affinity (`LearnableTier`). Combining rules from CombinationEngine stay as-is (level = min, rarity-bump resets — already implemented).

### 5.2 Abilities become real
- An ability-effects pass in the effective-stat pipeline: one choke point computing `Effective*` as (base + gear + ability modifiers) × racial effectiveness (× status modifiers from Phase 3). Dense Musculature's `StrengthMult 1.5` and Mana Circuitry's cooldown/regen mods finally function.
- Ability slots (max 3 for humans per Game Idea.md; per-race caps in races.json).

### 5.3 Rarity means power
Small multipliers at `ToAttack()` projection (e.g. ~+8% damage or −6% cooldown per tier above Common). Loot and forging instantly matter more; sell prices already scale.

### 5.4 Class specialization v1 (the first slice of the 115-class tree)
- **Action counters** on Hero + Codex: kills by weapon type, unarmed kills, spells learned before L5, innocents killed, bodies at the dark temple (later).
- Unlock notifications when a trigger fires; **class change offered at the trainer** (swaps class affinity seed + starting-kit template; stats stay).
- First wave = the triggers already implementable after Phases 3–4: Monk (unarmed kills), Spellsword (sword kill + spells), Thief (steal/gold trigger — simplest proxy first), Elementalist (two elemental spells before L5), Berserker, Cultist (innocent kill). Necromancer waits for the dark temple.
- **Meta-progression store** (`Saves/meta.json`, alongside the Codex): unlocked classes/races persist across characters and appear at creation — the "available as a starting class on later playthroughs" rule.
- Level-gated evolutions (10/25/50/75 → Uncommon/Rare/…) come after this proves out, as data tables.

---

## 7b. Phase 6 — The Alive World (owner rulings 2026-08-05)

The DF/CDDA layer, all confirmed in-milestone. Per-system notes 15–18.

- **World items, containers & ownership** (note 16, PR 6b): items on the ground and in shelves/barrels/cupboards; drop/pick-up/container verbs reusing the loot window; everything ownable — taking owned things is theft, resolved through the stealth witness check and the crime flow; merchants refuse stolen goods; shops become real shelves. *"Otherwise the world feels false."*
- **Player needs, food & meditation** (note 15, PR 9b): hunger + rest as lightweight bands (buffs for upkeep, mild penalties, never lethal); travel drains stamina and demands breathers; food bought/looted v1 with cooking joining the consumables pass; **Meditate** as a levelable skill — ×4 resource regen channel, interruptible, vulnerable while channeling.
- **Offscreen events** (note 17, PR 9c): an event framework where the world moves without you — wave 1 ships caravan arrivals, sewer outbreaks, and small dungeon breaks; the full owner catalog (invasion, cult uprising, plague, famine, flood, dragon attack — *"EVERYTHING"*) lands as data entries in a follow-on content pass. Abstract resolution when absent, concrete fights when present. **NPCs can die offscreen — no plot armor ("death makes it real")** — with apprentice/newcomer backfill so towns degrade honestly and heal slowly.
- **Audio** (note 18, PR 12): **no backend spike — Godot ships the audio engine** (ruling #30). Core emits sound events (shared with the stealth system's noise model); the Godot client wires them to `AudioStreamPlayer`s and buses. SFX-first, semi-retro/chiptune + atmospheric hybrid aesthetic.

---

## 8. Design decisions — resolved and open

**Resolved (owner rulings 2026-08-04):**
1. ~~Dungeon identity dial~~ — **hybrid confirmed**: parts roomy, parts mazy, plus the new open-terrain themed archetypes (§1.2b).
2. ~~Interiors~~ — **same map**, for immersion as well as simplicity.
3. ~~Charisma XP~~ — **removed**. Bonus XP only for fighting above your weight, measured against lifetime `TotalLevelsGained` (class advancement can reset `Level`, never this). Int's XP role = skill-mastery speed (§5.1).
4. *(new system)* **Agility time dilation** accepted into scope (§3.6).
5. *(new system)* **Guardian arenas** — the Guardian door leads to a per-Guardian open themed arena (§1.2b), replacing the boss-in-the-safe-room behavior.
6. *(new system)* **Stealth** — sneaking + noise-from-actions + hidden awareness/perception (§3.7) accepted into scope.
7. **Generated worlds (2026-08-05)** — worlds are generated at world-creation time from player options: Seed, Size, Hostility (**Peaceful / Normal / Hostile**). No resource-richness knob: abundance is predetermined by region/environment.
8. **Legacy worlds (2026-08-05)** — character death keeps the world; NPCs who knew the fallen hero remember them and mention them in passing (memorial barks).
9. **Starting region as grammar (2026-08-05)** — every world's starting town obeys the Starting Region.md rules; same style, never the same town.
10. **Vault cadence (2026-08-05)** — exactly one vault per 5-floor gate group, randomly placed on one of the 4 floors before the Guardian floor (Planning note 03).
11. **Charisma's social role (2026-08-05)** — Cha wires into merchant prices (Game Idea's original role) and into a new per-NPC **Opinion** value: interactions go more smoothly, people remember you more, and grow to care about you quicker. Opinion feeds bark warmth, price shaving, and legacy-memory formation (Planning notes 09/12).
12. **No save migration (2026-08-05)** — pre-split saves are playtests; delete them. Compatibility machinery never gates updates.
13. **Onboarding (2026-08-05)** — lightweight contextual message-log hints only ("Press C to sneak"); no tutorial. They'll figure it out — part of the fun is dying.
14. **The spine (2026-08-05)** — the main game is a **sandbox**: no win condition; legacy worlds are the long game. The original tower concept survives as a *future lightweight play mode* (§10), not as the main game's structure.
15. **Armor (2026-08-05)** — in-milestone; full slot roster kept per the character design; loot + smithy + merchants; enemies/guards wear and drop it (§3.8).
16. **Creatures (2026-08-05)** — in-milestone; broad public-domain roster; Dire + elemental-essence variants (Cinder/Frost/…); **goblins and orcs are cursed races**; dungeon humanoids = cursed races only, Guardians occasionally non-cursed dark exceptions (§3.9).
17. **Turn-based (2026-08-05)** — in-milestone, **world-waits** model (§3.10).
18. **Underground (2026-08-05)** — v2, layer slot reserved in world.json now; mine stays Press-E in v1.
19. **Player needs (2026-08-05)** — hunger + rest lightweight; travel stamina drain; **Meditate** skill; food buy/loot v1, cooking with the consumables pass (§7b).
20. **World items & ownership (2026-08-05)** — in-milestone: "otherwise the world feels false" (§7b).
21. **Offscreen events (2026-08-05)** — in-milestone framework + wave 1; full catalog (invasion, plague, famine, flood, dragon…) as staged data; **NPCs can die offscreen, no exceptions** (§7b).
22. **Day/night (2026-08-05)** — 15 real-minute days + 15 real-minute nights (30-minute cycle; daylight 6:00–18:00).
23. **Dilation control (2026-08-05)** — defaults to combat-only; player-set mode (InCombat/Always/Off) + strength slider, capped by what Agility has earned.
24. **Sneak UX (2026-08-05)** — toggle confirmed; while sneaking the game renders enemy vision cones and the player's own noise radius (Planning note 11).
25. **Open-floor cadence (2026-08-05)** — fully random two-stage roll (type first at ~20%, then biome); no set pattern, no pity timer; open floors stay rarer than corridor floors.
26. **Audio (2026-08-05)** — backend spike early, SFX-first; semi-retro/chiptune + atmospheric hybrid.
27. **Floor sizes (2026-08-05)** — fully random per floor, independent axes, no depth scaling of any kind; bounds are tunables (~31–101 × 21–101).
28. **Cursed roster (2026-08-05)** — the goblinoid family: Goblin, Orc, Kobold + enemy-only Hobgoblin and Troll (Bugbear/Ogre as easy adds); data-flagged in races.json. In-world family name still open (working term: "goblinoid").
29. **Equipment slots (2026-08-05, reconciled same day)** — the merged system's **MainHand/OffHand handedness model wins** (owner-confirmed), with **RingLeft, RingRight** (one per hand) and **Amulet** kept. Core's `EquipmentSlot` enum already carries the full union: Head, Chest, Hands, Legs, Feet, Amulet, RingLeft, RingRight, MainHand, OffHand — no code change was needed; the ruling is satisfied as built.
30. **The final product is the Godot client (2026-08-05)** — Avalonia is frozen at current parity as the development harness/legacy client: no new screens, rendering, or sprite investment there. All client-side work in this plan lands **Godot-first** (see the touchpoint mapping in [Planning/README.md](Planning/README.md)); Core remains the single shared simulation. Consequences: the audio backend spike is obsolete (Godot ships an audio engine — note 18 rewritten); a **Godot overworld view** becomes a load-bearing PR gating the NPC phase; night lighting re-lands via Godot 2D lights (`CanvasModulate` + `PointLight2D` + occluders) using the Avalonia implementation as its behavioral spec; the `TEST_*` harness gets extracted to a Core-referencing console project so verification outlives Avalonia; `hotbarKeys` canon flips to Godot's InputMap when Avalonia retires.
31. **Rarity is intrinsic (2026-08-05, IMPLEMENTED same day)** — a definition's rarity is fixed (plain Iron gear is Common forever, never rolls Legendary); loot randomness lives in *which* item drops (rarity-weighted, floor-boosted selection); rarity climbs only through combining/crafting; and rarity finally has mechanical weight (`RarityScaling`: +8% damage, -3% cooldown, +15% defense, +25% consumable effect per tier). Consumables are hotbar-slottable quick-use bindings in both clients.

**Still open:**
1. **NPC depth v1** — schedules-only remains the working assumption (per Starting Region.md); NPC-side needs stay abstracted until the economy pass.
2. **Stealth details** — stealth-strike = backstab + auto-crit stands as the default (unobjected); enemy Rogues sneaking at the player is a v2 candidate.
3. **Dilation numbers** — threshold `A*` (~20) and world-scale floor (0.35) remain playtest tunables.
4. **In-world name for the cursed family** — "goblinoid" is mechanical nomenclature; the lore term (the Cursed? Curse-born?) is the owner's to coin.

## 9. Suggested build order

| PR | Content | Size | Depends on |
|---|---|---|---|
| 1 | Phase 0 quick fixes (incl. the XP rework, 0.4) | S | — |
| 2 | 1.1 tile/door/room model + iterative carve + culling | M | — |
| 3 | 1.2–1.4 generator + placement + TEST_MAPGEN | M–L | PR 2 |
| 3b | 1.2b open-terrain themed floors (Forest/Swamp/Grassland; Fortress after PR 6) + Guardian arenas | M | PR 3 |
| 4 | 3.1 status effects + per-entity TimeScale substrate | M | — (parallel-capable) |
| 4b | 3.6 Agility time dilation + TEST_TEMPO | S | PR 4 |
| 4c | 3.10 turn-based world-waits mode + TEST_TURNBASED | S | PR 4 |
| 5 | 3.2–3.3 behavior payloads + kit identity | M | PR 4 |
| 5b | 3.7 stealth: sneak mode, noise events, awareness AI, sneak overlay + TEST_STEALTH | M | PR 5 (stealth strike) |
| 5c | 3.8 armor & equipment slots + TEST_ARMOR | M | PR 5 |
| 5d | 3.9 creatures & bestiary (variants, spawn rework, cursed-race dungeons) + TEST_CREATURES | M | PR 5 (payloads); before PR 8 |
| 6a | 2.0 world foundation: WorldGenOptions, world↔character save split, creation flow, legacy records | M | — (must precede PR 6) |
| 6 | 2.1–2.2 RegionGenerator (grammar + prefabs) → per-world freeze → load | L | PR 2, PR 6a |
| 6b | 7b world items, containers, ownership + TEST_WORLDITEMS | M | PR 6, PR 5b (witness checks) |
| 6c | **Godot overworld view**: render the town + Press-E layer in the Godot client, incl. night lighting via Godot 2D lights (Avalonia impl = behavioral spec) | M | PR 6 |
| 7 | 3.4 spell content (Tier-0/1 lines) | S–M | PR 5 |
| 8 | 4.1–4.3 world clock + NPCs + schedules (+ memorial barks off PR 6a legacy data) | L | PR 6, PR 6c (a town to see them in) |
| 9 | 4.4–4.5 merchants/trainer/priest + guards + Cha-scaled prices & Opinion v1 | M | PR 8, PR 7, PR 5b, PR 5c (armored guards) |
| 9b | 7b needs + food + Meditate + TEST_NEEDS | M | PR 9 (vendors), PR 5d (ingredient drops) |
| 9c | 7b offscreen events framework + wave 1 + TEST_EVENTS | M | PR 9, PR 5d (break/outbreak creatures) |
| 10 | 5.1–5.3 spell leveling (Int-scaled mastery) + abilities + rarity power | M | PR 5 |
| 11 | 5.4 specializations + meta store (Thief trigger → real thefts via PR 6b) | M–L | PR 9 |
| 12 | audio: wire Godot AudioStreamPlayers/buses to Core sound events + ~6 proof SFX (backend spike obsolete — ruling #30) | S | — (anytime; content pass post-milestone) |
| 13 | extract the TEST_* harness to a Core-referencing console project (verification outlives Avalonia) | S | — (cleanup; before Avalonia retires) |

Every PR keeps the house rule: a `TEST_*` headless demo proving the new mechanic + the full regression suite + a GUI smoke, with owner-eyeball items listed explicitly.

UI cost note (owner-acknowledged 2026-08-05; retargeted by ruling #30): PRs 6a–11 imply ~6 new screens (world creation, worlds list, trainer, merchant, evolution choice, ability slots) plus a Settings screen — all built as **Godot Control screens in `GameUi`**, not Avalonia XAML. Expect roughly half of PR 6a/9's effort to be screens. Onboarding hints (ruling #13) ride whichever polish pass comes last.

---

## 10. Future play modes (recorded, out of milestone)

**Tower mode (owner concept, recorded 2026-08-05).** The original game concept — a set sequence of floors where *each floor is a completely unique world* with its own towns and events — survives as a planned **lightweight secondary play mode**, not the main game's structure. It leans into the rogue side (fast runs, fresh world per floor, presumably no legacy persistence) where the main game leans full RPG-sandbox. Not scoped; recorded so it shapes architecture where cheap — the world-generation machinery (grammar + prefabs + WorldGenOptions) is exactly the engine Tower mode would call once per floor, which is another reason the generator stays a runtime system rather than a dev-time tool.

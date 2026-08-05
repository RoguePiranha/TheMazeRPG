# Planning folder

This folder holds the project's planning documents and the **per-system development notes** for the functional-state milestone.

- [Implementation Plan.md](Implementation%20Plan.md) — the running log / single source of truth for where the code is vs. where the design wants to go. Owner rulings get recorded there, dated.
- [../Functional State Plan.md](../Functional%20State%20Plan.md) — the 2026-08-04 milestone plan (phases, PR order, resolved/open decisions). Each PR row in its §9 maps to one note below.
- The numbered notes — one per system, in build order. Each contains: development steps, exact touchpoints in the current code (`path:line` as of the 2026-08-04 audit), code sketches for the complex parts, tunable parameters with proposed starting values, and the `TEST_*` verification plan.

| Note | Covers | PR row |
|---|---|---|
| [01 - Quick Fixes.md](01%20-%20Quick%20Fixes.md) | Unlock-wipe bug, affinity persistence, arrival helper, XP rework | PR 1 |
| [02 - Tile Door Room Model.md](02%20-%20Tile%20Door%20Room%20Model.md) | `TileType`, doors, `Room` metadata, iterative carve, culling | PR 2 |
| [03 - Dungeon Generator.md](03%20-%20Dungeon%20Generator.md) | Rooms-in-maze hybrid, map-aware placement, locked vault | PR 3 |
| [04 - Open Terrain Floors.md](04%20-%20Open%20Terrain%20Floors.md) | Themed open-air floors, Perlin parameters per theme | PR 3b |
| [05 - Status Effects and TimeScale.md](05%20-%20Status%20Effects%20and%20TimeScale.md) | Status system + per-entity TimeScale substrate | PR 4 |
| [06 - Agility Time Dilation.md](06%20-%20Agility%20Time%20Dilation.md) | Tempo curve, world-slowdown mechanic | PR 4b |
| [07 - Attack Behaviors and Kit Identity.md](07%20-%20Attack%20Behaviors%20and%20Kit%20Identity.md) | Behavior payloads, per-class kits, Tier-0/1 spell data | PR 5, 7 |
| [11 - Stealth Noise and Awareness.md](11%20-%20Stealth%20Noise%20and%20Awareness.md) | Sneak mode, noise events, hidden awareness/perception AI | PR 5b |
| [12 - World Creation and Legacy.md](12%20-%20World%20Creation%20and%20Legacy.md) | WorldGenOptions (seed/size/hostility), world↔character save split, legacy heroes + NPC memory | PR 6a |
| [08 - Town Generator.md](08%20-%20Town%20Generator.md) | World & town generation — grammar + prefabs, frozen per world | PR 6 |
| [09 - NPC Life System.md](09%20-%20NPC%20Life%20System.md) | World clock, Npc model, schedules/goals, steering, guards | PR 8, 9 |
| [10 - Progression Depth.md](10%20-%20Progression%20Depth.md) | Spell leveling, ability effects, rarity power, specializations, meta store | PR 10, 11 |
| [13 - Armor and Equipment Slots.md](13%20-%20Armor%20and%20Equipment%20Slots.md) | Full-slot armor, weight classes, enemy/guard armor | PR 5c |
| [14 - Creatures and Bestiary.md](14%20-%20Creatures%20and%20Bestiary.md) | Creature templates, Dire/elemental variants, cursed-race dungeon spawn rework | PR 5d |
| [15 - Needs Food and Meditation.md](15%20-%20Needs%20Food%20and%20Meditation.md) | Hunger/rest bands, travel fatigue, food, Meditate skill | PR 9b |
| [16 - World Items Containers Ownership.md](16%20-%20World%20Items%20Containers%20Ownership.md) | Ground items, containers, ownership + theft substrate | PR 6b |
| [17 - Offscreen Events.md](17%20-%20Offscreen%20Events.md) | Event framework, wave-1 roster, NPC death + backfill | PR 9c |
| [18 - Audio.md](18%20-%20Audio.md) | Backend spike, IAudioService, SFX-first content plan | PR 12 |

House rules that apply to every note: every feature ships with a `TEST_*=1` headless demo + the full regression suite + a GUI smoke; tunables live in `Data/Config/settings.json` or one named constant block, never scattered; content is data-driven (JSON under `Data/`) wherever a table would otherwise be hardcoded.

Code sketches are **starting points, not drop-in files** — they use the audited names/lines from 2026-08-04 and will need reconciling with whatever has landed since.

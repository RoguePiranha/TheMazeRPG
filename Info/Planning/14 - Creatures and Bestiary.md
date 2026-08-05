# 14 — Creatures & the Bestiary (PR 5d)

**Owner rulings 2026-08-05:** in this milestone. Broad public-domain roster ("take any creature that's not copyrighted and bolt it in"). **Elemental-essence variants**: creatures steeped in elemental essence change fundamentally — Dire versions, or elemental forms (Cinder Bear, Frost Wolf). **Lore ruling: goblins and orcs are cursed races** — humanoid sentients under a curse. **Dungeon population ruling: humanoids in the dungeon are cursed races only** (for the most part); Guardians may occasionally be non-cursed humanoids with dark flavor (e.g. a Necromancer).

## Creature templates — `Data/Creatures/creatures.json`

```json
{ "id": "wolf", "name": "Wolf", "category": "beast",
  "habitats": ["forest", "grassland-floor", "forest-floor"],
  "behavior": "pack-predator",        // passive | skittish | territorial | predator | pack-predator | lurker
  "baseStats": { "hp": 24, "attack": 6, "defense": 1, "agility": 8, "wisdom": 3 },
  "attacks": [ { "id": "bite", "damage": 6, "range": 0.9, "cooldown": 14, "animation": "Quick" } ],
  "tameable": true, "radius": 0.32, "drops": ["wolf-hide"], "xp": 12 }
```

`CreatureFactory` alongside `EnemyFactory` (no race×class derivation — templates + level scaling). Natural attacks reuse the note 07 payload system (spider bite → Toxin, boar gore → Knockback). Behaviors map onto the existing AI + note 11 awareness: `passive` flees on Alert; `skittish` flees on Suspicious; `territorial` attacks within a home radius; `predator` hunts on sight; `lurker` ambushes (stays still until close).

**v1 roster** (public-domain basics; extend freely): town — dog, cat (ambient, non-combat, flee). Wild — rabbit, deer (skittish), boar (territorial), wolf (pack), bear (territorial heavy). Staples — giant rat, bat (erratic flight path), spider (Toxin), slime (slow, Poison-tinted). Sewer/dungeon vermin variants of rat/bat/slime/spider. `drops` feed armor crafting (hides/leather — note 13 lands adjacent on purpose) and future alchemy reagents.

## Variants — essence changes them fundamentally

Applied at spawn as a template transform (one `VariantDef` table, not per-creature authoring):

- **Dire {X}**: HP ×1.8, damage ×1.5, radius +0.08, XP ×2 — the mundane apex.
- **Elemental {X}**: per-element prefix table — Fire=**Cinder**, Ice=**Frost**, Lightning=**Storm**, Poison=**Blight**, Water=**Tide**, Earth=**Stone**, Air=**Gale**, Shadow=**Umbral**, Light=**Radiant**, Arcane=**Arcane** — grants a high affinity seed in that element, tints the render, and adds the element's payload+status to its natural attacks (Cinder Bear's swipe burns; Frost Wolf's bite chills). XP ×1.6.
- Spawn weighting: **open-floor themes bias matching variants** (Scorched Waste rolls Cinder-heavy, Blighted Swamp rolls Blight — theme and fauna agree); wilderness variant chance scales with Hostility; deep floors raise variant rates.

## Who lives where (spawn-table rework)

| Context | Population |
|---|---|
| Corridor dungeon floors | **Cursed-race humanoids** (roster below) + dungeon vermin/monsters (rat, bat, slime, spider, lurkers) |
| Open-terrain floors | Beasts dominate, themed elemental variants; occasional cursed-race warband |
| Guardian arenas | Guardian per existing rules; **occasionally a non-cursed humanoid with dark flavor** (Necromancer-type) — a weighted exception table |
| Forest fringe / wilderness | Wild beasts, temperament by Hostility |
| Town | Ambient dogs/cats; livestock later |
| Sewers (v2) | Vermin + whatever the dark temple implies |

`EnemyFactory.PickRace` gains a context parameter: dungeon regulars draw from the cursed-race subset only; guards/NPCs/town draw from all races. This also means Elf/Human/Dwarf dungeon enemies disappear — a real identity change to dungeon population (and the Codex bestiary), deliberate per the ruling.

**The cursed roster (owner ruling 2026-08-05 — "goblinoid" as the working family term, in-world name open):**
- **Goblin, Orc** (existing playable races — stay playable; the Class Tree's Goblin→Orc→High Orc/Ogre racial-evolution line is exactly this cursed lineage).
- **Kobold** (existing race, assumed goblinoid per "what you would expect" — flag if wrong).
- **Hobgoblin, Troll** (owner-named; new `races.json` entries marked `"playable": false` — enemy-only, so character creation never offers them; Troll gets big multipliers + a Regen-status quirk, the classic).
- Easy adds later on the same flag: Bugbear, Ogre (Ogre doubling as the evolution-tier form).
- Explicitly **not** cursed: Human, Elf, Dwarf, Halfling, Dragonborn, Tiefling.
- `races.json` gains `"cursed": true` on the family so the dungeon spawn filter, and any future curse-themed mechanics (cleansing? the dark temple?), read one flag instead of a hardcoded list.

**Beast Tamer hook:** `tameable` flags are the class-unlock's future consumer (companion framework still parked — this PR only ships the flag).

## TEST_CREATURES

Template load + spawn per habitat table (100 floors: zero non-cursed humanoid regulars in corridor dungeons; beasts present on open floors); variant transform math (Dire/Cinder stat+payload asserts); Cinder Bear's attack applies Burn; behavior matrix (deer flees, boar holds ground, wolf pack converges, lurker holds until range); theme-variant bias statistical check; drops reach corpse loot; Codex records variant names distinctly. Full regression.

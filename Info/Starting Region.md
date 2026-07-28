# Starting Region — Town & Dungeon Entrance

Spec for the first Overworld region, authored 2026-07-24. Anchors the "dungeon entrance" point
the player arrives at when leaving the Dungeon via a safe-room shrine (see
[Implementation Plan.md](Implementation%20Plan.md), section 0a).

## Generation Approach

Procedurally generated **once**, during development, then that specific generated result becomes
the permanent, fixed content going forward — not regenerated per playthrough. Procedural
generation is used as an authoring tool to produce a plausible layout quickly; the output is then
fixed, so it can be hand-polished, balanced, and debugged like any other hand-authored content
rather than needing to hold up across arbitrary re-generations.

Kept deliberately small for this first slice — enough to prove the core loop (mine → craft →
sell/build), not a sprawling open world yet.

## Demographics

- ~100 residents.
- Averages 2 per home, but not uniform — some households have children, some residents live
  alone. Implies a mix of household sizes rather than exactly 50 identical 2-person homes.

## Regional Geography

- **Dungeon entrance**: set into the side of a mountain with steep sides. The steep terrain gives
  the town natural defense on that side.
- **Mine entrance**: a separate feature from the dungeon — further down the same mountainside,
  *outside* the town walls, near one of the gates. This is the mundane resource-gathering
  location (ore mining), distinct from the Dungeon (the Trial space).
- **River**: on the opposite side of town from the mountain. Water is channeled from the river
  into a water tower that supplies the town's drinking water. Sewage is fed out of the town
  further downstream, past where the river exits the town walls — downstream of the intake, so
  waste doesn't contaminate the drinking supply.
- **Cleared land**: roughly 100 meters of cleared ground surrounds the town outside the walls, so
  monsters can't approach unnoticed or use trees to climb/breach the walls.
- **Untamed lands**: beyond the cleared land, wilderness begins — except along roads, which are
  regularly patrolled by guards provided by the local kingdom the town belongs to (the town is a
  settlement within a larger kingdom, not fully isolated).
- **Forest**: kept small for this version — just far enough out to provide low-level monsters and
  choppable trees, not a sprawling forest yet.

## Town Layout

- **Walls**: surround the entire town.
- **Guard station**: posted at the dungeon entrance, in case of a "dungeon break" (monsters
  escaping into the town) and to keep children from wandering into the dungeon.
- **Town square**: with merchant stalls (this is where a player pursuing the merchant path would
  set up shop, per the original "endless choices" vision).
- **Training school**: for young adventurers.
- **Staple crafting locations**: smithy, alchemist shop, woodworking/carpentry shop, and likely
  others not yet enumerated.
- **Temple**: for priestly types.
- **Dark temple (hidden)**: a hard-to-find location for cultists and necromancers. Likely situated
  in or near the mine. One of the town's homes has a hidden trap door leading to a tunnel that
  connects to the mine and onward to the dark temple.
  - **Connects to Class Tree Inventory.md**: the Common specializations "Cultist" (unlocked by
    killing an innocent NPC) and "Necromancer" (unlocked by resurrecting a body at `{EVIL THING}`
    using `HAND`) both need an in-world location for their trigger action — the dark temple is a
    strong candidate for that "evil thing" site.
- **Public services**: a small team (a couple of water elementalists) clean waste and maintain the
  sewer system, protected by a warrior- or rogue-type guard against monsters that spawn in the
  sewers. Likely located near the river side of town, given the water tower/sewage connection.
  The sewers themselves are effectively a small hazard zone under the town.

## Not Yet Specified (flag for later)

- Exact building count, positions, and a concrete map layout (this is what the "generate once"
  pass needs to actually produce).
- Names for the town, the kingdom, and any NPCs.
- Whether this is the only town in the first version, or the first of several.
- Guard numbers/patrol patterns, and whether sewer monsters are a standing hazard or an
  occasional event.
- Whether NPCs have schedules/needs simulation from the start, or start simplified/static and
  grow richer over time (recommended: start simplified, consistent with the "prove one loop
  before adding breadth" approach used throughout this project so far).

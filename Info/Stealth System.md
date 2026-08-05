# Stealth System — Sneaking, Noise, and Awareness

Design reference, authored 2026-08-04 (owner request). Dev notes with code and parameters:
[Planning/11 - Stealth Noise and Awareness.md](Planning/11%20-%20Stealth%20Noise%20and%20Awareness.md).

## The idea

Three interlocking mechanics:

1. **Sneaking** — a movement mode the player chooses. Slower, quieter, harder to see.
2. **Noise** — everything you do makes sound with a radius: footsteps, armor, attacks, spells,
   doors, mining. Sound is how you get noticed when you can't be seen — and how enemies get
   noticed by *you*.
3. **Hidden perception** — every enemy and NPC continuously perceives (sight + hearing) against
   your stealth, but the rolls are invisible. The player sees *states* (calm / suspicious /
   alerted), never numbers.

## Stats (per Game Idea.md — these hooks already exist in the stat design)

- **Agility → Stealth.** Game Idea.md lists "Stealth Modifier" under Agility. Higher Agility =
  quieter steps and slower visual detection while sneaking.
- **Wisdom → Perception.** Already drives trap-spotting (`PerceptionService`); the same stat
  drives how quickly a creature notices *you*. High-Wisdom enemies (Priests, Mages) are the
  dangerous ones to sneak past; a dull brute barely looks up.
- **Gear weight → noise.** Heavy attribute gear rattles (louder steps), Light gear is quiet.
  When armor lands as equipment, armor weight becomes the dominant term.

## How detection works (player-facing rules)

- Creatures have a **vision cone** in the direction they face, plus **hearing** all around.
  You can approach from behind — but your footsteps still carry, so *walking* up behind
  someone alerts them anyway. Sneaking is what makes the back approach real.
- Being seen fills an invisible awareness meter: faster when you're close, in the open, in
  their cone; slower when sneaking, far, or high-Agility. Full meter = combat.
- Sounds raise suspicion and give away a *location*: a suspicious creature walks to where it
  heard you and looks around before giving up.
- **States the player can read:** nothing (unaware) → **?** (suspicious, investigating) →
  **!** (alerted, combat). Plus a HUD eye icon showing your own hidden/spotted status.
- Bumping into a creature (≤1.5 tiles) is always instant detection.

## Payoffs

- **Stealth strike:** attacking an unaware target counts as a backstab (auto-positional bonus)
  — the Rogue identity payoff, and it composes with the note-07 backstab multiplier. The noise
  of the strike then wakes the room.
- **Avoidance play:** slipping past a room pack you can't beat — meaningful now that floors
  have rooms/doors and stairs have no key gate (reaching them *is* the goal).
- **Town futures:** guard perception at night, theft, trespass, the hidden trapdoor and dark
  temple infiltration, and the Thief/Assassin class-unlock actions all ride this system.
- **Dilation synergy:** a high-Agility sneak build under time dilation is the ghost fantasy —
  intended, not an exploit.

## Noise sources (design intent; numbers live in the dev note)

Loud → quiet: explosions/mining > spellcasting > melee swings > door bumps > walking >
bow shots > sneaking. Walls muffle sound. Chest-opening channels make noise for their whole
duration (opening a chest next to a sleeping room pack is a choice).

## Deliberate constraints

- Non-sneaking play must feel like today: cone + hearing together ≈ the current 360° detection
  when you're walking normally. Stealth is opt-in depth, not a new tax on normal play.
- Light model status (2026-08-05): town night lighting v1 exists — real darkness with street
  lamps, carried torches, D&D-mapped racial darkvision, and the Night Sight skill (see the
  Implementation Plan log entry). It is *render-only* for now: NPC/enemy sight reduction at
  night, dungeon darkness beyond fog, and light-based stealth checks hook in with this system's
  awareness work.
- Auto-play mode ignores sneaking in v1.

## Open questions (owner)

1. Sneak input: toggle (recommended, matches M/mode conventions) vs. hold-to-sneak?
2. Should enemies sneak too (Rogue-class enemies ambushing *you*) — v2 candidate?
3. Stealth-strike bonus size: flat auto-crit, or full backstab multiplier stack?

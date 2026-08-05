# 17 — Offscreen Events: the world moves without you (PR 9c)

**Owner rulings 2026-08-05:** in this milestone, after the NPC PRs. Ambition is the full catalog — *"invasion, cult uprising, plague, famine, flood, dragon attacks, EVERYTHING"* — delivered as a **framework + staged content**: the engine ships with wave 1, and every later event is a data entry, not a code change. **NPCs can absolutely die from offscreen events — "death makes it real."** No plot armor, not even occupation-critical NPCs.

## Framework

```csharp
// Core/Systems/EventScheduler.cs — ticked hourly (game time) by GameState
// Events defined in Data/Events/events.json:
{ "id": "sewer-outbreak", "weight": 6, "minDay": 3, "cooldownDays": 5,
  "prereqs": ["town-has-sewer-grate"],
  "durationHours": 18,
  "phases": [ "brewing", "active", "resolved" ] }
```

- Roll cadence: one weighted event roll per game day (hour randomized), frequency × the Hostility profile (note 12's table already reserves the row). Cooldowns + prereqs keep it from stacking absurdities; concurrent events cap 2.
- **Dual resolution — the DF trick, cheap:**
  - **Player absent** (in the dungeon / far away): abstract resolution — dice vs. town stats (guard strength = live guard count × level, wall integrity, population). Outcomes: casualties (real NPC ids — they die per the ruling), injuries (schedule-replacing `Recovering` goal for N days), property flags, economy effects (stall closed, prices up).
  - **Player present**: the event is concrete — real spawns, real fights (dungeon-break monsters at the gate via `CreatureFactory`/cursed-race warband, guards actually fighting alongside whatever the player does). Presence should *matter*: a defended town takes fewer losses than the abstract roll would have dealt.
- Everything lands in `WorldDelta.eventLog` + a **"While you were below…"** summary (message log on surfacing + the notice board carries lingering effects). Events the player never learns details of stay terse — fog of war over history is flavor, not a bug.

## Wave 1 (ships with the framework)

| Event | Effect |
|---|---|
| **Caravan arrival** | Merchant restock + rotating exotic stock for 2 days; a good-news event so the scheduler isn't a misery engine |
| **Sewer outbreak** | Vermin near the grate; water elementalists injured or dead (roll); grate area hazardous until guards or the player clear it |
| **Dungeon break (small)** | Monsters spill from the entrance; guards fight at the guard station; abstract = casualties vs. guard strength, present = real defense fight |

## Wave 2 catalog (data entries against the same framework — sized for the events content pass)

**Invasion** (warband from the wilds; wall/gate stats matter, big casualty potential), **cult uprising** (dark-temple cells act openly; ties to the Cultist/dark-temple content when it lands), **plague** (spreads by household; temple healing demand spikes; quarantine flavor), **famine** (harvest/caravan failures compound; food prices spike — needs system makes it *personal*), **flood** (river bursts; bridge/riverside districts damaged; water elementalists shine), **dragon attack** (apex event: rare, min-day high, Hostile-weighted; a creature-system dragon, abstract resolution is *ugly* — the event that makes legacy worlds have scars worth talking about). Each is a JSON def + at most one bespoke effect hook.

## The world heals — succession & arrivals

Deaths are permanent, so roles backfill or the town degrades honestly:
- Vacant occupation → after 3–10 days an **apprentice promotes** (existing resident re-occupations) or a **newcomer arrives** (freshly generated NPC, recorded as an arrival in the delta — towns breathe in as well as out).
- Until filled, the service is genuinely down (no smith = no crafting; the player *feels* the loss — and can partially fill some gaps themselves, which is the emergent-role vision paying off).
- Backfill NPCs know of their predecessor (memorial-bark integration: "Took over when old Brenn died in the flood.").

## TEST_EVENTS

Deterministic-seed scheduler: cadence/cooldown/prereq/cap honored; Hostility scales frequency; absent-resolution kills real NPC ids and writes delta + log; injured NPCs run Recovering schedules then resume; present-mode dungeon break spawns real fight and better outcomes than abstract control; caravan restocks merchant; vacancy → backfill within window, service downtime enforced; "while you were below" summary renders. Full regression + a 30-day headless world soak (no event deadlocks, population never hits zero on Normal).

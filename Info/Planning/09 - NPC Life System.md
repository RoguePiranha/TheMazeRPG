# 09 — NPC Life System: clock, schedules, goals, guards (PR 8, PR 9)

Peaceful NPCs and how they live their lives. **Deliberately schedules-first** (Starting Region.md's own recommendation) — the emergent needs/economy simulation plugs into the same Goal seam later. Today `Enemy` is the only non-hero actor; nothing else exists (audit-verified).

## PR 8 — clock, population, daily life

### 1. World clock

```csharp
// Core/Services/WorldClock.cs — owned by GameState, ticked from Tick()
public class WorldClock
{
    public const float GameMinutesPerRealSecond = 0.8f; // owner ruling 2026-08-05: 15 real-min day + 15 real-min
                                                        // night = 30-min full cycle (1440/1800s); daylight 6:00-18:00
    public long TotalGameMinutes { get; private set; }
    public int Day => (int)(TotalGameMinutes / 1440);
    public float Hour => (TotalGameMinutes % 1440) / 60f;   // 0..24
    public DayPhase Phase => Hour switch {
        >= 6 and < 8   => DayPhase.Morning,
        >= 8 and < 12  => DayPhase.WorkAM,
        >= 12 and < 13 => DayPhase.Midday,
        >= 13 and < 18 => DayPhase.WorkPM,
        >= 18 and < 22 => DayPhase.Evening,
        _              => DayPhase.Night };
    public void Advance(float realSeconds) => /* accumulate, floor to minutes */;
}
```

Clock runs only while the sim runs (pause pauses time); ticks in *world* frame — **never scaled by dilation** (note 06). Persisted in `SaveData` (`TotalGameMinutes`). Dungeon time: the clock keeps running during dives (a dive "costs a day-part" — cheap and consistent; revisit if the owner wants timeless dungeons). HUD: small clock line in town ("Day 3, 14:20").

### 2. Npc actor

```csharp
// Core/Models/Npc.cs — parallel to Enemy, NOT a subclass of it
public class Npc
{
    public string Id, Name, Race, Occupation;
    public AgeBand Age;                       // Child | Adult | Elder
    public int HomeRoomId, WorkRoomId;
    public float X, Y, TargetX, TargetY;
    public float TimeScale = 1f;              // note 05/06 substrate
    public List<StatusEffect> Statuses = new();
    public NpcGoal CurrentGoal;
    public Queue<(int x,int y)> Path = new();
    public bool IsInnocent = true;            // Cultist-trigger bookkeeping
    public int Hp, MaxHp;                     // full character sheet via EnemyFactory-style derivation
    public Enemy? CombatShadow;               // spawned only if this NPC must fight (see PR 9 §5)
}
```

Shared-surface note: rather than a big `Actor` base-class refactor now, extract the *duplicated bits* (`TimeScale` recompute, status list processing, grid position) into small helpers that take the common fields — full unification is a later cleanup once three consumers exist and the shape is proven.

Stats derive through the existing `EnemyFactory` math (class starting stats + growth × race effectiveness — occupations map to classes: Smith→Warrior-ish, Priest→Priest, Guard→Warrior/Rogue, Elementalist→Mage Apprentice, others→Wanderer, level 1–4 by age). Population loads from the frozen town JSON's `npcs` array (note 08).

### 3. Schedule → Goal → Path/Activity

```csharp
public enum NpcGoalType { Sleep, Work, Meal, Socialize, Patrol, School, Flee, RespondToCrime }

// Occupation → phase table (data, one static dictionary; per-NPC jitter ±20 game-min so
// the town doesn't move in lockstep):
Smith:    Morning→Meal(home), WorkAM/PM→Work(smithy), Midday→Meal(square), Evening→Socialize(square), Night→Sleep(home)
Guard:    shifts — half the roster Patrol by day, half by night; off-shift = resident pattern
Child:    Morning→Meal, WorkAM/PM→School(trainingSchool), Evening→Socialize, Night→Sleep
Elder:    home-heavy variant; Merchant: Work(ownStall)…
```

Per-tick driver (NPCs update every other tick like enemy wander — 50 NPCs ≈ 25 updates/tick, trivial):
1. If `Phase` changed → resolve new `NpcGoal` → BFS path (`MovementSystem` path helpers, reuse `FindPath`) to a free tile in the target room; **bump-open unlocked doors** via the note 02 path.
2. Walk the path at `walkSpeed 0.05 × TimeScale`; on arrival run a *stationary loop* (v1 "activity": stand at a work anchor tile, small idle wiggle; sleeping NPCs despawn from render into their dark home — cheapest believable thing).
3. Local avoidance while walking: **context steering, ported from the old Godot prototype** (`Sample Files/Scripts/test_enemy.gd` — the one genuinely good system there):

```csharp
// Core/Systems/SteeringService.cs — used by NPCs now; enemies can adopt later
// 12 sample directions (16 in the original; 12 is plenty at tile scale)
weight[i] = max(0, dot(dir[i], toWaypoint))            // goal alignment
          * (WalkableAhead(pos, dir[i], 0.8f) ? 1 : 0) // wall check ~1 tile (replaces raycast)
          * (NeighborWithin(pos + dir[i]*0.5f, 0.4f) ? 0.5f : 1f);   // separation halving
move along argmax; if all zero → stand one tick (someone's in the doorway; BFS handles rerouting)
```

### 4. Rendering + interaction v1

- Render: small circle in occupation color + name label within 2 tiles; inside undiscovered buildings → hidden (room-visibility from note 08).
- `E` on an NPC (extend `NearbyInteractable` to include NPCs): **Talk** → one bark line from `Data/NPCs/barks.json` keyed `(occupation, phase)` ("Ore's been good this week." / night: "Shouldn't you be abed?"). No dialogue trees v1.
- **Memorial barks (owner ruling 2026-08-05):** NPCs holding a memory of a fallen hero (note 12 `WorldDelta.NpcMemories`) occasionally swap in a memorial line templated by relationship kind + death cause + the hero's record — the legacy-world ruling made audible. Every interaction choke point here (talk/trade/train/heal) writes the `npcId → hero` interaction set that feeds those memories.

### TEST_TOWNLIFE (PR 8 exit)

Headless 3 game days: every NPC in home room during Night and work room during Work phases (sampled hourly); 100% goal arrivals within a path-budget (no stuck NPC — assert max consecutive stand ticks < threshold); clock persists through save/load; determinism per seed.

## PR 9 — services and consequences

### 4.4 Working POIs

- **Merchants**: 2–3 stall NPCs with buy inventories (rotating stock from `CombinableCatalog`/`spells.json`, priced `RarityPoints × 10 × 1.5` buy vs. existing ×10 sell — first gold *sink*). Sell flow unchanged. `Data/NPCs/merchant_stock.json`. **Charisma finally wired to prices** (owner ruling 2026-08-05, closing Game Idea.md's Cha merchant-price role): `buy × (1 − 0.015·effCha − 0.001·Opinion)` floored at 0.75; `sell × (1 + 0.01·effCha)` capped at 1.25.
- **Relationships v1 — per-NPC `Opinion` (owner ruling 2026-08-05: "interactions go more smoothly, people remember you more and grow to care about you quicker").** `Opinion` 0–100 per NPC (strangers start ~10), raised by interactions — first talk of the day +1, trade +2, training +3, being healed +2 — with growth `× (1 + effCha × 0.05)`: Charisma is the *rate* of becoming known and liked, not a popularity score. No decay in v1. Tiers gate flavor: Stranger <20, Acquaintance <50, Friend <80, Close ≥80 — bark pools warm by tier, merchants shave prices (the Opinion term above), and **memory formation for the legacy system keys off Acquaintance+** (note 12): higher Cha → more people carry your memory when you're gone. This is deliberately the per-NPC seed of the parked full reputation/factions system — same field, wider consumers later.
- **Trainer** (training school): teaches Tier-0/1 spells (note 07 §3) for gold, gated `AffinityService.CanLearn(hero.Affinities.Get(spell.Element), spell.Tier)` — **the dormant tier gate's first real consumer** (AffinityService.cs:69-79). UI: list with affordable/gated states + reasons ("requires Fire affinity 45").
- **Priest** (temple): full heal + cleanse statuses for `10 + 2×level` gold.

### 4.5 Guards and crime

- Patrol goal: waypoint ring (west gate → square → dungeon entrance → east gate) walked on shift, steering per §3.
- **Crime v1 = assault**: hero attack damaging an NPC sets `TownAlert` (60 world-min decay): victim + nearby civilians get `Flee` (goal override: run to home/nearest building, bump doors); on-shift guards get `RespondToCrime` → converge on last-known hero position.
- **NPC combat via `CombatShadow`**: guards that engage spawn a real `Enemy` (Warrior/Rogue, level 4–6, from `EnemyFactory`) bound to the NPC — reusing the entire existing combat path (projectiles, statuses, XP) with zero duplication. Shadow dies → NPC dies.
- **Killing an innocent**: `Hero.InnocentKills++` (persisted; the Class Tree "Cultist" trigger, consumed in note 10); Codex records it; killed NPCs stay dead in the save (`DeadNpcIds` list in `SaveData`) — the town remembers.
- Explicitly deferred: theft, jail, reputation/fines, sewer-monster events, dark temple (needs its own pass).

### TEST_TOWNCRIME (PR 9 exit)

Scripted assault: civilians flee indoors within N ticks; ≥1 on-shift guard reaches the hero; guard shadow fights via normal combat path; innocent-kill increments + persists; alert decays and schedules resume. Plus trainer gating (low-affinity hero refused; post-training spell in inventory), merchant buy/sell gold math, priest heal/cleanse. Full regression + GUI smoke (barks, prompt on NPCs, patrol visible).

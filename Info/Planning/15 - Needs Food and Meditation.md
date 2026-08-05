# 15 — Player Needs, Food, and Meditation (PR 9b)

**Owner rulings 2026-08-05:** hunger + rest as lightweight needs (debuffs, never a death spiral); **walking drains stamina slowly** and requires rest after a while; a **Meditate skill** restores resources faster; food is bought/looted v1, cooking arrives with the consumables/alchemy pass once ingredients are findable.

## Needs model (Hero only — NPC needs stay abstracted in schedules)

```csharp
// Hero: two slow meters, 0..100, ticked by world-clock time (not TimeScale — hunger doesn't pause when you dilate)
public float Satiety = 80f;   // drains ~100 → 0 over ~1.5 game days
public float Rest    = 80f;   // drains ~100 → 0 over ~1 game day awake; dungeon time counts
```

Effect bands (buffs for upkeep, mild penalties for neglect, **never lethal**):

| Band | Satiety / Rest | Effect |
|---|---|---|
| Well-fed / Rested | > 70 | +25% HP/stamina regen (the carrot) |
| Fine | 30–70 | baseline |
| Hungry / Weary | 10–30 | −15% stamina regen; Weary also −10% Awareness gain resistance (you're easier to sneak up on — flavor symmetry) |
| Starving / Exhausted | < 10 | −25% regen, −10% move speed; message-log nag. **No damage, ever.** |

## Travel fatigue (the new stamina rule)

- Out-of-combat movement drains Stamina slowly (`~0.15/s while moving`, dash costs unchanged); standing still regens as today. Long treks now *end* in a meaningful "I need a breather," which Meditate answers.
- Below 20% Stamina: move speed −15% until it recovers (the "require rest" beat).
- Combat stamina economics untouched — this is a travel layer, not a combat nerf. All numbers in one `NeedsConfig` block.

## Food v1

- Food items = consumables (`Item` Combinables with `UseEffect: RestoreSatiety(n)` — the first consumable *use-verb*: hotbar/inventory "Use", eat over a 2s Activity).
- Sources v1: merchant stalls + tavern-shaped vendor sell meals (bread 15, stew 35, feast 60 satiety); loot tables sprinkle rations in dungeon chests (dives are long).
- **Cooking**: with the consumables/alchemy pass — ingredients (creature drops from note 14, forage from open floors) + a fire/kitchen → meals, better satiety per gold. Not this PR; the ingredient drops land now so nothing blocks it.
- Sleeping: at home/inn bed (Activity, fast-forwards the clock to morning, fills Rest; inn costs gold — the inn earns its place in the town roster). Sleeping in the dungeon: no (safe rooms restore via their regen buff; Rest only refills by real sleep or partially by Meditate).

## Meditate (skill, not menu magic)

- A learnable/innate **skill Combinable** ("Meditate") — usable anywhere via hotbar: channels a `MeditateActivity` (interrupted by damage or movement; you are *vulnerable* — awareness gain vs. you doubles while channeling).
- Effect: Stamina/Mana/Faith regen ×4 while channeling; Rest recovers slowly (½ rate of sleep) — the monk-on-the-trail fantasy, not a sleep replacement.
- **Levels with use** through the note 10 skill-XP system (Int-scaled mastery like everything else): levels raise the regen multiplier and reduce the interrupt penalty. Wanderer/Priest start with it; others learn it at the training school or temple (small gold, no affinity gate).

## TEST_NEEDS

Meters drain at configured world-time rates and ignore dilation; band effects apply/remove at exact thresholds and never deal damage; eating restores satiety via the use-verb Activity; travel drain triggers the low-stamina slow and recovers; Meditate ×4 regen, interrupt on hit, skill-XP accrues per note 10 formula; inn sleep advances clock + fills Rest + charges gold; save/load round-trips both meters. Full regression (auto-play demos: needs default OFF in headless tests via config so the 14-suite baseline is untouched — the *game* has them on).

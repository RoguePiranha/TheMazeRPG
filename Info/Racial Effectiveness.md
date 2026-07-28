# Racial Attribute Effectiveness

Canonical design for how race affects stats. Authored by the project owner (2026-07-24).
Implemented in code: `Data/Races/races.json` (multipliers), `Core/Models/CharacterData.cs`
(`CharacterRace.Effectiveness`), `Core/Models/Hero.cs` (`*Effectiveness` + `Effective*`),
`Core/Services/CharacterDataService.cs` (applies effectiveness, does NOT alter base stats),
and the derived formulas in `CombatSystem`, `MovementSystem`, and `GameState`.

Every character has a set of visible base attributes determined by their starting class and
later attribute growth. Starting class distributions contain exactly 28 total attribute points
regardless of the character's race.

Racial modifiers do not change the displayed attribute values or the number of points a
character receives. Instead, each modifier represents the race's effectiveness with that
attribute: how much of the attribute the character can translate into practical results.

## Calculation

```text
Effective Attribute = Base Attribute × Racial Effectiveness
```

For example, every Warrior begins with 8 Strength:

```text
Human Warrior — Base 8 × 1.0  = Effective 8.0
Dwarf Warrior — Base 8 × 1.3  = Effective 10.4
Elf   Warrior — Base 8 × 0.8  = Effective 6.4
```

All three still display 8 Strength and retain the same 28-point class distribution. Their races
determine how effectively that Strength contributes to melee damage, carrying capacity,
knockback, and other Strength-based mechanics.

## Racial Effectiveness Multipliers

| Race       | Strength | Constitution | Agility | Dexterity | Intelligence | Wisdom | Charisma |
| ---------- | -------: | -----------: | ------: | --------: | -----------: | -----: | -------: |
| Human      |      1.0 |          1.0 |     1.0 |       1.0 |          1.0 |    1.0 |      1.0 |
| Elf        |      0.8 |          0.9 |     1.2 |       1.1 |         1.25 |    1.1 |      1.0 |
| Dwarf      |      1.3 |          1.3 |     0.8 |       1.0 |          0.8 |    1.0 |      0.9 |
| Halfling   |      0.6 |          0.8 |     1.3 |       1.2 |          1.0 |    1.0 |      1.2 |
| Goblin     |      0.7 |          0.9 |     1.3 |       1.2 |          0.7 |    0.8 |      0.4 |
| Kobold     |      0.7 |          0.8 |     1.2 |       1.3 |          1.1 |    0.9 |      0.8 |
| Dragonborn |      1.3 |         1.15 |     0.9 |       0.9 |          0.9 |    1.0 |      1.1 |
| Orc        |      1.4 |          1.3 |     1.0 |       0.8 |          0.7 |    0.8 |      0.6 |
| Tiefling   |      0.9 |          0.8 |     1.0 |       1.1 |          1.2 |    0.9 |      1.3 |

Goblins and Orcs are **Cursed Races** and intentionally have lower overall effectiveness than
most other races. They retain strong physical specializations but suffer more severe weaknesses.

## Display and Internal Precision

The character sheet should display the unmodified **base** attribute as the primary value.
Racial effectiveness and the resulting effective attribute should remain visible through
tooltips or expanded stat details.

```text
STRENGTH: 8

Dwarven Strength Effectiveness: 130%
Effective Strength: 10.4

Affects:
- Melee damage
- Carrying capacity
- Knockback
- Strength-based abilities
```

Effective attributes retain their decimal precision internally and are **not** rounded.
Individual derived values may be rounded when they must be displayed or processed as whole numbers.

## Requirements

Class, equipment, spell, and ability requirements normally use the visible **base** attribute:

```text
Mage requires Intelligence 7
Longbow requires Dexterity 6
Heavy Armor requires Constitution 6
```

This allows any race to pursue any ordinary class or build if the player invests the required
attribute points. Racial effectiveness changes how well the resulting character performs, not
whether the build is permitted.

Some exceptional equipment or abilities may require an **effective** attribute instead. These
requirements must be explicitly labeled:

```text
Titan Maul requires 12 Effective Strength
```

## Derived Effects

Game systems calculate the effective attribute once and use that result consistently. Racial
effectiveness must **not** be reapplied inside individual formulas after the effective attribute
has already been calculated.

```text
Melee Damage    = Weapon Damage modified by Effective Strength
Maximum Health  = Base Health   modified by Effective Constitution
Movement Speed  = Base Speed    modified by Effective Agility
```

## Humans

Humans are the baseline race, with `1.0` effectiveness in every attribute. No exceptional
strength, but no ineffective attributes either — every point invested produces full value. This
makes Humans ideal for hybrid classes, unconventional builds, and characters who change focus
mid-run. Other races achieve greater results within their specialties but pay meaningful
penalties when developing outside them.

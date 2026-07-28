# Class Tree Inventory

Source: `Game Class and Stats.drawio.xml`

## Reading Conventions

- `→` means the screenshot shows an intended relationship between consecutive class nodes.
- A connection to another Common class is a specialization unlock, while a connection to a higher rarity is a class evolution.
- Nodes that are blank or repeat the preceding class name in the original diagram are treated as placeholders; approved replacement names are used throughout this inventory.
- Repeated class nodes in the unlockable-starter section are treated as additional starting availability, not as separate classes.
- The screenshot is authoritative for branch structure. Some Draw.io connectors span several tiers as a single line behind intermediate nodes, so the raw XML endpoints do not always identify every class through which the line passes.

## Inventory Summary

- 6 standard starter classes
- 11 action-unlocked Common specializations
- 115 distinct named class concepts after expanding the elemental lines and naming all 18 original placeholders
- 18 formerly unnamed advancement positions now finalized
- 4 intermediate advancement nodes located along progressions whose XML connectors span multiple tiers

## Specialization and Advancement Model

- Rogue, Archer, Warrior, Priest, Mage Apprentice, and Wanderer are the six Common classes initially available at character creation.
- Thief, Duelist, Beast Tamer, Ranger, Paladin, Berserker, Squire, Necromancer, Elementalist, Spellsword, and Monk are Common specializations unlocked through special actions.
- A specialization can only be unlocked while using a compatible parent class or class family. Unlocking one does not permit an unrelated class to pivot into it.
- After unlocking a specialization, the current eligible character may switch to it immediately without losing character level. The player may instead retain the current class.
- An unlocked specialization also becomes available as a Common starting class on later playthroughs.
- Class evolutions become available at levels 10, 25, 50, and 75, corresponding to Uncommon, Rare, Epic, and Legendary.
- Mythical classes are specialized Legendary evolutions with additional requirements.

## Tier and Level Structure

| Tier | Availability | Named nodes currently mapped |
|---|---|---|
| Starter — Common | Character creation | Rogue, Archer, Warrior, Priest, Mage Apprentice, Wanderer |
| Common | Special-action specializations | Thief, Duelist, Ranger, Beast Tamer, Berserker, Squire, Paladin, Necromancer, Elementalist, Spellsword, Monk |
| Uncommon | Level 10 | Assassin, Fencer, Pathfinder, Marksman, Beast Whisperer, Ravager, Gladiator, Man-at-Arms, Crusader, Fallen Paladin, High Priest, Cultist, Grave Warden, Fire Elementalist, Water Elementalist, Earth Elementalist, Air Elementalist, Ice Elementalist, Lightning Elementalist, Mage, Mageblade, Forsaken |
| Rare | Level 25 | Nightblade, Sword Adept, Wildstalker, Sniper, Packmaster, Wrathborn, Champion, Knight, Justicar, Oathbreaker, Saint, Void Acolyte, Soul Reaver, Void Elementalist, Pyromancer, Hydromancer, Geomancer, Aeromancer, Cryomancer, Stormcaller, Arcanist, Arcane Swordsman, Whirlwind Blade, Hero, Shadowbound |
| Epic | Level 50 | Shadow Wraith, Blademaster, Horizon Walker, Deadshot, Beastlord, Juggernaut, Conqueror, Dragon Knight, Templar, Blackguard, Anointed, Dark Prophet, Death Lord, Voidborn, Inferno, Maelstrom, Worldshaper, Skybreaker, Winter's Wrath, Thunderlord, Arcane Ascendant, Mystic Blademaster, Lightning Warden, Doommarked |
| Legendary | Level 75 | Reaper, Swordborn, Worldstrider, Fate's Arrow, Primal Sovereign, Dreadnaught, The Unconquered, Draconic Warlord, Holy Avenger, Lord of Ruin, Avatar of Light, Herald of the Void, Lich King, Aetherlord, The Primordial Flame, The Primordial Tide, The Primordial Stone, The Primordial Sky, The Primordial Frost, The Primordial Storm, Archmage, Arcane Swordlord, Storm Sovereign, Chosen, Harbinger of Doom |
| Mythical | Specialized Legendary evolution | Paragon of Light, Paragon of Darkness |

The tier lists use the approved replacement names for nodes that remain duplicated or blank in the original diagram. Progressions are interpreted from the visible diagram, including long connectors that pass behind intermediate nodes.

## Starter-Class Families

### Rogue — Common Base Class

Rogue is one of the six initially unlocked Common classes. Its family currently contains four branches.

#### Assassin path

```text
Rogue                              Common
  → Assassin                         Uncommon
    → Nightblade                     Rare
      → Shadow Wraith                Epic
        → Reaper                     Legendary
```

Assassin is Rogue's direct Uncommon evolution.

#### Duelist path

```text
Rogue                              Common
  → Duelist                          Common
    → Fencer                         Uncommon
      → Sword Adept                  Rare
        → Blademaster                Epic
          → Swordborn                Legendary
```

Duelist is shared with the Warrior branch.

#### Ranger path

```text
Rogue                              Common
  → Ranger                           Common
    → Pathfinder                     Uncommon
      → Wildstalker                  Rare
        → Horizon Walker             Epic
          → Worldstrider             Legendary
```

Ranger is shared with the Archer branch.

Ranger also branches into Beast Tamer:

```text
Ranger                             Common
  → Beast Tamer                      Common
    → Beast Whisperer                Uncommon
      → Packmaster                   Rare
        → Beastlord                  Epic
          → Primal Sovereign         Legendary
```

#### Thief path

```text
Rogue                              Common
  → Thief                            Common
```

No later Thief specialization is connected yet.

---

### Archer — Common Base Class

Archer is one of the six initially unlocked Common classes. Its family currently contains two branches.

#### Ranger path

```text
Archer                             Common
  → Ranger                           Common
```

From Ranger, the character can continue through Pathfinder or unlock Beast Tamer. Both shared lines are documented under Rogue.

#### Marksman path

```text
Archer                             Common
  → Marksman                         Uncommon
    → Sniper                         Rare
      → Deadshot                     Epic
        → Fate's Arrow               Legendary
```

The screenshot shows Deadshot as the Epic step in the continuous vertical progression. The underlying XML stores the line as a longer connector whose endpoints skip over the Deadshot node.

---

### Warrior — Common Base Class

Warrior is one of the six initially unlocked Common classes. Its family currently contains five branches.

#### Berserker path

```text
Warrior                            Common
  → Berserker                        Common
    → Ravager                         Uncommon
      → Wrathborn                     Rare
        → Juggernaut                 Epic
          → Dreadnaught              Legendary
```

#### Squire and knight path

```text
Warrior                            Common
  → Squire                           Common
    → Man-at-Arms                    Uncommon
      → Knight                       Rare
        → Dragon Knight              Epic
          → Draconic Warlord         Legendary
```

#### Duelist path

```text
Warrior                            Common
  → Duelist                          Common
    → Fencer                         Uncommon
      → Sword Adept                  Rare
        → Blademaster                Epic
          → Swordborn                Legendary
```

Duelist is shared with the Rogue branch.

#### Gladiator path

```text
Warrior                            Common
  → Gladiator                        Uncommon
    → Champion                       Rare
      → Conqueror                     Epic
        → The Unconquered             Legendary
```

Gladiator is Warrior's direct Uncommon evolution.

#### Paladin path

```text
Warrior                            Common
  → Paladin                          Common
```

Paladin is shared with the Priest branch and divides into holy and fallen lines.

Holy line:

```text
Paladin                            Common
  → Crusader                         Uncommon
    → Justicar                       Rare
      → Templar                      Epic
        → Holy Avenger               Legendary
```

Fallen line:

```text
Paladin                            Common
  → Fallen Paladin                   Uncommon
    → Oathbreaker                    Rare
      → Blackguard                   Epic
        → Lord of Ruin               Legendary
```

---

### Priest — Common Base Class

Priest is one of the six initially unlocked Common classes. Its family currently contains four branches.

#### Paladin path

```text
Priest                             Common
  → Paladin                          Common
```

Paladin's holy and fallen lines are documented under Warrior because the nodes are shared.

#### Monk path

```text
Priest                             Common
  → Monk                             Common
```

Monk is shared with Wanderer. No later Monk specialization is connected yet.

#### High Priest path

```text
Priest                             Common
  → High Priest                      Uncommon
    → Saint                          Rare
      → Anointed                     Epic
        → Avatar of Light            Legendary
```

The screenshot shows Anointed as the Epic step in the continuous vertical progression.

#### Cultist path

```text
Priest                             Common
  → Cultist                          Uncommon
    → Void Acolyte                   Rare
      → Dark Prophet                 Epic
        → Herald of the Void         Legendary
```

Cultist is Priest's direct Uncommon evolution.

---

### Mage Apprentice — Common Base Class

Mage Apprentice is one of the six initially unlocked Common classes. Its family currently contains four branches.

#### Mage path

```text
Mage Apprentice                    Common
  → Mage                             Uncommon
    → Arcanist                       Rare
      → Arcane Ascendant             Epic
        → Archmage                   Legendary
```

The Epic node repeats `Arcanist` in the original diagram; its finalized replacement name is Arcane Ascendant.

#### Necromancer path

```text
Mage Apprentice                    Common
  → Necromancer                      Common
    → Grave Warden                   Uncommon
      → Soul Reaver                  Rare
        → Death Lord                 Epic
          → Lich King                Legendary
```

#### Elementalist paths

Elementalist is a Common specialization of Mage Apprentice. To unlock it, a Mage Apprentice must learn two elemental spells before level 5. Unlocking it allows an immediate level-preserving switch and permanently adds Elementalist to the player's available Common starting classes.

The finalized standard elemental lines are:

| Element | Common | Uncommon | Rare | Epic | Legendary |
|---|---|---|---|---|---|
| Fire | Elementalist | Fire Elementalist | Pyromancer | Inferno | The Primordial Flame |
| Water | Elementalist | Water Elementalist | Hydromancer | Maelstrom | The Primordial Tide |
| Earth | Elementalist | Earth Elementalist | Geomancer | Worldshaper | The Primordial Stone |
| Air | Elementalist | Air Elementalist | Aeromancer | Skybreaker | The Primordial Sky |
| Ice | Elementalist | Ice Elementalist | Cryomancer | Winter's Wrath | The Primordial Frost |
| Lightning | Elementalist | Lightning Elementalist | Stormcaller | Thunderlord | The Primordial Storm |

Void line:

```text
Elementalist                       Common
  → Void Elementalist                Rare
    → Voidborn                       Epic
      → Aetherlord                   Legendary
```

The screenshot shows Voidborn as the Epic step in the continuous vertical progression.

#### Spellsword paths

```text
Mage Apprentice                    Common
  → Spellsword                       Common
    → Mageblade                      Uncommon
```

Mageblade divides into two Rare paths.

Arcane blade line:

```text
Mageblade                          Uncommon
  → Arcane Swordsman                 Rare
    → Mystic Blademaster             Epic
      → Arcane Swordlord             Legendary
```

Lightning blade line:

```text
Mageblade                          Uncommon
  → Whirlwind Blade                  Rare
    → Lightning Warden               Epic
      → Storm Sovereign              Legendary
```

---

### Wanderer — Common Base Class

Wanderer is one of the six initially unlocked Common classes. Its family currently contains one Common specialization and two evolution lines.

#### Monk specialization

```text
Wanderer                           Common
  → Monk                             Common
```

Monk is shared with Priest. No later specialization is connected yet.

#### Hero and light path

```text
Wanderer                           Common
  → Hero                             Rare
    → Chosen                         Legendary
      → Paragon of Light             Mythical
```

The diagram associates Hero with a village-defense event:

> Once monster density at the spawn portal reaches a certain point, monsters move as an army toward a village. The Hero must defend the village and keep a required percentage of the population alive.

Wanderer occupies the Common tier. Hero is a special Rare evolution that omits an Uncommon node, and Chosen is its Legendary evolution with no separate Epic node.

#### Forsaken and darkness path

```text
Wanderer                           Common
  → Forsaken                         Uncommon
    → Shadowbound                    Rare
      → Doommarked                   Epic
        → Harbinger of Doom          Legendary
          → Paragon of Darkness      Mythical
```

Shadowbound and Doommarked replace the two blank nodes in the original diagram.

## Shared Specializations

| Common specialization | Compatible starter-class families shown |
|---|---|
| Ranger | Rogue, Archer |
| Duelist | Rogue, Warrior |
| Paladin | Warrior, Priest |
| Monk | Priest, Wanderer |

These are specialization-unlock relationships, not level-based class evolutions. A character must belong to a compatible family and satisfy the specialization's action requirement.

## Unlockable Common Specializations

The full action-unlocked Common specialization pool is:

- Thief
- Duelist
- Beast Tamer
- Ranger
- Paladin
- Berserker
- Squire
- Necromancer
- Elementalist
- Spellsword
- Monk

The diagram's bottom unlockable-starting-class row currently shows only nine of these and omits Berserker and Squire. Once unlocked, all eleven are additional Common character-creation options on later playthroughs.

## Special-Action Unlocks

The screenshot visually associates these actions with their class unlocks:

| Class unlocked | Requirement shown |
|---|---|
| Cultist | Kill an innocent NPC. |
| Necromancer | Resurrect a body at `{EVIL THING}` using `HAND`; the hand becomes a pet. |
| Elementalist | While using Mage Apprentice, learn two elemental spells before level 5. |
| Spellsword | Kill an enemy with a sword. |
| Monk | Punch an enemy to death. |
| Hero | Defend a village from a monster army and preserve a required percentage of its population. |
| Paragon of Light / Paragon of Darkness | Reach level 75 and satisfy additional class-specific requirements. |

## Finalized Placeholder Replacements

| Branch | Finalized advancement names |
|---|---|
| Ranger | Pathfinder — Uncommon; Wildstalker — Rare; Horizon Walker — Epic; Worldstrider — Legendary |
| Beast Tamer | Beast Whisperer — Uncommon; Packmaster — Rare; Beastlord — Epic; Primal Sovereign — Legendary |
| Berserker | Ravager — Uncommon; Wrathborn — Rare |
| Champion | Conqueror — Epic; The Unconquered — Legendary |
| Fallen Paladin | Oathbreaker — Rare; Blackguard — Epic; Lord of Ruin — Legendary |
| Mage/Arcanist | Arcane Ascendant — Epic |
| Forsaken | Shadowbound — Rare; Doommarked — Epic |

All 18 duplicated or blank class positions in the original diagram now have finalized names.

## Long-Connector Progressions

The screenshot confirms four progressions where a single Draw.io connector runs behind an intermediate node instead of terminating and restarting at that node:

| Progression | Intermediate tier |
|---|---|
| Sniper → Deadshot → Fate's Arrow | Deadshot — Epic |
| Saint → Anointed → Avatar of Light | Anointed — Epic |
| Void Elementalist → Voidborn → Aetherlord | Voidborn — Epic |
| Arcanist → Arcane Ascendant → Archmage | Arcane Ascendant — Epic |

These are complete class lines, not unconnected nodes.

## Racial Evolutions Kept Separate

The diagram also includes a racial evolution path:

```text
Goblin
  → Orc
    → High Orc
    → Ogre
```

High Orc and Ogre are treated as species evolutions rather than combat classes and are therefore excluded from the class inventory.

## Decisions Still Needed

1. Add later Thief and Monk evolutions or explicitly define those as shorter class lines.
2. Document the compatible parent class and special-action requirement for every Common specialization.
3. Define the additional conditions for special evolutions that omit an intermediate rarity, particularly Hero and Void Elementalist.

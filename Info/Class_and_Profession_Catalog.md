# Class and Profession Catalog

**Project:** TheMazeRPG  
**Status:** Working content catalog  
**Revision:** August 2, 2026

## 1. Purpose

This document catalogs every class and profession name established, discussed, or reasonably inferred during design work. It translates the older fixed class tree into the current slot-based progression rules defined by `Progression_System_Reference.md`.

It is a content inventory, not a claim that every proposed path must ship in the first playable version.

### Canonical progression rules used here

- Classes represent combat, magic, adventuring methods, and dungeon-party identities.
- Professions represent gathering, crafting, production, scholarship, services, and economic identities.
- Classes and professions occupy separate finite slot pools and use separate XP.
- A path can add a specialization at level 10 without changing its level, XP, slot, base identity, or instance.
- A level-25 path can advance alone or converge with other level-25 paths.
- Advancement creates a new level-1 path and preserves permanent statistics, learned skills, and lineage.
- Ordinary classes combine with classes. Ordinary professions combine with professions.
- A normal class cannot require a profession ID, and a normal profession cannot require a class ID.
- Skills, knowledge, affinities, deeds, environment, teachers, race, titles, affiliations, divine bonds, and transformations can all supply offer evidence.
- Multiple routes may reveal the same class or profession.
- Rarity and level are independent. Every class or profession begins at level 1, can specialize at 10, and normally advances at 25 regardless of rarity.

### Status labels

| Status | Meaning |
|---|---|
| **Established** | Explicitly named or approved in prior design work or the latest user notes. |
| **Legacy-remapped** | A confirmed name from the original tree whose progression mechanics are remapped to the slot system. |
| **Previously proposed** | Already inferred in earlier design work but not explicitly approved. |
| **Newly inferred** | Added here to fill an obvious class, profession, or progression gap. |
| **Retired relationship** | The name may be retained or reassigned, but its original connection is noncanonical. |
| **Non-class state** | A transformation, title, affiliation, or species state that must not occupy an ordinary class slot. |

## 2. Class and profession boundary

The latest content decision places these identities on the following sides of the system:

| Identity | Domain | Reason |
|---|---|---|
| Warrior | Class | Direct combat and party-frontline identity. |
| Healer | Class | Active dungeon support, restoration, stabilization, cleansing, and survival under combat pressure. |
| Alchemist | Class | Uses volatile reactions, bombs, mutagens, transmutation, toxins, and prepared combat formulas as an adventuring method. |
| Mage | Class | Structured spellcasting and magical combat or utility. |
| Priest | Class | Divine authority, miracles, doctrine, sacred support, and divine combat. |
| Miner | Profession | Ore extraction, geology, tunnels, claims, and mine operation. |
| Smith | Profession | Reproducible forging of weapons, armor, tools, and fittings. |
| Apothecary | Profession | Safe preparation, preservation, sale, and administration of remedies, extracts, and compounds. |
| Merchant | Profession | Appraisal, trade, logistics, contracts, shops, and markets. |
| Cook | Profession | Food preparation, preservation, provisioning, and hospitality. |
| Builder | Profession | Construction, repair, demolition, surveying, and structures. |

Two distinctions are important for implementation:

> **Alchemist is not the profession name.** Alchemist is the adventuring class that makes dangerous reactions part of its combat identity. Apothecary is the profession that produces reliable remedies, reagents, and saleable preparations.

> **Healer is not the profession name.** Healer is the active party-support class. Physician is the practical medical profession; Chirurgeon is a likely advanced medical profession.

The systems can still exchange evidence. An Apothecary may naturally acquire reagent knowledge useful for an Alchemist offer, but `Profession == Apothecary` is not required. A Healer may become an excellent Physician more quickly, but the Physician profession must also be reachable through treatment, study, apprenticeship, and practice without the Healer class.

## 3. Primary class identities

### 3.1 Established foundations

| Class | Status | Core identity | Typical offer evidence |
|---|---|---|---|
| **Warrior** | Established foundation | Melee weapons, armor, guards, endurance, and direct battlefield control. | Martial training, surviving close combat, weapon practice, armor use, military teacher. |
| **Archer** | Established foundation | Ranged weapons, aim, ammunition, positioning, and target selection. | Bow or crossbow practice, accurate ranged kills, hunting culture, ranged teacher. |
| **Rogue** | Established foundation | Stealth, opportunism, mobility, traps, precision, and indirect solutions. | Sneaking, bypassing obstacles, ambushes, locks, traps, criminal or scout training. |
| **Priest** | Established foundation | Divine relationship, ritual, miracles, doctrine, and sacred authority. | Divine bond, temple training, fulfilled tenet, granted ability, religious background. |
| **Mage Apprentice** | Established foundation | Basic Mana control, runic literacy, spell construction, and magical experimentation. | Mana affinity, first stable spell, grimoire, academy or mentor, innate casting. |
| **Wanderer** | Established foundation | Exploration, survival, adaptability, travel, discovery, and broad self-reliance. | Journeying, surviving unknown terrain, mapping, foraging, cultural exposure. |

### 3.2 Required primary identities whose exact starting placement remains open

| Class | Status | Core identity | Placement options |
|---|---|---|---|
| **Healer** | Established by latest notes | Nonexclusive party-restoration identity: stabilizing allies, cleansing conditions, repairing bodies, and keeping a group alive. | Offer as a foundation when starting evidence supports it, or as an early class from medical, Life, Restoration, divine, or self-taught routes. It must not require Priest. |
| **Alchemist** | Established by latest notes | Combat chemistry and magical reaction identity: bombs, mutagens, catalysts, toxins, transmutation, and prepared battlefield effects. | Offer as a foundation when starting evidence supports it, or as an early class from reagent use, experimentation, combat preparations, chemistry knowledge, or a teacher. It must not require Apothecary. |
| **Mage** | Established class | Full structured caster beyond apprenticeship. | Canonically the first major single-path advancement of Mage Apprentice; do not create a duplicate foundation unless the starting-background system deliberately skips apprenticeship. |

## 4. Established and legacy-remapped class families

Every arrow in this section represents a normal level-25 advancement unless a specialization or exceptional condition is explicitly shown. The old Common, Uncommon, Rare, Epic, Legendary, and Mythical labels may remain as rarity metadata, but they no longer correspond to global character levels.

### 4.1 Rogue family

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Rogue → Assassin → Nightblade → Shadow Wraith → Reaper** | Legacy-remapped | Repeated ambushes, precision kills, target study, concealment, escape, and increasingly supernatural Shadow or Death practice. Reaper should require a defining legendary hunt or execution, not level alone. |
| **Rogue (Thief)** | Established level-10 specialization | Successful theft, locks, traps, infiltration, fencing stolen goods, or completing an objective without direct combat. The later Thief advancement line remains open. |
| **Rogue 25 + Warrior 25 → Duelist → Fencer → Sword Adept → Blademaster → Swordborn** | Established convergence and legacy line | Light-weapon mastery, one-on-one victories, parries, counters, precision footwork, and defeating a superior opponent in a formal or improvised duel. |
| **Rogue 25 + Archer 25 → Ranger → Pathfinder → Wildstalker → Horizon Walker → Worldstrider** | Established convergence and legacy line | Tracking, stealth at range, wilderness travel, mapping, pursuit, survival, and operating beyond settled roads. The deed determines whether Ranger later reveals Pathfinder, Hunter, or Warden-oriented offers. |
| **Ranger (Beast Tamer) → Beast Whisperer → Packmaster → Beastlord → Primal Sovereign** | Established specialization identity and legacy line | Form a willing companion bond, train without breaking the creature, coordinate multiple beasts, and eventually lead or protect an ecosystem-scale beast domain. |

#### Newly inferred Thief continuation candidates

| Path | Status | Unlock idea |
|---|---|---|
| **Rogue (Thief) → Burglar → Master Thief → Phantom → King of Thieves** | Newly inferred | Escalates from protected-property infiltration to legendary thefts. `King of Thieves` can be a title instead if the class does not gain unique techniques. |
| **Rogue (Thief) → Saboteur → Infiltrator → Ghost Agent → Unseen Hand** | Newly inferred | Emphasizes bypass, sabotage, disguise, intelligence, and changing battles without standing in the front line. |

### 4.2 Archer family

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Archer → Marksman → Sniper → Deadshot → Fate's Arrow** | Legacy-remapped | Accuracy under pressure, range milestones, weak-point hits, patient aim, moving-target shots, and a legendary shot that alters a major event. |
| **Archer 25 + Rogue 25 → Ranger** | Established convergence | Same Ranger line as the Rogue family; both source classes contribute independent level-25 instances. |
| **Archer (Scout) → Forward Scout → Farstrider → Horizon Scout** | Newly inferred | Reconnaissance, early threat detection, route marking, reporting, and returning alive from hostile territory. This is distinct from the Ranger's tracking and wilderness-combat integration. |

### 4.3 Warrior family

#### Established and legacy paths

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Warrior (Berserker) → Ravager → Wrathborn → Juggernaut → Dreadnaught** | Established specialization and legacy line | Continue fighting while injured, use aggression without immediately losing tactical value, break formations, and survive overwhelming force. |
| **Warrior (Squire) → Man-at-Arms → Knight → Dragon Knight → Draconic Warlord** | Established specialization and legacy line | Serve or train under a knight, maintain arms and armor, protect a charge, uphold martial obligations, receive knighthood or demonstrate equivalent capability, and eventually defeat or ally with draconic power. |
| **Warrior → Gladiator → Champion → Conqueror → The Unconquered** | Legacy-remapped | Arena or public combat, repeated victories under varied rules, defeating champions, commanding spectacle, and remaining undefeated through a legendary campaign. |
| **Warrior 25 + Rogue 25 → Duelist** | Established convergence | Same Duelist line as the Rogue family. |
| **Warrior 25 + Priest 25 → Paladin** | Established convergence | Martial competence, a valid divine bond, conduct aligned with the bond, protection of others, and a deity-authorized martial gift. |

#### Paladin branches

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Paladin → Crusader → Justicar → Templar → Holy Avenger** | Legacy-remapped | Fulfill an oath at cost, defend doctrine or people, judge without violating divine terms, lead a sacred campaign, and become an authorized divine weapon. Holy does not mean Light affinity. |
| **Paladin → Fallen Paladin → Oathbreaker → Blackguard → Lord of Ruin** | Legacy-remapped | Break, reject, corrupt, or lose the empowering covenant; then deliberately build a new combat identity around the severed oath. A fall is not caused merely by using Shadow, Death, or Poison. |

#### Warrior specializations and missing party roles

| Specialization or path | Status | Unlock idea |
|---|---|---|
| **Warrior (Swordsman)** | Established in the progression reference | Sustained sword practice, multiple guards and cuts, successful parries, and a defining sword victory. |
| **Warrior (Shieldbearer)** | Previously discussed | Prevent damage to allies, hold a chokepoint, intercept attacks, and maintain guard under pressure. |
| **Warrior (Spearman)** | Previously discussed | Reach control, formation fighting, bracing charges, and consistent spear use. |
| **Warrior (Commander)** | Newly inferred | Direct allies successfully, maintain morale, issue useful orders, and win encounters through positioning rather than personal damage alone. |
| **Warrior (Swordsman) → Swordmaster → Blade Saint → Sword Sovereign** | Newly inferred | A pure weapon-mastery route distinct from the Rogue-derived Duelist line. |
| **Warrior (Shieldbearer) → Guardian → Bulwark → Living Bastion** | Newly inferred | A dedicated tank and protector route based on threat control, interception, formation stability, and surviving focused attacks. |
| **Warrior (Spearman) → Lancer → Dragoon → Sky Lancer** | Newly inferred | Mobile spear combat, mounted or leaping attacks, anti-large-creature technique, and vertical battlefield control. |
| **Warrior (Commander) → Captain → Warlord → Grand Marshal** | Newly inferred | Command increasingly large groups, win through logistics and formation control, and retain loyalty under extreme conditions. `Grand Marshal` may become an affiliation rank if it grants authority without class techniques. |

### 4.4 Priest family

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Priest → High Priest → Saint → Anointed → Avatar of Light** | Legacy-remapped | Deepen divine authority, perform costly miracles, embody a deity's tenets, receive explicit anointing, and act as a stable vessel for divine power. `Avatar of Light` is a legacy name; the deity need not use the Light element. |
| **Priest → Cultist → Void Acolyte → Dark Prophet → Herald of the Void** | Legacy line requiring a content decision | Betray or pervert a divine bond, adopt a forbidden doctrine, gather followers, and channel an alien or corrupted power. If this line uses actual Void, Mana Erosion and forced Void transformation can interrupt it; if it uses a Void-like corrupted deity, it remains an ordinary divine line. |
| **Priest 25 + Warrior 25 → Paladin** | Established convergence | Same Paladin line as Warrior. |
| **Priest 25 + Wanderer 25 → Monk** | Established convergence | Discipline of body and spirit, unarmed combat, meditation, pilgrimage, austerity, and action consistent with an internal doctrine. |
| **Priest + Death practice → Gravekeeper** | Previously inferred | Release trapped spirits, maintain funerary boundaries, oppose uncontrolled undead, and use Death without automatically becoming corrupted. |

### 4.5 Monk family

The original tree stopped at Monk. The following continuations are inferred rather than canon.

| Path | Status | Unlock idea |
|---|---|---|
| **Monk → Disciple → Master → Grandmaster → Enlightened One** | Newly inferred | Perfect forms, discipline resources, teach another monk, overcome spiritual trials, and achieve self-mastery without requiring a deity. |
| **Monk 25 + Mage Apprentice 25 → Mindblade** | Previously proposed convergence | Create force or a weapon through concentration, sustain it during movement, and combine martial forms with stable Mana control. |

### 4.6 Mage Apprentice and Mage family

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Mage Apprentice → Mage → Arcanist → Arcane Ascendant → Archmage** | Established and legacy-remapped | Stabilize complete formulas, demonstrate independent spell construction, research original magic, control dangerous Arcane possibility, and complete a legendary work of spellcraft. |
| **Mage Apprentice (Necromancer) → Grave Warden → Soul Reaver → Death Lord → Lich King** | Established specialization and legacy line | Animate or communicate with remains or spirits, master Death-related schools, bind or protect souls, command undeath, and complete lichdom. Lichdom itself may also need a transformation record. |
| **Mage Apprentice (Elementalist) → source-specific Elementalist lines** | Established specialization framework | Learn and repeatedly use multiple elemental formulas, demonstrate a real source affinity, and complete a source-defining deed. |
| **Warrior 25 + Mage Apprentice 25 → Spellsword → Mageblade → Arcane Swordsman → Mystic Blademaster → Arcane Swordlord** | Established convergence and legacy line | Maintain spell matrices in melee, use a weapon as focus or anchor, alternate or combine strikes and spells, and eventually sever or shape hostile magic with the blade. |
| **Mageblade → Whirlwind Blade → Lightning Warden → Storm Sovereign** | Legacy alternate branch | Integrate Air or Lightning movement with weapon forms; later stages require actual storm competence rather than Lightning alone. |
| **Mage Apprentice (Conjurer) → Spatialist → Riftwalker → Aetherweaver → Aetherlord** | Previously proposed reassignment | Master Conjuration and stable Space formulas, create bounded openings, traverse rifts, weave persistent spatial structures, and control large-scale aetheric topology. Aetherlord is retained from the old tree but removed from Voidborn. |

### 4.7 Wanderer family

| Path | Status | Unlock and advancement evidence |
|---|---|---|
| **Wanderer → Hero → Chosen → Paragon of Light** | Legacy special line | Defend a village from a monster army while preserving the required population, continue choosing costly protection, become selected by a qualifying power or world-state, and complete a Mythical act of preservation. `Light` here is moral symbolism unless the class explicitly gains Light affinity. |
| **Wanderer → Forsaken → Shadowbound → Doommarked → Harbinger of Doom → Paragon of Darkness** | Legacy-remapped | Isolation, abandonment, survival after rejection, a binding Shadow or doom-related event, acceptance or mastery of the mark, and a Mythical act that embodies the route. Shadow is not automatically evil or Void. |
| **Wanderer (Bard)** | Previously proposed specialization | Learn songs or stories from several settlements and successfully perform them before an audience. Bard is not automatically a Sonic caster. |
| **Wanderer 25 + Priest 25 → Monk** | Established convergence | Same Monk route described above. |
| **Ranger 25 + Wanderer 25 → Hunter** | Established recipe example | Track and study a dangerous named creature, prepare terrain and traps, pursue it across regions, and exploit discovered weaknesses. |
| **Ranger 25 + Wanderer 25 → Warden** | Previously inferred alternate result | Defend a wilderness region or community rather than pursuing prey. The same ancestry can reveal Hunter, Pathfinder, or Warden depending on deeds. |

## 5. Healer class family

Healer is an established required class identity, but its exact position and full advancement tree have not been approved. It must remain independently accessible from Priest and Physician.

### 5.1 Multiple unlock routes

| Route | Example evidence |
|---|---|
| Practical | Stabilize wounded allies, triage under threat, stop bleeding, set injuries, and keep a party alive without divine or magical aid. |
| Life magic | Life affinity, Restoration mastery, cleansing formulas, controlled regeneration, and safe biological repair. |
| Divine | Healing gifts granted through a divine bond; Priest can help supply evidence but is not required. |
| Self-taught | Repeated successful treatment, experimentation, anatomical knowledge, and surviving medical emergencies. |
| Teacher or institution | Training from a healer, battlefield infirmary, monastery, academy, or medical guild. |
| Racial or innate | Regenerative or empathic traits plus demonstrated use for the benefit of others. |

### 5.2 Proposed Healer specializations and advances

| Path | Status | Identity and unlock idea |
|---|---|---|
| **Healer (Field Medic) → Battle Medic** | Newly inferred | Fast stabilization while threatened, movement between allies, limited supplies, and treatment without interrupting battle tempo. |
| **Healer (Restorer) → Lifewarden** | Lifewarden previously proposed; route newly inferred | Life and Restoration mastery, regeneration, cleansing, and repairing damage without uncontrolled growth. |
| **Healer (Purifier) → Plaguebreaker** | Newly inferred | Remove poison, disease, curses, hostile enchantments, or corruption; survive and reverse a major outbreak or mass affliction. |
| **Healer (Spirit Mender) → Soulweaver** | Soulweaver previously proposed | Repair identity, memory, possession damage, or spirit-body connections using Spirit knowledge. |
| **Healer + Priest → Miracle Worker** | Newly inferred convergence candidate | Combine repeatable healing competence with authorized divine miracles; must demonstrate both, not merely possess the two classes. |
| **Healer + Mage Apprentice → Vitalist** | Newly inferred convergence candidate | Treat health as a structured magical system, shaping vitality with precise Mana control. |
| **Healer + Alchemist → Combat Chirurgeon** | Newly inferred convergence candidate | Integrate battlefield treatment, reaction-based compounds, emergency mutagens, and controlled surgical techniques. The Physician profession is not required. |

## 6. Alchemist class family

Alchemist is an established required class identity. It is not the Apothecary profession.

### 6.1 Multiple unlock routes

| Route | Example evidence |
|---|---|
| Combat preparation | Defeat threats using bombs, coatings, catalysts, clouds, traps, or reaction chains prepared by the character. |
| Magical research | Stabilize essence reactions, alter spell components with reagents, or create a repeatable transmutation formula. |
| Natural chemistry | Identify reactive materials, safely combine dangerous substances, and reproduce results outside a formal profession. |
| Teacher or text | Learn from an alchemist, laboratory, grimoire, military sapper, or recovered research notes. |
| Cultural or racial | Inherited reagent knowledge plus practical demonstration. |
| Apothecary-aided | Apothecary experience can supply ingredient knowledge and preparation skill, but the profession itself is never a hard requirement. |

### 6.2 Proposed Alchemist specializations and advances

| Path | Status | Identity and unlock idea |
|---|---|---|
| **Alchemist (Bombardier) → Demolitionist** | Newly inferred | Build and deploy explosive, incendiary, concussive, smoke, and terrain-altering mixtures during real encounters. |
| **Alchemist (Mutagenist) → Fleshshaper** | Newly inferred | Create temporary or permanent controlled alterations, survive self-testing, and reverse failed mutations. |
| **Alchemist (Transmuter) → Matter Shaper** | Newly inferred | Change material properties through reagents and structured reactions; demonstrate conservation limits and reproducibility. |
| **Alchemist (Venomist) → Toxicologist** | Toxicologist previously proposed; route newly inferred | Design toxins for particular physiology, manage dose and delivery, bypass resistance, and create antidotes. |
| **Alchemist (Catalyst) → Reaction Master** | Newly inferred | Chain multiple prepared effects, accelerate allied formulas, and control when reactions begin or stop. |
| **Alchemist + Mage Apprentice → Arcane Chemist** | Newly inferred convergence candidate | Treat reagents and spell matrices as one system, replacing brute Mana cost with prepared transformations. |
| **Alchemist + Archer → Grenadier** | Newly inferred convergence candidate | Deliver prepared reactions accurately at range and control blast timing, spread, and terrain. |
| **Alchemist + Rogue → Saboteur** | Newly inferred alternate route | Use delayed reactions, traps, coatings, and structural weak points for infiltration and sabotage. |
| **Alchemist + Healer → Combat Chirurgeon** | Newly inferred convergence candidate | Same integrated treatment route described in the Healer family. |

## 7. Elemental and magical class paths

### 7.1 Established elemental lines

| Source | Progression path | Status | Key unlock evidence |
|---|---|---|---|
| Fire | **Elementalist → Fire Elementalist → Pyromancer → Inferno → The Primordial Flame** | Legacy-remapped | Fire affinity, controlled ignition, sustained heat, environment interaction, and mastery without self-destruction. |
| Water | **Elementalist → Water Elementalist → Hydromancer → Maelstrom → The Primordial Tide** | Legacy-remapped | Pressure, flow, redirection, cleansing, fluid shaping, and large-scale control. |
| Earth | **Elementalist → Earth Elementalist → Geomancer → Worldshaper → The Primordial Stone** | Legacy-remapped | Earth affinity, stone shaping, geological understanding, terrain control, and eventually region-scale structure. Miner can help supply knowledge but is not required. |
| Air | **Elementalist → Air Elementalist → Aeromancer → Skybreaker → The Primordial Sky** | Legacy-remapped | Pressure, lift, wind blades, falling control, flight, weather interaction, and open-space mastery. |
| Ice | **Elementalist → Ice Elementalist → Cryomancer → Winter's Wrath → The Primordial Frost** | Legacy-remapped | Cold, crystallization, preservation, slowing, barriers, and large-scale freeze control. |
| Lightning | **Elementalist → Lightning Elementalist → Stormcaller → Thunderlord → The Primordial Storm** | Legacy-remapped with compound gate | Lightning competence plus Air, Water, Converge, a stabilized Storm compound, and a defining deed in an active storm. A pure-Lightning Rare alternative is still needed. |

### 7.2 Previously proposed primary-essence lines

| Source | Proposed progression | Core distinction |
|---|---|---|
| Poison | **Elementalist → Poison Elementalist → Venomancer → Pestilence → The Primordial Venom** | Toxins, disease, hostile chemistry, stacking afflictions, antidotes, and contamination. |
| Life | **Elementalist → Life Elementalist → Biomancer → Evergrowth → The Primordial Bloom** | Vitality, growth, adaptation, regeneration, and living form; not automatically benevolent. |
| Death | **Elementalist → Death Elementalist → Thanatomancer → Requiem → The Primordial End** | Cessation, decay, vitality loss, passage, and death boundaries; distinct from Necromancy. |
| Light | **Elementalist → Light Elementalist → Luminist → Daybreak → The Primordial Radiance** | Illumination, color, refraction, revelation, and focused radiance; not automatically Holy. |
| Shadow | **Elementalist → Shadow Elementalist → Umbramancer → Nightfall → The Primordial Shadow** | Obscurity, sensory denial, concealment, and the unseen; not automatically evil or Void. |
| Sonic | **Elementalist → Sonic Elementalist → Echomancer → Worldsong → The Primordial Voice** | Vibration, resonance, rhythm, interruption, communication, and material weakness. |

### 7.3 Magical practice and school classes

The ten schools—Evocation, Restoration, Warding, Enhancement, Enchantment, Illusion, Transmutation, Conjuration, Divination, and Affliction—remain skill/mastery axes. They reveal a named class only after becoming a coherent adventuring identity.

| Class | Status | Primary route and defining deed |
|---|---|---|
| **Aegis Mage** | Previously proposed | Mana + Warding; stabilize, reflect, or dispel a dangerous effect while protecting others. |
| **Runewright** | Previously proposed class | Mana + Enchantment; create persistent wards, traps, structures, or magical machinery that function outside direct concentration. |
| **Flamewarden** | Previously proposed | Fire + Warding; defend an area using controlled fire without harming protected allies or assets. |
| **Bastion Shaper** | Previously proposed | Earth + Warding; create terrain, armor, walls, or reinforcement that survives a major assault. |
| **Lifewarden** | Previously proposed | Life + Restoration; repair severe harm while preventing mutation or uncontrolled growth. |
| **Mirage Weaver** | Previously proposed | Light + Illusion; use refraction or projection to hide, reveal, or misdirect at encounter scale. |
| **Veilweaver** | Previously proposed | Shadow + Illusion; deny multiple senses, create false silhouettes, or conceal a group through active opposition. |
| **Toxicologist** | Previously proposed | Poison + Affliction; design and counter targeted toxins. Alchemist is an alternate route. |
| **Resonance Smith** | Previously proposed class name | Sonic + Enchantment; tune objects or structures to store, amplify, suppress, or release vibration. The word `Smith` does not make this the Smith profession. |
| **Reality Shaper** | Previously proposed | Arcane + Transmutation; force unstable possibility into a controlled and persistent change of form. |
| **Warder** | Previously inferred | Broad Warding mastery across several sources; protect, redirect, seal, and dispel rather than specialize by element. |
| **Illusionist** | Previously discussed | Repeated successful sensory constructs, concealment, decoys, and perception manipulation using more than one source. |
| **Enchanter** | Previously discussed, domain placement open | If a class, it focuses on active combat enchantments and allied enhancement. Item production belongs to an Enchanter or Rune-crafting profession. Use distinct definition IDs even if the display name is shared. |
| **Diviner** | Previously inferred | Predict, locate, reveal, or interpret hidden information through stable Divination and verifiable results. |
| **Restorer** | Previously inferred class anchor | Broad magical repair of bodies, objects, enchantments, or patterns. Healer supplies one route but is not mandatory. |
| **Flux Mage** | Previously proposed | Arcane possibility; survive and control intentional state changes rather than merely causing random magic. |
| **Spell Architect** | Previously proposed | Direct Mana, runic literacy, and original multi-part spell structures that remain stable under disruption. |

### 7.4 Compound and derived-force classes

Knowing the component spells is insufficient. These offers require compatible class anchors, component competence, Converge or equivalent structure, successful stabilization, repeated practical use, and a defining deed.

| Class or route | Status | Components and unlock idea |
|---|---|---|
| **Volcanist** | Previously proposed | Fire + Earth; stabilize Lava and use it without losing containment, preferably in a volcanic or forge-scale environment. |
| **Steamwright** | Previously proposed | Fire + Water; control heat, pressure, phase change, and machinery or battlefield steam. |
| **Stormcaller** | Established name, corrected route | Air + Water + Lightning; stabilize Storm and complete a deed in an active storm. |
| **Runeforger** | Previously proposed class | Earth/Metal + Transmutation or Runewright practice; create and fight through magically structured metal. Distinct from the Runesmith cross-system identity. |
| **Crystalwright** | Previously proposed | Earth + Mana; grow or shape Mana-bearing crystal into durable magical structures. |
| **Druid** | Previously proposed | Life + Earth + Water with Wanderer or nature-oriented anchoring; protect, restore, or command a living ecosystem. |
| **Verdant Shaper** | Previously proposed alternate | Same Overgrowth compound, but emphasizes direct plant shaping rather than covenantal nature identity. |
| **Hemomancer** | Previously proposed | Life + Water; manipulate blood with clear physiological limits and survive a defining blood-working. |
| **Bone Shaper** | Previously proposed | Life + Earth or Death applied to remains; create useful, stable bone structures without requiring Necromancy. |
| **Corroder** | Previously proposed | Poison + Water; stabilize Acid and use controlled corrosion against a protected or precise target. |
| **Plaguebringer** | Previously proposed | Poison + Death; survive, create, cure, or deliberately spread a genuine magical plague. Moral state depends on deeds, not the source alone. |
| **Smokeweaver** | Previously proposed | Fire + Air, optionally Shadow; use smoke for movement, concealment, choking, signaling, or battlefield control. |
| **Mirecaller** | Previously proposed | Earth + Water; terrain denial, mud shaping, trapping, and controlled ground collapse. |
| **Sandshaper** | Previously proposed | Earth + Air; erosion, abrasive wind, dunes, glass precursors, and mobile terrain. |
| **Twilight Weaver** | Previously proposed | Light + Shadow; stabilize simultaneous revelation and concealment without treating either as moral alignment. |
| **Rotbringer** | Previously proposed | Death + Poison or Death + Time; accelerate decay, break preservation, and manipulate decomposition. |
| **Graviturge** | Previously proposed | Space structured through Earth or Mana; alter weight, attraction, and spatial relationships without uncontrolled Void exposure. |
| **Dreamwalker** | Previously proposed | Spirit + Illusion or Arcane + Illusion; enter, navigate, alter, or defend dreams and return with verifiable knowledge. |
| **Chronomancer** | Previously proposed | Arcane + Void; stabilize Time, demonstrate acceleration, delay, echo, or stasis, and avoid treating Time as a simple rewind. |
| **Creation Mage** | Previously proposed | Mana + Arcane; reconstruct something ordinary Restoration cannot repair using a surviving identity, rune, temporal, or structural pattern. |
| **Spiritcaller** | Previously proposed | Mana + Arcane arranged as identity and agency; communicate with or manifest spirits while preserving their identity. |
| **Soulweaver** | Previously proposed | Spirit-focused repair, binding, or reconstruction of identity; can be reached through Healer, Priest, or Conjuration evidence. |
| **Spatialist → Riftwalker → Aetherweaver → Aetherlord** | Previously proposed route using a retained legacy name | Mana + Void structured as Space; increasingly complex openings, travel, persistent spatial structures, and large-scale control. |

## 8. Class convergence catalog

All normal sources in these recipes are independent level-25 class instances. Repeated ingredients require separately developed slots. Specializations, deeds, and capability evidence alter which result is offered.

### 8.1 Established or already documented convergences

| Sources | Result | Status | Defining integration and deed |
|---|---|---|---|
| Rogue + Archer | **Ranger** | Established | Combine stealth, tracking, range, and wilderness operation. |
| Rogue + Warrior | **Duelist** | Established | Integrate precision, mobility, counters, and direct martial technique. |
| Warrior + Priest | **Paladin** | Established | Combine martial competence with an active divine covenant and authorized power. |
| Priest + Wanderer | **Monk** | Established | Integrate bodily discipline, spiritual practice, pilgrimage, and self-mastery. |
| Warrior + Mage Apprentice | **Spellsword** | Established | Sustain spell matrices in melee and deliver or shape spells through a weapon. |
| Ranger + Wanderer | **Hunter** | Established recipe example | Track, study, trap, pursue, and defeat a dangerous named quarry. |
| Warrior + Priest + Mage Apprentice | **Mystic Knight** | Established recipe example | Integrate martial, divine, and structured arcane practice in one encounter role. |
| Spellsword + Priest | **Mystic Knight** | Established alternate route | Demonstrate the same three-family identity through condensed lineage. |
| Bard + Warrior + Mage Apprentice | **Bladesinger** | Established recipe example from discussion | Maintain Sonic or Enhancement magic while alternating weapon strikes and spells. |
| Wanderer ×2 + Priest | **Sage** | Established weighted recipe example | Complete major journeys, acquire broad worldly knowledge, and demonstrate spiritual wisdom. |

### 8.2 Previously proposed weighted and specialized convergences

| Sources | Result | Identity and unlock idea |
|---|---|---|
| Ranger + Priest | **Witch Hunter** | Track curses, spirits, corrupted creatures, and forbidden magic; resolve a case through investigation and pursuit. |
| Ranger + Mage Apprentice | **Arcane Archer** | Build spell structures into ammunition and land controlled magical shots under pressure. |
| Duelist + Mage Apprentice | **Bladeweaver** | Use exact sword forms to shape, redirect, interrupt, or sever spells. |
| Warrior ×2 + Mage Apprentice | **Arcane Knight** | Warrior-dominant armored class using magic primarily for reinforcement and defense. |
| Warrior + Mage Apprentice ×2 | **Swordmage** | Mage-dominant caster using a weapon as focus and delivery mechanism. |
| Thief + Mage Apprentice | **Spellthief** | Steal charges, active enchantments, runes, spell access, or magical effects. |
| Rogue ×2 + Mage Apprentice | **Arcane Trickster** | Use limited magic principally for infiltration, deception, preparation, and escape. |
| Berserker + Priest | **Zealot** | Convert devotion, pain, sacrifice, or divine fervor into controlled combat power. |
| Necromancer + Warrior | **Death Knight** | Combine armor and weapon mastery with remains, spirits, vitality drain, and Death techniques. |
| Beast Tamer + Mage Apprentice | **Summoner** | Conjure temporary creatures and maintain their structures under combat pressure. |
| Beast Tamer + Elementalist | **Elemental Binder** | Form persistent bonds with elementals or compound-essence creatures. |
| Monk + Mage Apprentice | **Mindblade** | Create weapons or force through concentration, movement, and Mana control. |
| Squire + Wanderer | **Knight-Errant** | Operate as a self-sufficient traveling knight without a permanent lord or base. |
| Wanderer + Priest ×2 | **Oracle** | Emphasize revelation, prophecy, omens, and divine Divination over travel. |
| Wanderer ×2 + Mage Apprentice | **Loremaster** | Find, decipher, preserve, and use lost spells, ruins, histories, and languages. |

### 8.3 Bard-derived convergences

| Sources | Result | Identity and unlock idea |
|---|---|---|
| Bard + Warrior | **Skald** | Martial inspiration, battle rhythm, morale, and intimidation. |
| Bard + Mage Apprentice | **Spellsinger** | Use voice, rhythm, and Sonic structures to cast without making all Bards magical. |
| Bard + Priest | **Cantor** | Channel divine gifts through authorized hymns, liturgy, and ritual music. |
| Bard + Rogue | **Charlatan** | Social infiltration, impersonation, distraction, false identity, and misdirection. |
| Bard + Spellsword | **Bladesinger** | Condensed alternate route to the established three-path convergence. |
| Bard ×2 + Warrior | **War Chanter** | Bard-dominant battlefield control and group enhancement. |
| Bard + Warrior ×2 | **Battle Herald** | Warrior-dominant command through voice, presence, rhythm, and morale. |

## 9. Profession foundations and families

Profession offers should examine productive evidence rather than require a class. Every specialization shown in parentheses is a level-10 layer. Every arrow is a proposed level-25 advancement unless otherwise stated.

### 9.1 Core required profession identities

| Profession | Status | Level-10 specialization ideas | Level-25 advancement or convergence ideas | Typical offer evidence |
|---|---|---|---|---|
| **Miner** | Established | Prospector, Quarryman, Gem Miner, Delver | Deep Prospector, Master Miner; Miner + Smith → Metallurgist; Miner + Builder → Tunnelwright | Extract ore or stone, identify deposits, brace tunnels, manage ventilation, train with a guild, or operate a mine. |
| **Smith** | Established | Weaponsmith, Armorsmith, Toolsmith, Farrier | Forge Master; Smith + Miner → Metallurgist; Smith + Engineer → Machinist | Forge usable items, control heat, work several metals, repair equipment, and meet quality tolerances. |
| **Apothecary** | Established by latest notes | Herbalist, Compounder, Poisoner, Remedy Maker | Master Apothecary, Pharmacist; Apothecary + Physician → Chirurgeon or Plague Doctor candidate | Prepare stable remedies, extracts, antidotes, salves, reagents, dosage instructions, and maintain ingredients. |
| **Merchant** | Established | Appraiser, Peddler, Shopkeeper, Broker | Factor, Magnate; Merchant + Teamster → Caravan Master; Merchant + Cook → Innkeeper | Complete trades, appraise goods, understand markets, keep accounts, manage inventory, negotiate contracts. |
| **Cook** | Established | Baker, Brewer, Butcher, Preserver | Chef, Master Chef; Cook + Merchant → Innkeeper; Cook + Farmer → Provisioner | Prepare safe meals, preserve food, manage a kitchen, feed groups, discover recipes, and use scarce ingredients efficiently. |
| **Builder** | Established | Carpenter, Mason, Surveyor, Demolitionist | Architect, Master Builder; Builder + Miner → Tunnelwright; Builder + Engineer → Civil Engineer | Build, repair, dismantle, survey, plan supports, estimate materials, and complete structures that survive use. |

### 9.2 Additional established or previously discussed professions

| Profession | Status | Specializations and likely advances | Typical offer evidence |
|---|---|---|---|
| **Scholar** | Established in progression reference | Historian, Linguist, Naturalist, Arcanologist; Archivist, Polymath | Study, verify sources, preserve knowledge, translate, teach, publish findings, or solve research problems. |
| **Farmer** | Established in progression reference | Grower, Rancher, Orchardist, Beekeeper; Agronomist, Estate Farmer | Cultivate crops, manage soil and water, breed animals, protect harvests, and sustain production through seasons. |
| **Engineer** | Established in progression reference | Mechanist, Artificer, Siege Engineer, Civil Engineer; Master Engineer | Design mechanisms, calculate loads, build prototypes, diagnose failures, and create repeatable technical solutions. |
| **Physician** | Established here to preserve Healer as a class | Surgeon, Midwife, Veterinarian, Diagnostician; Chirurgeon, Master Physician | Diagnose and treat patients, perform procedures, study anatomy, maintain records, prevent infection, and achieve repeatable outcomes. |
| **Metallurgist** | Established profession convergence example | Alloy Specialist, Smelter, Metal Assayer; Master Metallurgist | Normally Miner 25 + Smith 25, but alternate routes may use equivalent ore, furnace, alloy, and testing knowledge. |
| **Prospector** | Established Miner specialization | Deep Prospector as single-path advancement | Discover and correctly evaluate a valuable deposit using geological evidence rather than luck alone. |
| **Weaponsmith** | Established Smith specialization | Master Weaponsmith or Forge Master | Forge, balance, harden, repair, and test weapons across several forms and materials. |
| **Deep Prospector** | Established advancement example | Further advanced mine-finder or Deepseer profession path remains open | Locate deposits in dangerous deep environments and return with a usable survey. `Deepseer` must remain distinct from magical Geomancer unless deliberately cross-system. |

### 9.3 Newly inferred gathering and resource professions

| Profession | Status | Specializations and advances | Typical offer evidence |
|---|---|---|---|
| **Forester** | Newly inferred | Logger, Arborist, Charcoal Burner; Master Forester | Identify, harvest, replant, manage woodland, prevent fire, and choose suitable timber. |
| **Fisher** | Newly inferred | Angler, Netter, River Fisher, Deepwater Fisher; Master Fisher | Catch, clean, preserve, breed, or manage fish stocks in varied waters. |
| **Forager** | Newly inferred | Herbal Gatherer, Mushroom Hunter, Scavenger; Master Forager | Reliably identify and collect safe, useful wild materials without exhausting the source. |
| **Trapper** | Newly inferred | Snare Setter, Fur Trapper, Pest Controller; Master Trapper | Build humane or lethal traps, read animal movement, maintain lines, and process catches. This does not require the Hunter class. |
| **Animal Handler** | Newly inferred | Trainer, Breeder, Stablemaster, Drover; Master Handler | Care for, train, breed, transport, and safely manage domestic or working creatures. This does not require Beast Tamer. |
| **Salvager** | Newly inferred | Ruin Picker, Wreck Diver, Reclaimer; Master Reclaimer | Recover useful material from ruins, battlefields, machines, wrecks, or demolished structures while preserving value. |

### 9.4 Newly inferred crafting professions

| Profession | Status | Specializations and advances | Typical offer evidence |
|---|---|---|---|
| **Woodworker** | Newly inferred | Carpenter, Bowyer, Fletcher, Cooper; Master Woodwright | Select, season, join, shape, repair, and finish wood into reliable goods. Carpenter may also be a Builder specialization; shared names use distinct IDs. |
| **Leatherworker** | Newly inferred | Tanner, Saddler, Cobbler, Armorer; Master Leatherworker | Cure hides, cut and stitch leather, create durable fitted goods, and repair wear. |
| **Tailor** | Newly inferred | Weaver, Dyer, Clothier, Embroiderer; Master Tailor | Produce cloth, fit garments, dye consistently, reinforce seams, and work specialized fibers. |
| **Jeweler** | Newly inferred | Gemcutter, Goldsmith, Silversmith, Appraiser; Master Jeweler | Cut stones, work precious metals, set gems, verify purity, and create fine durable pieces. |
| **Potter** | Newly inferred | Tilemaker, Ceramist, Kiln Master; Master Potter | Prepare clay, form vessels or tiles, control firing, glaze, and produce repeatable heat-safe goods. |
| **Glassworker** | Newly inferred | Glassblower, Lens Grinder, Glazier; Master Glasswright | Control furnaces, form glass, grind lenses, glaze structures, and reduce flaws. |
| **Scribe** | Newly inferred | Copyist, Calligrapher, Cartographer, Runescribe; Archivist or Master Scribe | Produce accurate durable records, maps, diagrams, contracts, books, or inscriptions. |
| **Enchanter** | Previously discussed as a productive identity | Rune Engraver, Ward Crafter, Focus Maker; Master Enchanter | Create repeatable enchanted goods using learned enchanting skills, external tools, teachers, or innate capability. No class ID is required. |
| **Artificer** | Previously inferred as a profession type | Mechanist, Focuswright, Device Maker; Master Artificer | Build magical or technical devices whose effects persist outside the creator's direct casting. |

### 9.5 Newly inferred trade, service, and knowledge professions

| Profession | Status | Specializations and advances | Typical offer evidence |
|---|---|---|---|
| **Teamster** | Newly inferred | Wagoner, Courier, Caravaner; Caravan Master | Move people and cargo safely, care for draft animals, plan routes, and complete deliveries. |
| **Sailor** | Newly inferred | Deckhand, Navigator, Pilot; Shipmaster | Operate vessels, navigate, manage weather and cargo, repair at sea, and lead a crew. |
| **Performer** | Newly inferred | Musician, Actor, Storyteller, Dancer; Master Performer | Learn and present works, hold an audience, travel circuits, and earn income or reputation through performance. It can help reveal Bard but is not required. |
| **Innkeeper** | Newly inferred convergence | Hospitality, Tavernkeeper, Lodgemaster | Likely Merchant + Cook or equivalent hospitality, accounting, provisioning, and guest-service evidence. |
| **Steward** | Newly inferred | Quartermaster, Treasurer, Estate Manager; Administrator | Manage supplies, labor, records, budgets, and property with measurable reliability. |
| **Appraiser** | Previously implied by Merchant | Goods Appraiser, Gem Assayer, Relic Evaluator; Master Appraiser | Correctly identify quality, provenance, condition, risk, and market value. |
| **Cartographer** | Previously implied by exploration and Scribe | Surveyor, Navigator, Atlas Maker; Master Cartographer | Measure, map, annotate, verify, and distribute usable geographic information. |
| **Undertaker** | Newly inferred | Embalmer, Grave Tender, Mortician; Funeral Master | Prepare remains, conduct funerary practice, maintain graves, prevent disease, and preserve legal or cultural records. It can supply Death knowledge without requiring Necromancer. |
| **Teacher** | Newly inferred | Tutor, Drillmaster, Lecturer; Master Teacher | Successfully transfer skills or knowledge, adapt instruction, assess learners, and produce repeatable student improvement. |

## 10. Profession convergence ideas

These are profession-to-profession recipes. None requires a class ID, although class-derived skills may supply alternate evidence.

| Sources | Result | Status | Integrated identity |
|---|---|---|---|
| Miner + Smith | **Metallurgist** | Established | Ore identification, smelting, alloy design, heat treatment, and material testing. |
| Miner + Builder | **Tunnelwright** | Newly inferred | Excavation, bracing, drainage, ventilation, underground planning, and safe demolition. |
| Miner + Jeweler | **Gemologist** | Newly inferred | Deposit knowledge, extraction, cutting, grading, and gem-market expertise. |
| Smith + Engineer | **Machinist** | Newly inferred | Precision metal parts, mechanisms, tolerances, repair, and repeatable machines. |
| Smith + Jeweler | **Master Goldsmith** | Newly inferred | Fine metalwork, alloys, settings, ornament, and high-value commissions. |
| Builder + Engineer | **Civil Engineer** | Newly inferred | Loads, foundations, roads, water, large structures, inspection, and public works. |
| Builder + Woodworker | **Architectural Woodwright** | Newly inferred | Frames, roofs, bridges, joinery, plans, and large timber structures. |
| Builder + Salvager | **Reconstructionist** | Newly inferred | Safely dismantle, reclaim, repair, and rebuild from limited materials. |
| Apothecary + Physician | **Chirurgeon** | Newly inferred | Diagnosis, surgery, infection control, dosing, anesthesia, and medical compounds. |
| Apothecary + Forager | **Herbalist** | Newly inferred as a full advancement route | Identify, cultivate, harvest, preserve, and formulate wild medicinal ingredients. |
| Apothecary + Scholar | **Pharmacologist** | Newly inferred | Controlled trials, dosage, interactions, records, and reproducible compound research. |
| Cook + Farmer | **Provisioner** | Newly inferred | Sustainable food supply, preservation, nutrition, logistics, and feeding groups. |
| Cook + Merchant | **Innkeeper** | Newly inferred | Food, lodging, purchasing, staff, accounts, and reputation. |
| Cook + Apothecary | **Dietitian** | Newly inferred | Food as health support, safe restrictions, recovery diets, and functional preparations. Name may be changed for setting tone. |
| Merchant + Teamster | **Caravan Master** | Newly inferred | Trade, routes, guards, animals, cargo, schedules, contracts, and risk. |
| Merchant + Appraiser | **Factor** | Previously mentioned | Acts as a trusted commercial agent with authority to value, buy, sell, and arrange supply. |
| Scholar + Scribe | **Archivist** | Newly inferred | Acquire, authenticate, preserve, index, copy, and retrieve knowledge. |
| Scholar + Cartographer | **Surveyor** | Newly inferred | Research, measurement, mapping, boundaries, resources, and reliable field reports. |
| Engineer + Artificer | **Master Artificer** | Newly inferred | Integrate mechanical design, persistent magical devices, testing, and production. |
| Sailor + Builder | **Shipwright** | Newly inferred | Vessel design, hull construction, rigging integration, repair, and seaworthiness. |
| Farmer + Animal Handler | **Husbander** | Newly inferred | Breeding, feed, health, work traits, herd management, and sustainable stock. |

## 11. Explicit cross-system identities

These identities deliberately combine class and profession capabilities. They are exceptions and must be marked `CrossSystem`. They should initially be optional synergy tracks or special recipes, not required steps in either parent line. Slot consumption remains a per-recipe design decision and must be shown to the player before acceptance.

| Identity | Status | Class-side evidence | Profession-side evidence | Unlock idea |
|---|---|---|---|---|
| **Runesmith** | Established cross-system example | Runic literacy, Mana shaping, persistent enchantment skill | Smithing, heat treatment, material selection, forged-item quality | Forge a durable item whose rune survives quenching, impact, and repeated use. |
| **Runeforge Knight** | Established cross-system example | Warrior or Spellsword-style combat, armor use, integrated rune activation | Smith or Runesmith-quality forge work | Personally forge rune-bearing equipment and use its integrated effects through a major battle. |
| **Battle Chef** | Established cross-system example | Combat support, timing, buffs, field survival | Cook expertise, provisioning, safe preparation under pressure | Sustain a party through an expedition using meals actively adapted to threats and resource limits. |
| **Siegewright** | Established cross-system example | Command, ranged warfare, demolition, or siege combat | Builder or Engineer design and construction | Design, build, deploy, and successfully operate a siege solution against a defended target. |
| **Soulforger** | Established cross-system example | Spirit, Creation, Death, or soul-structure competence | Smithing or artificing capable of housing persistent patterns | Create an item that safely houses, repairs, or anchors an identity-pattern without enslaving it. |
| **Plague Doctor** | Established cross-system example | Healer, Alchemist, Poison, Death, Affliction, or cleansing competence | Physician or Apothecary practice | Diagnose, contain, treat, and survive a major magical outbreak while producing a repeatable protocol. |
| **Resonance Smith** | Previously proposed; domain can be cross-system | Sonic and Enchantment mastery | Smithing, artificing, or structural tuning | Create and use an object whose tuned vibration stores or redirects a magical effect. |
| **Steamwright** | Previously proposed; cross-system route optional | Fire and Water compound competence | Engineer or Artificer practice | Build a stable pressure device or battlefield engine powered by controlled Steam. |

## 12. Names that must not be implemented as ordinary classes or professions

| Name or category | Correct domain | Reason |
|---|---|---|
| **Void-Touched** | Transformation overlay | Mana is being eroded but the current class still exists. |
| **Voidbound** | Forced transformation/class-state consequence | The Mana matrix is gone and normal paths close; this cannot be freely slotted or removed. |
| **Hollow** | Transitional transformation state | Loss of self and Void Baptism mechanics replace ordinary progression. |
| **First Consumption** | Transformation trigger | Irreversible event, not an identity selected from an offer list. |
| **Void Baptism** | Transformation process | Active resource and body conversion. |
| **Voidborn** | Terminal metaphysical state | Health and Void become one pool; the original legacy Epic-class relationship is retired. |
| **Goblin → Orc → High Orc → Ogre** | Species evolution | Changes race or species, not combat training. |
| **Knight of a named realm** | Usually affiliation/title | Authority or membership is not automatically the trained Knight class. |
| **Guild Merchant** | Affiliation plus profession recognition | Guild status does not replace the Merchant profession. |
| **Founder, Dragonslayer, Oathbreaker as a deed-only label** | Title | A name is a class only if it grants and progresses actual techniques. The established Oathbreaker class remains valid when it has class content. |
| **Vampire, Lycanthrope, Lich body-state, Construct, Stoneborn** | Transformation | These change what the character is and may alter class offers without occupying a normal slot. |

### Retired Void relationship

The original diagram contained:

```text
Elementalist → Void Elementalist → Voidborn → Aetherlord
```

This relationship is noncanonical.

- Void is primordial destruction, not an element.
- `Void Elementalist` is retired unless reused as a historical misconception or NPC label.
- Voidborn belongs to the transformation system.
- Aetherlord is reassigned to the Spatialist/Riftwalker/Aetherweaver path.

## 13. Open content decisions

The following decisions should remain explicit rather than being silently hard-coded:

1. Whether Healer and Alchemist join the six established foundation classes at character creation or are contextually offered early classes.
2. The final level-10 specialization names and level-25 advances for Healer and Alchemist.
3. Full advancement lines for Thief, Monk, Bard, Guardian, Scout, and the new pure weapon specializations.
4. Whether the six proposed Poison, Life, Death, Light, Shadow, and Sonic Elementalist line names are approved.
5. A pure-Lightning alternative for characters who do not qualify for compound Stormcaller.
6. Whether Cultist and Void Acolyte use actual Void or a corrupted divine power that resembles it.
7. Whether Enchanter is reserved as a profession, allowed in both domains with separate IDs, or renamed on one side.
8. Whether Gravekeeper, Restorer, and Spiritcaller are specializations, advancements, or convergences.
9. Final rarity assignments for proposed convergences. Rarity must remain independent of level.
10. Cross-system slot behavior: synergy track, hosted specialization, special slot, or explicit consumption recipe.
11. Which profession foundations are available in the first playable version and which are discovered later through world activity.
12. Whether highly social ranks such as Grand Marshal, King of Thieves, and Guildmaster remain classes or become titles/affiliations.

## 14. Recommended first implementation catalog

The complete catalog is intentionally broad. The first implementation should prove the system with a smaller set that exercises every rule:

### Classes

- Foundations: Warrior, Archer, Rogue, Priest, Mage Apprentice, Wanderer.
- Required new identity tests: Healer and Alchemist.
- Level-10 specializations: Swordsman, Shieldbearer, Thief, Berserker, Squire, Necromancer, Elementalist, Bard.
- Single-path advances: Mage, Assassin, Marksman, High Priest, Gladiator, Hero or Forsaken.
- Two-class convergences: Ranger, Duelist, Paladin, Monk, Spellsword.
- Three-family or recursive convergence: Mystic Knight.
- Transformation override: Void-Touched → Voidbound → Hollow → Voidborn.

### Professions

- Foundations: Miner, Smith, Apothecary, Merchant, Cook, Builder.
- Additional breadth: Scholar, Farmer, Engineer, Physician.
- Level-10 specializations: Prospector, Weaponsmith, Appraiser, Baker, Carpenter, Surgeon.
- Single-path advances: Deep Prospector, Forge Master, Chef, Architect, Chirurgeon.
- Convergence: Miner + Smith → Metallurgist.
- Cross-system proof: Runesmith or Plague Doctor, implemented only after ordinary domain isolation is tested.

This slice is large enough to validate contextual offers, additive specialization, single-path advancement, convergence, repeated evidence, class/profession isolation, alternate routes, and forced transformation without requiring the entire catalog at launch.

using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>
/// Item/spell/weapon/armor rarity. Ordered from weakest to strongest.
/// (Game Idea.md: Common → Mythic.)
/// </summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythic
}

/// <summary>
/// What kind of thing a <see cref="Combinable"/> is. Combination rules depend on the
/// pair of kinds (e.g. Weapon + Spell → a new Weapon).
/// </summary>
public enum CombinableKind
{
    Weapon,
    Armor,
    Item,
    Spell,
    Ability
}

/// <summary>
/// Elemental and physical attribute tags (Game Idea.md "Item Attributes").
/// Named GameAttribute to avoid clashing with System.Attribute.
/// </summary>
public enum GameAttribute
{
    // General
    Magic,
    Arcane,
    // Elemental
    Earth,
    Air,
    Water,
    Fire,
    Poison,
    Ice,
    Lightning,
    Holy,
    Shadow,
    // Physical / quality
    Sharp,
    Heavy,
    Medium,
    Light,
    Fragile,
    Cursed,
    Blessed,
    // Emergent synergies (produced by combining, not authored directly)
    Infernal,   // Fire + Shadow
    Frost,      // Fire + Ice blend marker
    Storm       // Air + Lightning
}

/// <summary>
/// Base type for everything the player can hold, equip, cast, or merge: items,
/// weapons, armor, spells, and abilities. The unified shape is what makes the
/// merge/combine system (see CombinationEngine) possible across kinds.
///
/// This layer is intentionally decoupled from live combat for now — nothing here is
/// wired into GameState yet. Attacks are still produced by AttackFactory; the planned
/// next step is to project a hero's equipped Weapon/Spell into an Attack.
/// </summary>
public abstract class Combinable
{
    /// <summary>Stable identifier (kebab-case), independent of the display name.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public abstract CombinableKind Kind { get; }
    public Rarity Rarity { get; set; } = Rarity.Common;

    /// <summary>Attribute tags carried by this thing (drives combine rules and effects).</summary>
    public HashSet<GameAttribute> Attributes { get; set; } = new();

    /// <summary>
    /// Level for things that grow with use (spells, abilities). Items are level-less
    /// (their power comes from rarity); leave at 0 for those.
    /// </summary>
    public int Level { get; set; } = 0;

    /// <summary>Intensity counter bumped when merging same-attribute things (Fireball → Fireball+).</summary>
    public int Intensity { get; set; } = 0;

    public bool HasAttribute(GameAttribute a) => Attributes.Contains(a);
}

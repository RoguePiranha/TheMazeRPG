using System.Collections.Generic;
using System.Text.Json.Serialization;

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
/// [JsonPolymorphic]/[JsonDerivedType] preserve the concrete type (Weapon vs. Spell vs. ...)
/// when a List&lt;Combinable&gt; (Hero.Loadout/Inventory) round-trips through JSON — needed
/// for SaveService, since deserializing into the abstract base alone would lose which
/// concrete fields (BaseDamage, ManaCost, Modifiers, ...) actually apply.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(Weapon), "weapon")]
[JsonDerivedType(typeof(Armor), "armor")]
[JsonDerivedType(typeof(Spell), "spell")]
[JsonDerivedType(typeof(Ability), "ability")]
[JsonDerivedType(typeof(Item), "item")]
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

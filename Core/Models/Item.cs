namespace TheMazeRPG.Core.Models;

public enum ItemUseEffect
{
    None,
    RestoreHealth
}

/// <summary>
/// A generic item: consumables, materials, and gear that isn't a weapon (e.g. a Shield
/// Generator, Holy Symbol). Items are level-less — their power comes from rarity and
/// attributes. Combining an item with a spell generally yields a new item.
/// </summary>
public class Item : Combinable
{
    public override CombinableKind Kind => CombinableKind.Item;
    public ItemUseEffect UseEffect { get; set; }
    public int EffectPower { get; set; }
    public bool Consumable { get; set; }
}

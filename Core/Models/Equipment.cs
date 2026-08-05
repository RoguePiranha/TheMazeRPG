namespace TheMazeRPG.Core.Models;

/// <summary>Physical locations on a humanoid body. Held items are separate from worn gloves.</summary>
public enum EquipmentSlot
{
    Head,
    Chest,
    Hands,
    Legs,
    Feet,
    Amulet,
    RingLeft,
    RingRight,
    MainHand,
    OffHand
}

public static class EquipmentSlots
{
    public static readonly EquipmentSlot[] DisplayOrder =
    {
        EquipmentSlot.Head,
        EquipmentSlot.Chest,
        EquipmentSlot.Hands,
        EquipmentSlot.Legs,
        EquipmentSlot.Feet,
        EquipmentSlot.Amulet,
        EquipmentSlot.RingLeft,
        EquipmentSlot.RingRight,
        EquipmentSlot.MainHand,
        EquipmentSlot.OffHand
    };

    public static string Label(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.RingLeft => "Left Ring",
        EquipmentSlot.RingRight => "Right Ring",
        EquipmentSlot.MainHand => "Main Hand",
        EquipmentSlot.OffHand => "Off Hand",
        _ => slot.ToString()
    };
}

namespace TheMazeRPG.Core.Models;

/// <summary>Armor or clothing worn in one specific humanoid body slot.</summary>
public class Armor : Combinable
{
    public override CombinableKind Kind => CombinableKind.Armor;
    public EquipmentSlot Slot { get; set; }
    public int DefenseBonus { get; set; }
}

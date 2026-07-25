using System.Collections.Generic;

namespace TheMazeRPG.Core.Models;

/// <summary>A crafting recipe definition (Data/Recipes/recipes.json). Output is either more
/// resources ("material") or a real Combinable ("weapon") — see CraftedItemCatalog for the
/// weapon-id -> Weapon mapping.</summary>
public class RecipeDef
{
    public string Name { get; set; } = "";
    public Dictionary<string, int> Inputs { get; set; } = new();
    public string OutputType { get; set; } = "material"; // "material" | "weapon"
    public string OutputId { get; set; } = "";
    public int OutputAmount { get; set; } = 1;
    public float DurationSeconds { get; set; } = 3f;
}

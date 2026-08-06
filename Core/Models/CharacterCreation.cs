using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TheMazeRPG.Core.Models;

/// <summary>The equipment-first choices used to create and restart a character.</summary>
public class CharacterCreationSelection
{
    public string Name { get; set; } = "Wayfarer";
    public string RaceName { get; set; } = "Human";
    public string KitId { get; set; } = "traveler";
    public bool IsCustom { get; set; }
    public List<string> ItemIds { get; set; } = new();

    /// <summary>Where this character comes into existence. Dungeon by default, which is the
    /// canonical opening — see StartLocation. Saves predating the field deserialize to Dungeon,
    /// which is what they did.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StartLocation StartLocation { get; set; } = StartLocation.Dungeon;

    public CharacterCreationSelection Clone() => new()
    {
        Name = Name,
        RaceName = RaceName,
        KitId = KitId,
        IsCustom = IsCustom,
        ItemIds = new List<string>(ItemIds),
        StartLocation = StartLocation
    };
}

public class StarterKitDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ItemIds { get; set; } = new();
}

public class StarterLoadoutOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ItemIds { get; set; } = new();
}

public class StarterLoadoutGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<StarterLoadoutOption> Options { get; set; } = new();
}

public class StarterLoadoutCatalog
{
    public List<StarterKitDefinition> Kits { get; set; } = new();
    public List<StarterLoadoutGroup> CustomGroups { get; set; } = new();

    public List<string> ResolveCustomItems(IReadOnlyDictionary<string, string> selections) =>
        CustomGroups.SelectMany(group => group.Options
                .Where(option => selections.GetValueOrDefault(group.Id) == option.Id)
                .SelectMany(option => option.ItemIds))
            .ToList();
}

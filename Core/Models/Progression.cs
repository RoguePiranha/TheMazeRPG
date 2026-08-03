using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TheMazeRPG.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressionDomain
{
    Class,
    Profession
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressionSlotState
{
    Locked,
    Empty,
    Active,
    Mastered
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressionAdvancementKind
{
    SinglePath,
    Convergence
}

public class ProgressionLineageEntry
{
    public string InstanceId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public int LevelAtConsumption { get; set; }
    public string SpecializationId { get; set; } = "";
    public string SpecializationName { get; set; } = "";
}

public class ProgressionSpecialization
{
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public int SelectedAtLevel { get; set; } = 10;
}

public class ProgressionInstance
{
    public string InstanceId { get; set; } = System.Guid.NewGuid().ToString();
    public string DefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public int CurrentXp { get; set; }
    public int MaxLevel { get; set; } = 25;
    public ProgressionSpecialization? Specialization { get; set; }
    public List<ProgressionLineageEntry> Lineage { get; set; } = new();
}

public class ProgressionSlot
{
    public string SlotId { get; set; } = System.Guid.NewGuid().ToString();
    public ProgressionDomain Domain { get; set; }
    public bool IsLocked { get; set; }
    public ProgressionInstance? Instance { get; set; }

    [JsonIgnore]
    public ProgressionSlotState State => IsLocked
        ? ProgressionSlotState.Locked
        : Instance == null
            ? ProgressionSlotState.Empty
            : Instance.Level >= Instance.MaxLevel
                ? ProgressionSlotState.Mastered
                : ProgressionSlotState.Active;
}

public class SkillProgress
{
    public string SkillId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public int CurrentXp { get; set; }
    public int MaxLevel { get; set; } = 10;
}

/// <summary>Persisted progression state. Overall level is always derived from occupied class slots.</summary>
public class ProgressionState
{
    public int UnallocatedXp { get; set; }
    public List<ProgressionSlot> ClassSlots { get; set; } = new();
    public List<ProgressionSlot> ProfessionSlots { get; set; } = new();
    public Dictionary<string, SkillProgress> Skills { get; set; } = new();
    public HashSet<string> GrantedRewardIds { get; set; } = new();
    public Dictionary<string, decimal> Facts { get; set; } = new();

    [JsonIgnore]
    public int CharacterLevel => ClassSlots
        .Where(slot => !slot.IsLocked && slot.Instance != null)
        .Sum(slot => slot.Instance!.Level);
}

public class ProgressionDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgressionDomain Domain { get; set; }
    public int MaxLevel { get; set; } = 25;
    public int XpCurveMultiplier { get; set; } = 100;
    public List<int> XpToNextLevel { get; set; } = new();
    public int AttributePointsPerLevel { get; set; }
    public Dictionary<string, int> AutoAttributeWeights { get; set; } = new();
    public Dictionary<string, int> FixedAttributesPerLevel { get; set; } = new();
    public List<string> AssociatedSkills { get; set; } = new();
    public int ActionXp { get; set; }
    public int BaseOfferWeight { get; set; }
    public List<ProgressionUnlockRoute> InitialRoutes { get; set; } = new();
    public List<ProgressionSpecializationDefinition> Specializations { get; set; } = new();
    public HashSet<WeaponType> WeaponAffinities { get; set; } = new();
}

public class ProgressionSpecializationDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int BaseOfferWeight { get; set; }
    public List<ProgressionUnlockRoute> Routes { get; set; } = new();
}

public class ProgressionRequirement
{
    public string FactId { get; set; } = "";
    public string Operator { get; set; } = ">=";
    public decimal Threshold { get; set; } = 1m;
    public bool Negated { get; set; }
}

public class ProgressionUnlockRoute
{
    public string Id { get; set; } = "";
    public string Explanation { get; set; } = "";
    public int BaseScore { get; set; }
    public List<ProgressionRequirement> AllOf { get; set; } = new();
}

public class ProgressionOffer
{
    public string ResultDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgressionDomain Domain { get; set; }
    public int Score { get; set; }
    public string ContextHash { get; set; } = "";
    public List<string> SatisfiedRouteIds { get; set; } = new();
    public List<string> Explanations { get; set; } = new();
}

public class ProgressionSpecializationOffer
{
    public string SourceDefinitionId { get; set; } = "";
    public string SpecializationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgressionDomain Domain { get; set; }
    public int Score { get; set; }
    public string ContextHash { get; set; } = "";
    public List<string> Explanations { get; set; } = new();
}

public class ProgressionAdvancementSourceRequirement
{
    public string DefinitionId { get; set; } = "";
    public int Count { get; set; } = 1;
    public int MinimumLevel { get; set; } = 25;
    public string RequiredSpecializationId { get; set; } = "";
}

public class ProgressionAdvancementRecipe
{
    public string Id { get; set; } = "";
    public string ResultDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgressionAdvancementKind Kind { get; set; }
    public ProgressionDomain Domain { get; set; }
    public int BaseOfferWeight { get; set; }
    public List<ProgressionAdvancementSourceRequirement> Sources { get; set; } = new();
    public List<ProgressionUnlockRoute> Routes { get; set; } = new();
}

public class ProgressionAdvancementOffer
{
    public string RecipeId { get; set; } = "";
    public string ResultDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProgressionAdvancementKind Kind { get; set; }
    public ProgressionDomain Domain { get; set; }
    public int Score { get; set; }
    public string ContextHash { get; set; } = "";
    public string TargetSlotId { get; set; } = "";
    public List<string> SourceSlotIds { get; set; } = new();
    public List<string> Explanations { get; set; } = new();
}

public class ProgressionSkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxLevel { get; set; } = 10;
    public List<int> XpToNextLevel { get; set; } = new();
    public float YieldBonusPerLevel { get; set; }
    public float BonusDropChancePerLevel { get; set; }
}

public class ProgressionCatalog
{
    public int StartingClassSlots { get; set; } = 2;
    public int StartingProfessionSlots { get; set; } = 1;
    public int DefaultClassAttributePointsPerLevel { get; set; } = 4;
    public int OfferPresentationLimit { get; set; } = 4;
    public List<ProgressionDefinition> Definitions { get; set; } = new();
    public List<ProgressionAdvancementRecipe> AdvancementRecipes { get; set; } = new();
    public List<ProgressionSkillDefinition> Skills { get; set; } = new();
}

public class ProgressionAdvanceResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public int XpApplied { get; set; }
    public int SkillXpApplied { get; set; }
    public int LevelsGained { get; set; }
    public int FreeAttributePointsGranted { get; set; }
    public Dictionary<string, int> AutomaticAttributesGranted { get; set; } = new();
}

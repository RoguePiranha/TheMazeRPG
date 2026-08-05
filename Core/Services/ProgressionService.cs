using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Owns XP movement and atomic level rewards for class, profession, and skill paths.</summary>
public sealed class ProgressionService
{
    private static ProgressionService? _instance;
    public static ProgressionService Instance => _instance ??= new ProgressionService();

    private readonly ProgressionDataService _data;

    public ProgressionService(ProgressionDataService? data = null)
    {
        _data = data ?? ProgressionDataService.Instance;
    }

    public ProgressionState CreateCompatibilityState(string className, int legacyLevel = 1, int legacyXp = 0)
    {
        var state = new ProgressionState();
        for (int index = 0; index < Math.Max(1, _data.Catalog.StartingClassSlots); index++)
            state.ClassSlots.Add(NewSlot(ProgressionDomain.Class));
        for (int index = 0; index < Math.Max(1, _data.Catalog.StartingProfessionSlots); index++)
            state.ProfessionSlots.Add(NewSlot(ProgressionDomain.Profession));

        ProgressionDefinition definition = _data.FindDefinition(className) ??
            _data.FindDefinition("wanderer") ?? throw new InvalidOperationException("No fallback class definition exists.");
        int level = Math.Clamp(legacyLevel, 1, definition.MaxLevel);
        state.ClassSlots[0].Instance = NewInstance(definition, level);
        if (level < definition.MaxLevel)
            state.ClassSlots[0].Instance!.CurrentXp = Math.Clamp(legacyXp, 0, XpToNext(definition, level) - 1);
        else
            state.UnallocatedXp = Math.Max(0, legacyXp);
        return state;
    }

    public ProgressionState CreateEmptyState()
    {
        var state = new ProgressionState();
        for (int index = 0; index < Math.Max(1, _data.Catalog.StartingClassSlots); index++)
            state.ClassSlots.Add(NewSlot(ProgressionDomain.Class));
        for (int index = 0; index < Math.Max(1, _data.Catalog.StartingProfessionSlots); index++)
            state.ProfessionSlots.Add(NewSlot(ProgressionDomain.Profession));
        return state;
    }

    public void Normalize(Hero hero)
    {
        hero.Progression ??= CreateCompatibilityState(hero.Class, hero.Level, hero.Experience);
        ProgressionState state = hero.Progression;
        state.ClassSlots ??= new List<ProgressionSlot>();
        state.ProfessionSlots ??= new List<ProgressionSlot>();
        state.Skills ??= new Dictionary<string, SkillProgress>();
        state.GrantedRewardIds ??= new HashSet<string>();
        state.Facts ??= new Dictionary<string, decimal>();

        if (state.ClassSlots.Count == 0)
            state.ClassSlots.Add(NewSlot(ProgressionDomain.Class));
        while (state.ClassSlots.Count < Math.Max(1, _data.Catalog.StartingClassSlots))
            state.ClassSlots.Add(NewSlot(ProgressionDomain.Class));
        while (state.ProfessionSlots.Count < Math.Max(1, _data.Catalog.StartingProfessionSlots))
            state.ProfessionSlots.Add(NewSlot(ProgressionDomain.Profession));
        foreach (ProgressionSlot slot in state.ClassSlots) slot.Domain = ProgressionDomain.Class;
        foreach (ProgressionSlot slot in state.ProfessionSlots) slot.Domain = ProgressionDomain.Profession;
        foreach (ProgressionSlot slot in state.ClassSlots.Concat(state.ProfessionSlots))
        {
            if (slot.Instance == null) continue;
            ProgressionInstance instance = slot.Instance;
            instance.Lineage ??= new List<ProgressionLineageEntry>();
            ProgressionDefinition? definition = _data.FindDefinition(instance.DefinitionId);
            if (definition == null) continue;
            instance.Name = definition.Name;
            instance.MaxLevel = definition.MaxLevel;
            instance.Level = Math.Clamp(instance.Level, 1, instance.MaxLevel);
            if (instance.Level >= instance.MaxLevel) instance.CurrentXp = 0;
            else instance.CurrentXp = Math.Clamp(
                instance.CurrentXp, 0, XpToNext(definition, instance.Level) - 1);
        }

        SyncLegacyFields(hero);
    }

    public int GrantSharedXp(Hero hero, int amount)
    {
        Normalize(hero);
        if (amount <= 0) return 0;
        int adjusted = Math.Max(0, (int)(amount * (1f + hero.EffectiveCharisma * 0.05f)));
        hero.Progression.UnallocatedXp += adjusted;
        SyncLegacyFields(hero);
        return adjusted;
    }

    public bool TryPlacePath(Hero hero, ProgressionDomain domain, string definitionId, out string error)
    {
        Normalize(hero);
        List<ProgressionSlot> slots = domain == ProgressionDomain.Class
            ? hero.Progression.ClassSlots : hero.Progression.ProfessionSlots;
        ProgressionSlot? slot = slots.FirstOrDefault(candidate => !candidate.IsLocked && candidate.Instance == null);
        if (slot == null)
        {
            error = $"No empty {domain.ToString().ToLowerInvariant()} slot is available.";
            return false;
        }
        return TryPlacePath(hero, domain, slot.SlotId, definitionId, out error);
    }

    public bool TryPlacePath(Hero hero, ProgressionDomain domain, string slotId,
        string definitionId, out string error)
    {
        Normalize(hero);
        ProgressionDefinition? definition = _data.FindDefinition(definitionId);
        if (definition == null || definition.Domain != domain)
        {
            error = "That progression path does not exist in this domain.";
            return false;
        }

        List<ProgressionSlot> slots = domain == ProgressionDomain.Class
            ? hero.Progression.ClassSlots : hero.Progression.ProfessionSlots;
        ProgressionSlot? slot = slots.FirstOrDefault(candidate => candidate.SlotId == slotId);
        if (slot is not { IsLocked: false, Instance: null })
        {
            error = $"The selected {domain.ToString().ToLowerInvariant()} slot is not empty.";
            return false;
        }

        slot.Instance = NewInstance(definition);
        SyncLegacyFields(hero);
        error = "";
        return true;
    }

    public bool TrySpecialize(Hero hero, string slotId, string specializationId, out string error)
    {
        Normalize(hero);
        ProgressionSlot? slot = hero.Progression.ClassSlots.Concat(hero.Progression.ProfessionSlots)
            .FirstOrDefault(candidate => candidate.SlotId == slotId);
        if (slot is not { IsLocked: false, Instance: not null })
        {
            error = "Select an occupied, unlocked progression slot.";
            return false;
        }

        ProgressionInstance instance = slot.Instance;
        if (instance.Level < 10)
        {
            error = "A path must reach level 10 before specializing.";
            return false;
        }
        if (instance.Specialization != null)
        {
            error = $"{instance.Name} is already specialized as {instance.Specialization.Name}.";
            return false;
        }

        ProgressionDefinition? definition = _data.FindDefinition(instance.DefinitionId);
        ProgressionSpecializationDefinition? specialization = definition?.Specializations
            .FirstOrDefault(candidate => string.Equals(candidate.Id, specializationId,
                StringComparison.OrdinalIgnoreCase));
        if (specialization == null)
        {
            error = "That specialization is not defined for this path.";
            return false;
        }

        instance.Specialization = new ProgressionSpecialization
        {
            DefinitionId = specialization.Id,
            Name = specialization.Name,
            SelectedAtLevel = instance.Level
        };
        SyncLegacyFields(hero);
        error = "";
        return true;
    }

    public bool TryAdvance(Hero hero, string recipeId, string targetSlotId,
        IReadOnlyList<string> sourceSlotIds, out string error)
    {
        Normalize(hero);
        if (!_data.AdvancementRecipes.TryGetValue(recipeId, out ProgressionAdvancementRecipe? recipe))
        {
            error = "That advancement recipe does not exist.";
            return false;
        }
        ProgressionDefinition? resultDefinition = _data.FindDefinition(recipe.ResultDefinitionId);
        if (resultDefinition == null || resultDefinition.Domain != recipe.Domain)
        {
            error = "The advancement result is unavailable in the required domain.";
            return false;
        }

        List<ProgressionSlot> domainSlots = recipe.Domain == ProgressionDomain.Class
            ? hero.Progression.ClassSlots : hero.Progression.ProfessionSlots;
        if (sourceSlotIds.Count != recipe.Sources.Sum(source => Math.Max(1, source.Count)) ||
            sourceSlotIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceSlotIds.Count)
        {
            error = "The advancement source selection is incomplete.";
            return false;
        }
        List<ProgressionSlot> sources = sourceSlotIds
            .Select(id => domainSlots.FirstOrDefault(slot => slot.SlotId == id))
            .Where(slot => slot != null)
            .Cast<ProgressionSlot>()
            .ToList();
        ProgressionSlot? target = sources.FirstOrDefault(slot => slot.SlotId == targetSlotId);
        if (sources.Count != sourceSlotIds.Count || target == null ||
            sources.Any(slot => slot.IsLocked || slot.Instance == null))
        {
            error = "One or more advancement source slots are no longer available.";
            return false;
        }

        var remaining = new List<ProgressionSlot>(sources);
        foreach (ProgressionAdvancementSourceRequirement requirement in recipe.Sources
                     .SelectMany(source => Enumerable.Repeat(source, Math.Max(1, source.Count)))
                     .OrderByDescending(source => !string.IsNullOrWhiteSpace(source.RequiredSpecializationId)))
        {
            ProgressionSlot? match = remaining.FirstOrDefault(slot => SourceMatches(slot, requirement));
            if (match == null)
            {
                error = "The selected paths no longer satisfy the advancement recipe.";
                return false;
            }
            remaining.Remove(match);
        }

        var lineage = new List<ProgressionLineageEntry>();
        foreach (ProgressionSlot source in sources)
        {
            ProgressionInstance instance = source.Instance!;
            lineage.AddRange(instance.Lineage.Select(CloneLineage));
            lineage.Add(new ProgressionLineageEntry
            {
                InstanceId = instance.InstanceId,
                DefinitionId = instance.DefinitionId,
                Name = instance.Name,
                LevelAtConsumption = instance.Level,
                SpecializationId = instance.Specialization?.DefinitionId ?? "",
                SpecializationName = instance.Specialization?.Name ?? ""
            });
        }

        ProgressionInstance result = NewInstance(resultDefinition);
        result.Lineage = lineage;
        target.Instance = result;
        foreach (ProgressionSlot source in sources)
            if (!ReferenceEquals(source, target)) source.Instance = null;
        SyncLegacyFields(hero);
        error = "";
        return true;
    }

    public bool HasActiveWeaponAffinity(Hero hero, WeaponType weaponType)
    {
        Normalize(hero);
        return hero.Progression.ClassSlots
            .Where(slot => !slot.IsLocked && slot.Instance != null)
            .Select(slot => _data.FindDefinition(slot.Instance!.DefinitionId))
            .Any(definition => definition != null && definition.WeaponAffinities.Contains(weaponType));
    }

    public ProgressionAdvanceResult AllocateSharedXp(Hero hero, string slotId, int requestedAmount)
    {
        Normalize(hero);
        ProgressionSlot? slot = hero.Progression.ClassSlots.FirstOrDefault(candidate => candidate.SlotId == slotId);
        if (slot is not { IsLocked: false, Instance: not null })
            return Failure("Select an occupied, unlocked class slot.");

        ProgressionInstance instance = slot.Instance;
        ProgressionDefinition? definition = _data.FindDefinition(instance.DefinitionId);
        if (definition == null || definition.Domain != ProgressionDomain.Class)
            return Failure("The class definition is unavailable.");
        if (instance.Level >= instance.MaxLevel)
            return Failure("That class is already mastered.");
        if (requestedAmount <= 0 || hero.Progression.UnallocatedXp <= 0)
            return Failure("No shared XP is available to allocate.");

        int capacity = XpCapacityToMax(definition, instance);
        int applied = Math.Min(Math.Min(requestedAmount, hero.Progression.UnallocatedXp), capacity);
        if (applied <= 0) return Failure("That class cannot accept more XP.");

        var result = new ProgressionAdvanceResult { Success = true, XpApplied = applied };
        hero.Progression.UnallocatedXp -= applied;
        instance.CurrentXp += applied;

        while (instance.Level < instance.MaxLevel)
        {
            int needed = XpToNext(definition, instance.Level);
            if (instance.CurrentXp < needed) break;
            instance.CurrentXp -= needed;
            instance.Level++;
            result.LevelsGained++;
            GrantClassLevelRewards(hero, definition, instance, result);
        }

        if (instance.Level >= instance.MaxLevel && instance.CurrentXp > 0)
        {
            hero.Progression.UnallocatedXp += instance.CurrentXp;
            instance.CurrentXp = 0;
        }
        SyncLegacyFields(hero);
        return result;
    }

    public ProgressionAdvanceResult RecordProfessionAction(Hero hero, string professionId, int? xp = null)
    {
        Normalize(hero);
        ProgressionDefinition? definition = _data.FindDefinition(professionId);
        if (definition == null || definition.Domain != ProgressionDomain.Profession)
            return Failure("The profession definition is unavailable.");
        ProgressionInstance? instance = hero.Progression.ProfessionSlots
            .Where(slot => !slot.IsLocked)
            .Select(slot => slot.Instance)
            .FirstOrDefault(candidate => candidate != null &&
                string.Equals(candidate.DefinitionId, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (instance == null)
            return Failure($"{definition.Name} is not active.");
        if (instance.Level >= instance.MaxLevel)
            return Failure($"{definition.Name} is already mastered.");

        int actionXp = Math.Max(0, xp ?? definition.ActionXp);
        int applied = Math.Min(actionXp, XpCapacityToMax(definition, instance));
        var result = new ProgressionAdvanceResult
        {
            Success = true, XpApplied = applied, SkillXpApplied = actionXp
        };
        ApplyDirectPathXp(hero, definition, instance, applied, result);
        foreach (string skillId in definition.AssociatedSkills)
            ApplySkillXp(hero.Progression, skillId, actionXp);
        SyncLegacyFields(hero);
        return result;
    }

    public int XpNeededForNext(ProgressionInstance instance)
    {
        ProgressionDefinition? definition = _data.FindDefinition(instance.DefinitionId);
        if (definition == null || instance.Level >= instance.MaxLevel) return 0;
        return Math.Max(0, XpToNext(definition, instance.Level) - instance.CurrentXp);
    }

    public int ActiveClassLevel(Hero hero, string className)
    {
        Normalize(hero);
        return hero.Progression.ClassSlots
            .Where(slot => !slot.IsLocked && slot.Instance != null)
            .Select(slot => slot.Instance!)
            .Where(instance => string.Equals(instance.Name, className, StringComparison.OrdinalIgnoreCase))
            .Select(instance => instance.Level)
            .DefaultIfEmpty(0)
            .Max();
    }

    public float SkillYieldMultiplier(Hero hero, string skillId)
    {
        Normalize(hero);
        if (!hero.Progression.Skills.TryGetValue(skillId, out SkillProgress? skill) ||
            !_data.Skills.TryGetValue(skillId, out ProgressionSkillDefinition? definition)) return 1f;
        return 1f + Math.Max(0, skill.Level - 1) * definition.YieldBonusPerLevel;
    }

    public float SkillBonusDropChance(Hero hero, string skillId)
    {
        Normalize(hero);
        if (!hero.Progression.Skills.TryGetValue(skillId, out SkillProgress? skill) ||
            !_data.Skills.TryGetValue(skillId, out ProgressionSkillDefinition? definition)) return 0f;
        return Math.Clamp(Math.Max(0, skill.Level - 1) * definition.BonusDropChancePerLevel, 0f, 1f);
    }

    private void ApplyDirectPathXp(Hero hero, ProgressionDefinition definition,
        ProgressionInstance instance, int amount, ProgressionAdvanceResult result)
    {
        if (instance.Level >= instance.MaxLevel || amount <= 0) return;
        instance.CurrentXp += Math.Min(amount, XpCapacityToMax(definition, instance));
        while (instance.Level < instance.MaxLevel)
        {
            int needed = XpToNext(definition, instance.Level);
            if (instance.CurrentXp < needed) break;
            instance.CurrentXp -= needed;
            instance.Level++;
            result.LevelsGained++;
            GrantFixedRewards(hero, definition, instance, result);
        }
        if (instance.Level >= instance.MaxLevel) instance.CurrentXp = 0;
    }

    private void ApplySkillXp(ProgressionState state, string skillId, int amount)
    {
        if (!_data.Skills.TryGetValue(skillId, out ProgressionSkillDefinition? definition)) return;
        if (!state.Skills.TryGetValue(skillId, out SkillProgress? skill))
        {
            skill = new SkillProgress
            {
                SkillId = definition.Id, Name = definition.Name, MaxLevel = definition.MaxLevel
            };
            state.Skills[skillId] = skill;
        }
        if (skill.Level >= skill.MaxLevel) return;
        skill.CurrentXp += amount;
        while (skill.Level < skill.MaxLevel)
        {
            int needed = XpToNext(definition.XpToNextLevel, skill.Level);
            if (skill.CurrentXp < needed) break;
            skill.CurrentXp -= needed;
            skill.Level++;
        }
        if (skill.Level >= skill.MaxLevel) skill.CurrentXp = 0;
    }

    private void GrantClassLevelRewards(Hero hero, ProgressionDefinition definition,
        ProgressionInstance instance, ProgressionAdvanceResult result)
    {
        int budget = definition.AttributePointsPerLevel > 0
            ? definition.AttributePointsPerLevel : _data.Catalog.DefaultClassAttributePointsPerLevel;
        int automatic = budget / 2;
        int free = budget - automatic;
        Dictionary<string, int> weights = definition.AutoAttributeWeights
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (weights.Count == 0) weights["Constitution"] = 1;

        for (int point = 0; point < automatic; point++)
        {
            int priorAutomaticPoints = Math.Max(0, instance.Level - 2) * automatic;
            string stat = SelectWeightedStat(weights, priorAutomaticPoints + point);
            string rewardId = $"{instance.InstanceId}:level:{instance.Level}:auto:{point}";
            if (!hero.Progression.GrantedRewardIds.Add(rewardId)) continue;
            IncrementAttribute(hero, stat, 1);
            result.AutomaticAttributesGranted[stat] =
                result.AutomaticAttributesGranted.GetValueOrDefault(stat, 0) + 1;
        }

        string freeRewardId = $"{instance.InstanceId}:level:{instance.Level}:free";
        if (hero.Progression.GrantedRewardIds.Add(freeRewardId))
        {
            hero.UnspentStatPoints += free;
            result.FreeAttributePointsGranted += free;
        }
    }

    private static void GrantFixedRewards(Hero hero, ProgressionDefinition definition,
        ProgressionInstance instance, ProgressionAdvanceResult result)
    {
        foreach ((string stat, int amount) in definition.FixedAttributesPerLevel)
        {
            string rewardId = $"{instance.InstanceId}:level:{instance.Level}:fixed:{stat}";
            if (!hero.Progression.GrantedRewardIds.Add(rewardId)) continue;
            IncrementAttribute(hero, stat, amount);
            result.AutomaticAttributesGranted[stat] =
                result.AutomaticAttributesGranted.GetValueOrDefault(stat, 0) + amount;
        }
    }

    private static void IncrementAttribute(Hero hero, string stat, int amount)
    {
        switch (stat)
        {
            case "Strength": hero.Strength += amount; break;
            case "Constitution": hero.Constitution += amount; break;
            case "Agility": hero.Agility += amount; break;
            case "Dexterity": hero.Dexterity += amount; break;
            case "Intelligence": hero.Intelligence += amount; break;
            case "Wisdom": hero.Wisdom += amount; break;
            case "Charisma": hero.Charisma += amount; break;
        }
    }

    private static string SelectWeightedStat(IReadOnlyDictionary<string, int> weights, int ordinal)
    {
        int totalWeight = weights.Values.Sum();
        var scores = weights.Keys.ToDictionary(stat => stat, _ => 0);
        string selected = weights.Keys.OrderBy(stat => stat, StringComparer.Ordinal).First();
        for (int step = 0; step <= ordinal; step++)
        {
            foreach ((string stat, int weight) in weights) scores[stat] += weight;
            selected = scores
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First().Key;
            scores[selected] -= totalWeight;
        }
        return selected;
    }

    private static ProgressionSlot NewSlot(ProgressionDomain domain) => new() { Domain = domain };

    private static ProgressionInstance NewInstance(ProgressionDefinition definition, int level = 1) => new()
    {
        DefinitionId = definition.Id,
        Name = definition.Name,
        Level = level,
        MaxLevel = definition.MaxLevel
    };

    private static ProgressionAdvanceResult Failure(string error) => new() { Error = error };

    private static int XpToNext(ProgressionDefinition definition, int currentLevel)
    {
        int index = Math.Max(0, currentLevel - 1);
        return index < definition.XpToNextLevel.Count
            ? Math.Max(1, definition.XpToNextLevel[index])
            : Math.Max(1, definition.XpCurveMultiplier) * currentLevel * currentLevel;
    }

    private static int XpToNext(IReadOnlyList<int> curve, int currentLevel)
    {
        int index = Math.Max(0, currentLevel - 1);
        return index < curve.Count ? Math.Max(1, curve[index]) : 100 * currentLevel * currentLevel;
    }

    private static int XpCapacityToMax(ProgressionDefinition definition, ProgressionInstance instance)
    {
        long capacity = -instance.CurrentXp;
        for (int level = instance.Level; level < instance.MaxLevel; level++)
            capacity += XpToNext(definition, level);
        return (int)Math.Clamp(capacity, 0, int.MaxValue);
    }

    private static bool SourceMatches(ProgressionSlot slot,
        ProgressionAdvancementSourceRequirement requirement)
    {
        ProgressionInstance? instance = slot.Instance;
        return instance != null && instance.Level >= requirement.MinimumLevel &&
            string.Equals(instance.DefinitionId, requirement.DefinitionId,
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(requirement.RequiredSpecializationId) ||
             string.Equals(instance.Specialization?.DefinitionId, requirement.RequiredSpecializationId,
                 StringComparison.OrdinalIgnoreCase));
    }

    private static ProgressionLineageEntry CloneLineage(ProgressionLineageEntry source) => new()
    {
        InstanceId = source.InstanceId,
        DefinitionId = source.DefinitionId,
        Name = source.Name,
        LevelAtConsumption = source.LevelAtConsumption,
        SpecializationId = source.SpecializationId,
        SpecializationName = source.SpecializationName
    };

    private void SyncLegacyFields(Hero hero)
    {
        hero.Level = hero.Progression.CharacterLevel;
        hero.Experience = hero.Progression.UnallocatedXp;
        ProgressionInstance? primary = hero.Progression.ClassSlots
            .FirstOrDefault(slot => !slot.IsLocked && slot.Instance != null)?.Instance;
        hero.ExperienceToNext = primary == null || primary.Level >= primary.MaxLevel
            ? 0 : XpNeededForNext(primary);
    }
}

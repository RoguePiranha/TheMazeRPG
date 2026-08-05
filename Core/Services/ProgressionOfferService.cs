using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Builds normalized facts and deterministic initial-path offers for empty slots.</summary>
public sealed class ProgressionOfferService
{
    private static ProgressionOfferService? _instance;
    public static ProgressionOfferService Instance => _instance ??= new ProgressionOfferService();

    private readonly ProgressionDataService _data;

    public ProgressionOfferService(ProgressionDataService? data = null)
    {
        _data = data ?? ProgressionDataService.Instance;
    }

    public IReadOnlyList<ProgressionOffer> GenerateOffers(Hero hero, ProgressionDomain domain,
        string targetSlotId, IReadOnlyDictionary<string, decimal>? contextFacts = null)
    {
        ProgressionService.Instance.Normalize(hero);
        List<ProgressionSlot> slots = domain == ProgressionDomain.Class
            ? hero.Progression.ClassSlots : hero.Progression.ProfessionSlots;
        ProgressionSlot? target = slots.FirstOrDefault(slot => slot.SlotId == targetSlotId);
        if (target is not { IsLocked: false, Instance: null }) return Array.Empty<ProgressionOffer>();

        Dictionary<string, decimal> facts = BuildFacts(hero, contextFacts);
        string contextHash = BuildContextHash("initial", domain, targetSlotId, facts);
        var offers = new List<ProgressionOffer>();

        foreach (ProgressionDefinition definition in _data.Definitions.Values
                     .Where(candidate => candidate.Domain == domain && candidate.InitialRoutes.Count > 0))
        {
            List<ProgressionUnlockRoute> satisfied = definition.InitialRoutes
                .Where(route => route.AllOf.All(requirement => RequirementPasses(requirement, facts)))
                .ToList();
            if (satisfied.Count == 0) continue;
            int bestRoute = satisfied.Max(route => route.BaseScore);
            offers.Add(new ProgressionOffer
            {
                ResultDefinitionId = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                Domain = definition.Domain,
                Score = definition.BaseOfferWeight + bestRoute + Math.Max(0, satisfied.Count - 1) * 5,
                ContextHash = contextHash,
                SatisfiedRouteIds = satisfied.Select(route => route.Id).ToList(),
                Explanations = satisfied.Select(route => route.Explanation)
                    .Where(explanation => !string.IsNullOrWhiteSpace(explanation)).ToList()
            });
        }

        return offers.OrderByDescending(offer => offer.Score)
            .ThenBy(offer => offer.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, _data.Catalog.OfferPresentationLimit))
            .ToList();
    }

    public IReadOnlyList<ProgressionSpecializationOffer> GenerateSpecializationOffers(Hero hero,
        string slotId, IReadOnlyDictionary<string, decimal>? contextFacts = null)
    {
        ProgressionService.Instance.Normalize(hero);
        ProgressionSlot? slot = hero.Progression.ClassSlots.Concat(hero.Progression.ProfessionSlots)
            .FirstOrDefault(candidate => candidate.SlotId == slotId);
        if (slot is not { IsLocked: false, Instance: not null } ||
            slot.Instance.Level < 10 || slot.Instance.Specialization != null)
            return Array.Empty<ProgressionSpecializationOffer>();

        ProgressionDefinition? definition = _data.FindDefinition(slot.Instance.DefinitionId);
        if (definition == null || definition.Specializations.Count == 0)
            return Array.Empty<ProgressionSpecializationOffer>();

        Dictionary<string, decimal> facts = BuildFacts(hero, contextFacts);
        string contextHash = BuildContextHash("specialization", slot.Domain, slotId, facts);
        var offers = new List<ProgressionSpecializationOffer>();
        foreach (ProgressionSpecializationDefinition specialization in definition.Specializations)
        {
            List<ProgressionUnlockRoute> satisfied = specialization.Routes
                .Where(route => route.AllOf.All(requirement => RequirementPasses(requirement, facts)))
                .ToList();
            if (satisfied.Count == 0) continue;
            offers.Add(new ProgressionSpecializationOffer
            {
                SourceDefinitionId = definition.Id,
                SpecializationId = specialization.Id,
                Name = specialization.Name,
                Description = specialization.Description,
                Domain = definition.Domain,
                Score = specialization.BaseOfferWeight + satisfied.Max(route => route.BaseScore) +
                    Math.Max(0, satisfied.Count - 1) * 5,
                ContextHash = contextHash,
                Explanations = satisfied.Select(route => route.Explanation)
                    .Where(explanation => !string.IsNullOrWhiteSpace(explanation)).ToList()
            });
        }
        return offers.OrderByDescending(offer => offer.Score)
            .ThenBy(offer => offer.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, _data.Catalog.OfferPresentationLimit))
            .ToList();
    }

    public IReadOnlyList<ProgressionAdvancementOffer> GenerateAdvancementOffers(Hero hero,
        string targetSlotId, IReadOnlyDictionary<string, decimal>? contextFacts = null)
    {
        ProgressionService.Instance.Normalize(hero);
        ProgressionSlot? target = hero.Progression.ClassSlots.Concat(hero.Progression.ProfessionSlots)
            .FirstOrDefault(candidate => candidate.SlotId == targetSlotId);
        if (target is not { IsLocked: false, Instance: not null } ||
            target.Instance.Level < target.Instance.MaxLevel)
            return Array.Empty<ProgressionAdvancementOffer>();

        List<ProgressionSlot> domainSlots = target.Domain == ProgressionDomain.Class
            ? hero.Progression.ClassSlots : hero.Progression.ProfessionSlots;
        Dictionary<string, decimal> facts = BuildFacts(hero, contextFacts);
        var offers = new List<ProgressionAdvancementOffer>();
        foreach (ProgressionAdvancementRecipe recipe in _data.AdvancementRecipes.Values
                     .Where(candidate => candidate.Domain == target.Domain))
        {
            List<ProgressionSlot>? sources = MatchSources(recipe, domainSlots, targetSlotId);
            if (sources == null || _data.FindDefinition(recipe.ResultDefinitionId) == null) continue;
            List<ProgressionUnlockRoute> satisfied = recipe.Routes.Count == 0
                ? new List<ProgressionUnlockRoute> { new() { Id = "mastery", BaseScore = 0 } }
                : recipe.Routes
                    .Where(route => route.AllOf.All(requirement => RequirementPasses(requirement, facts)))
                    .ToList();
            if (satisfied.Count == 0) continue;
            string sourceNames = string.Join(" + ", sources.Select(source => source.Instance!.Name));
            string subject = recipe.Id + "|" + string.Join(",", sources.Select(source => source.SlotId));
            var explanations = satisfied.Select(route => route.Explanation)
                .Where(explanation => !string.IsNullOrWhiteSpace(explanation)).ToList();
            explanations.Insert(0, $"Mastered sources: {sourceNames}.");
            offers.Add(new ProgressionAdvancementOffer
            {
                RecipeId = recipe.Id,
                ResultDefinitionId = recipe.ResultDefinitionId,
                Name = recipe.Name,
                Description = recipe.Description,
                Kind = recipe.Kind,
                Domain = recipe.Domain,
                Score = recipe.BaseOfferWeight + satisfied.Max(route => route.BaseScore) +
                    Math.Max(0, satisfied.Count - 1) * 5,
                ContextHash = BuildContextHash("advancement", target.Domain, subject, facts),
                TargetSlotId = targetSlotId,
                SourceSlotIds = sources.Select(source => source.SlotId).ToList(),
                Explanations = explanations
            });
        }
        return offers.OrderByDescending(offer => offer.Score)
            .ThenBy(offer => offer.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, _data.Catalog.OfferPresentationLimit))
            .ToList();
    }

    public Dictionary<string, decimal> BuildFacts(Hero hero,
        IReadOnlyDictionary<string, decimal>? contextFacts = null)
    {
        var facts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["character.exists"] = 1m,
            [$"race.{Slug(hero.Race)}"] = 1m,
            ["attribute.strength"] = hero.Strength,
            ["attribute.constitution"] = hero.Constitution,
            ["attribute.agility"] = hero.Agility,
            ["attribute.dexterity"] = hero.Dexterity,
            ["attribute.intelligence"] = hero.Intelligence,
            ["attribute.wisdom"] = hero.Wisdom,
            ["attribute.charisma"] = hero.Charisma
        };

        foreach ((MagicElement element, float value) in hero.Affinities.Values)
            facts[$"affinity.{Slug(element.ToString())}"] = (decimal)value;
        foreach (ProgressionSlot slot in hero.Progression.ClassSlots.Where(slot => slot.Instance != null))
        {
            Add(facts, $"class.{Slug(slot.Instance!.Name)}", slot.Instance.Level);
            AddPathHistoryFacts(facts, slot.Instance);
        }
        foreach (ProgressionSlot slot in hero.Progression.ProfessionSlots.Where(slot => slot.Instance != null))
        {
            Add(facts, $"profession.{Slug(slot.Instance!.Name)}", slot.Instance.Level);
            AddPathHistoryFacts(facts, slot.Instance);
        }
        foreach (SkillProgress skill in hero.Progression.Skills.Values)
            facts[$"skill.{Slug(skill.Name)}"] = skill.Level;
        foreach ((string factId, decimal value) in hero.Progression.Facts)
            facts[factId] = value;

        IEnumerable<Combinable> owned = hero.Equipment.Values.Concat(hero.Inventory).Concat(hero.Loadout);
        foreach (Combinable item in owned)
        {
            Add(facts, "equipment.item.any", 1m);
            Add(facts, $"equipment.item.{Slug(item.Id)}", 1m);
            switch (item)
            {
                case Weapon weapon:
                    Add(facts, "equipment.weapon.any", 1m);
                    Add(facts, $"equipment.weapon.{Slug(weapon.WeaponType.ToString())}", 1m);
                    Add(facts, weapon.Animation == AttackAnimation.Ranged
                        ? "equipment.weapon.ranged" : "equipment.weapon.melee", 1m);
                    if (weapon.HandsRequired > 1) Add(facts, "equipment.weapon.two-handed", 1m);
                    break;
                case Armor armor:
                    Add(facts, "equipment.armor.any", 1m);
                    foreach (GameAttribute attribute in armor.Attributes)
                        Add(facts, $"equipment.armor.{Slug(attribute.ToString())}", 1m);
                    break;
                case Spell:
                    Add(facts, "equipment.spell.any", 1m);
                    break;
            }
            foreach (GameAttribute attribute in item.Attributes)
                Add(facts, $"equipment.attribute.{Slug(attribute.ToString())}", 1m);
        }

        if (contextFacts != null)
            foreach ((string factId, decimal value) in contextFacts) facts[factId] = value;
        return facts;
    }

    private static bool RequirementPasses(ProgressionRequirement requirement,
        IReadOnlyDictionary<string, decimal> facts)
    {
        decimal actual = facts.GetValueOrDefault(requirement.FactId, 0m);
        bool result = requirement.Operator switch
        {
            ">" => actual > requirement.Threshold,
            "==" or "=" => actual == requirement.Threshold,
            "<=" => actual <= requirement.Threshold,
            "<" => actual < requirement.Threshold,
            "!=" => actual != requirement.Threshold,
            _ => actual >= requirement.Threshold
        };
        return requirement.Negated ? !result : result;
    }

    private static string BuildContextHash(string kind, ProgressionDomain domain, string subject,
        IReadOnlyDictionary<string, decimal> facts)
    {
        string canonical = kind + "|" + domain + "|" + subject + "|" + string.Join("|", facts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key.ToLowerInvariant() + "=" +
                pair.Value.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void Add(IDictionary<string, decimal> facts, string id, decimal amount)
    {
        facts.TryGetValue(id, out decimal current);
        facts[id] = current + amount;
    }

    private static void AddPathHistoryFacts(IDictionary<string, decimal> facts,
        ProgressionInstance instance)
    {
        if (instance.Specialization != null)
            Add(facts, $"specialization.{Slug(instance.Specialization.DefinitionId)}", 1m);
        foreach (ProgressionLineageEntry lineage in instance.Lineage)
        {
            Add(facts, $"lineage.{Slug(lineage.DefinitionId)}", 1m);
            if (!string.IsNullOrWhiteSpace(lineage.SpecializationId))
                Add(facts, $"lineage-specialization.{Slug(lineage.SpecializationId)}", 1m);
        }
    }

    private static List<ProgressionSlot>? MatchSources(ProgressionAdvancementRecipe recipe,
        IReadOnlyList<ProgressionSlot> slots, string targetSlotId)
    {
        var available = slots
            .Where(slot => !slot.IsLocked && slot.Instance != null)
            .OrderByDescending(slot => slot.SlotId == targetSlotId)
            .ThenBy(slot => slot.SlotId, StringComparer.Ordinal)
            .ToList();
        var selected = new List<ProgressionSlot>();
        IEnumerable<ProgressionAdvancementSourceRequirement> requirements = recipe.Sources
            .SelectMany(source => Enumerable.Repeat(source, Math.Max(1, source.Count)))
            .OrderByDescending(source => !string.IsNullOrWhiteSpace(source.RequiredSpecializationId));
        foreach (ProgressionAdvancementSourceRequirement requirement in requirements)
        {
            ProgressionSlot? match = available.FirstOrDefault(slot => SourceMatches(slot, requirement));
            if (match == null) return null;
            selected.Add(match);
            available.Remove(match);
        }
        return selected.Any(slot => slot.SlotId == targetSlotId) ? selected : null;
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

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool separator = false;
        foreach (char character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separator && builder.Length > 0) builder.Append('-');
                builder.Append(character);
                separator = false;
            }
            else separator = true;
        }
        return builder.ToString();
    }
}

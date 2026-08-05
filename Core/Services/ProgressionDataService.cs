using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Loads progression paths and skill curves from Data/Progression/progression.json.</summary>
public sealed class ProgressionDataService
{
    private static ProgressionDataService? _instance;
    public static ProgressionDataService Instance => _instance ??= new ProgressionDataService();

    public ProgressionCatalog Catalog { get; }
    public IReadOnlyDictionary<string, ProgressionDefinition> Definitions { get; }
    public IReadOnlyDictionary<string, ProgressionAdvancementRecipe> AdvancementRecipes { get; }
    public IReadOnlyDictionary<string, ProgressionSkillDefinition> Skills { get; }

    public ProgressionDataService()
    {
        Catalog = LoadCatalog();
        Definitions = Catalog.Definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        AdvancementRecipes = Catalog.AdvancementRecipes.ToDictionary(
            recipe => recipe.Id, StringComparer.OrdinalIgnoreCase);
        Skills = Catalog.Skills.ToDictionary(skill => skill.Id, StringComparer.OrdinalIgnoreCase);
    }

    public ProgressionDefinition? FindDefinition(string idOrName) =>
        Definitions.GetValueOrDefault(idOrName) ??
        Definitions.Values.FirstOrDefault(definition =>
            string.Equals(definition.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    private static ProgressionCatalog LoadCatalog()
    {
        try
        {
            string path = GamePaths.Content("Data", "Progression", "progression.json");
            if (File.Exists(path))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                ProgressionCatalog? catalog = JsonSerializer.Deserialize<ProgressionCatalog>(
                    File.ReadAllText(path), options);
                if (catalog is { Definitions.Count: > 0 }) return catalog;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to load progression catalog, using defaults: {ex.Message}");
        }

        return BuildFallbackCatalog();
    }

    private static ProgressionCatalog BuildFallbackCatalog()
    {
        var classXp = Enumerable.Range(1, 24).Select(level => 100 * level * level).ToList();
        var professionXp = Enumerable.Range(1, 24).Select(level => 50 * level * level).ToList();
        var catalog = new ProgressionCatalog();

        void AddClass(string id, string name, params (string stat, int weight)[] weights) =>
            catalog.Definitions.Add(new ProgressionDefinition
            {
                Id = id, Name = name, Domain = ProgressionDomain.Class,
                AttributePointsPerLevel = 4, XpToNextLevel = new List<int>(classXp),
                AutoAttributeWeights = weights.ToDictionary(pair => pair.stat, pair => pair.weight)
            });

        AddClass("wanderer", "Wanderer", ("Strength", 1), ("Constitution", 1), ("Agility", 1),
            ("Dexterity", 1), ("Intelligence", 1), ("Wisdom", 1), ("Charisma", 1));
        AddClass("warrior", "Warrior", ("Strength", 3), ("Constitution", 3), ("Dexterity", 1));
        AddClass("archer", "Archer", ("Agility", 3), ("Dexterity", 3), ("Wisdom", 1));
        AddClass("rogue", "Rogue", ("Agility", 3), ("Dexterity", 3), ("Charisma", 1));
        AddClass("priest", "Priest", ("Wisdom", 3), ("Charisma", 2), ("Constitution", 1));
        AddClass("mage-apprentice", "Mage Apprentice", ("Intelligence", 3), ("Wisdom", 2), ("Dexterity", 1));
        AddClass("bard", "Bard", ("Charisma", 3), ("Dexterity", 2), ("Agility", 1));
        AddClass("healer", "Healer", ("Wisdom", 3), ("Intelligence", 2), ("Charisma", 1));
        AddClass("alchemist", "Alchemist", ("Intelligence", 3), ("Wisdom", 2), ("Dexterity", 1));
        AddClass("mage", "Mage", ("Intelligence", 3), ("Wisdom", 2), ("Dexterity", 1));
        AddClass("spellsword", "Spellsword", ("Strength", 2), ("Intelligence", 2),
            ("Dexterity", 1), ("Constitution", 1));
        catalog.Definitions.First(item => item.Id == "healer").Description =
            "A practical restoration path focused on stabilization, cleansing, recovery, and keeping a party alive under pressure.";
        catalog.Definitions.First(item => item.Id == "alchemist").Description =
            "A combat-reaction path using bombs, catalysts, toxins, mutagens, transmutation, and prepared battlefield formulas.";
        catalog.Definitions.First(item => item.Id == "healer").BaseOfferWeight = 20;
        catalog.Definitions.First(item => item.Id == "alchemist").BaseOfferWeight = 20;

        void AddInitialRoute(string definitionId, string routeId, string factId, int score,
            decimal threshold = 1m, string? explanation = null)
        {
            ProgressionDefinition definition = catalog.Definitions.First(item => item.Id == definitionId);
            definition.InitialRoutes.Add(new ProgressionUnlockRoute
            {
                Id = routeId, BaseScore = score,
                Explanation = explanation ?? $"Your equipment supports {definition.Name}.",
                AllOf = new List<ProgressionRequirement>
                {
                    new() { FactId = factId, Threshold = threshold }
                }
            });
        }

        AddInitialRoute("wanderer", "open-road", "character.exists", 10);
        AddInitialRoute("warrior", "sword", "equipment.weapon.sword", 100);
        AddInitialRoute("archer", "bow", "equipment.weapon.bow", 110);
        AddInitialRoute("rogue", "dagger", "equipment.weapon.dagger", 100);
        AddInitialRoute("priest", "blessed", "equipment.attribute.blessed", 110);
        AddInitialRoute("mage-apprentice", "staff", "equipment.weapon.staff", 110);
        AddInitialRoute("healer", "practical-care", "practice.healing-actions", 110, 3,
            "Repeated treatment under pressure reveals a practical restoration path.");
        AddInitialRoute("healer", "healer-training", "training.healer", 100, 1,
            "Formal instruction gives you the foundation to serve as a party healer.");
        AddInitialRoute("alchemist", "combat-reactions", "practice.alchemical-combat-actions", 110, 3,
            "Repeated use of prepared reactions in danger reveals a combat-alchemy path.");
        AddInitialRoute("alchemist", "repeatable-reaction", "knowledge.alchemical-reactions", 100, 1,
            "Developing a repeatable reagent reaction reveals an alchemical foundation.");
        AddInitialRoute("alchemist", "alchemist-training", "training.alchemist", 100, 1,
            "Formal alchemical instruction gives you a foundation for dangerous field formulas.");
        ProgressionDefinition healer = catalog.Definitions.First(item => item.Id == "healer");
        healer.InitialRoutes.Add(new ProgressionUnlockRoute
        {
            Id = "life-restoration", BaseScore = 100,
            Explanation = "Demonstrated healing supported by Life affinity reveals a restorative path.",
            AllOf = new List<ProgressionRequirement>
            {
                new() { FactId = "practice.healing-actions" },
                new() { FactId = "affinity.life", Threshold = 30 }
            }
        });
        catalog.Definitions.First(item => item.Id == "warrior").WeaponAffinities.Add(WeaponType.Sword);
        catalog.Definitions.First(item => item.Id == "archer").WeaponAffinities.Add(WeaponType.Bow);
        catalog.Definitions.First(item => item.Id == "rogue").WeaponAffinities.Add(WeaponType.Dagger);
        catalog.Definitions.First(item => item.Id == "mage-apprentice").WeaponAffinities.Add(WeaponType.Staff);
        catalog.Definitions.First(item => item.Id == "mage").WeaponAffinities.Add(WeaponType.Staff);
        catalog.Definitions.First(item => item.Id == "spellsword").WeaponAffinities
            .UnionWith(new[] { WeaponType.Sword, WeaponType.Staff });

        void AddSpecialization(string definitionId, string id, string name, string description,
            string factId, decimal threshold = 1m)
        {
            catalog.Definitions.First(item => item.Id == definitionId).Specializations.Add(
                new ProgressionSpecializationDefinition
                {
                    Id = id, Name = name, Description = description, BaseOfferWeight = 20,
                    Routes = new List<ProgressionUnlockRoute>
                    {
                        new()
                        {
                            Id = id + "-evidence", BaseScore = 100,
                            Explanation = $"Your demonstrated practice reveals {name}.",
                            AllOf = new List<ProgressionRequirement>
                            {
                                new() { FactId = factId, Threshold = threshold }
                            }
                        }
                    }
                });
        }

        AddSpecialization("wanderer", "bard", "Bard", "A practiced traveling performer.",
            "practice.performance-actions", 3);
        AddSpecialization("warrior", "swordsman", "Swordsman", "A dedicated sword practitioner.",
            "equipment.weapon.sword");
        AddSpecialization("warrior", "shieldbearer", "Shieldbearer", "A dedicated protector.",
            "practice.blocked-damage-for-allies", 3);
        AddSpecialization("warrior", "berserker", "Berserker", "A controlled aggression specialist.",
            "practice.fought-while-injured", 3);
        AddSpecialization("warrior", "squire", "Squire", "A trained martial attendant.",
            "training.squire");
        AddSpecialization("rogue", "thief", "Thief", "A lock, trap, and infiltration specialist.",
            "practice.lockpick-actions", 3);
        AddSpecialization("mage-apprentice", "necromancer", "Necromancer",
            "A controlled Death-magic practitioner.", "practice.death-spells", 3);
        AddSpecialization("mage-apprentice", "elementalist", "Elementalist",
            "A student of multiple elemental sources.", "practice.distinct-elements", 2);

        catalog.Definitions.Add(new ProgressionDefinition
        {
            Id = "miner", Name = "Miner", Domain = ProgressionDomain.Profession,
            XpToNextLevel = professionXp, XpCurveMultiplier = 50, ActionXp = 25,
            FixedAttributesPerLevel = new Dictionary<string, int> { ["Strength"] = 1 },
            AssociatedSkills = new List<string> { "mining" },
            InitialRoutes = new List<ProgressionUnlockRoute>
            {
                new()
                {
                    Id = "practical-mining", BaseScore = 100,
                    Explanation = "Your practical ore extraction revealed the Miner profession.",
                    AllOf = new List<ProgressionRequirement> { new() { FactId = "practice.mining-actions" } }
                }
            }
        });
        catalog.Definitions.Add(new ProgressionDefinition
        {
            Id = "apothecary", Name = "Apothecary", Domain = ProgressionDomain.Profession,
            XpToNextLevel = professionXp, XpCurveMultiplier = 50, ActionXp = 25,
            FixedAttributesPerLevel = new Dictionary<string, int> { ["Wisdom"] = 1 },
            AssociatedSkills = new List<string> { "herbalism" }
        });
        AddSpecialization("miner", "prospector", "Prospector",
            "A specialist in locating and evaluating deposits.", "practice.mining-actions", 10);
        catalog.AdvancementRecipes.Add(new ProgressionAdvancementRecipe
        {
            Id = "mage-apprentice-to-mage", ResultDefinitionId = "mage", Name = "Mage",
            Description = "Advance beyond apprenticeship into independent structured spellcraft.",
            Kind = ProgressionAdvancementKind.SinglePath, Domain = ProgressionDomain.Class,
            BaseOfferWeight = 20,
            Sources = new List<ProgressionAdvancementSourceRequirement>
            {
                new() { DefinitionId = "mage-apprentice", MinimumLevel = 25 }
            },
            Routes = new List<ProgressionUnlockRoute>
            {
                new()
                {
                    Id = "independent-spellcraft", BaseScore = 110,
                    Explanation = "Your independent spell construction demonstrates mastery beyond apprenticeship.",
                    AllOf = new List<ProgressionRequirement>
                    {
                        new() { FactId = "knowledge.independent-spell-construction" }
                    }
                }
            }
        });
        catalog.AdvancementRecipes.Add(new ProgressionAdvancementRecipe
        {
            Id = "warrior-mage-to-spellsword", ResultDefinitionId = "spellsword", Name = "Spellsword",
            Description = "Converge martial and structured magical mastery.",
            Kind = ProgressionAdvancementKind.Convergence, Domain = ProgressionDomain.Class,
            BaseOfferWeight = 25,
            Sources = new List<ProgressionAdvancementSourceRequirement>
            {
                new() { DefinitionId = "warrior", MinimumLevel = 25 },
                new() { DefinitionId = "mage-apprentice", MinimumLevel = 25 }
            },
            Routes = new List<ProgressionUnlockRoute>
            {
                new()
                {
                    Id = "spell-melee-integration", BaseScore = 120,
                    Explanation = "You have practiced sustaining spell structures during weapon combat.",
                    AllOf = new List<ProgressionRequirement>
                    {
                        new() { FactId = "practice.spell-melee-integration", Threshold = 3 }
                    }
                }
            }
        });
        catalog.Skills.Add(new ProgressionSkillDefinition
        {
            Id = "mining", Name = "Mining", XpToNextLevel = professionXp,
            YieldBonusPerLevel = 0.1f, BonusDropChancePerLevel = 0.02f
        });
        catalog.Skills.Add(new ProgressionSkillDefinition
        {
            Id = "herbalism", Name = "Herbalism", XpToNextLevel = professionXp,
            YieldBonusPerLevel = 0.1f, BonusDropChancePerLevel = 0.02f
        });
        return catalog;
    }
}

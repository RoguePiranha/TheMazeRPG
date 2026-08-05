using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

public class CharacterDataService
{
    private Dictionary<string, CharacterClass> _classes = new();
    private Dictionary<string, CharacterRace> _races = new();
    
    public IReadOnlyDictionary<string, CharacterClass> Classes => _classes;
    public IReadOnlyDictionary<string, CharacterRace> Races => _races;
    
    public CharacterDataService()
    {
        LoadClasses();
        LoadRaces();
    }
    
    private void LoadClasses()
    {
        try
        {
            var classesPath = GamePaths.Content("Data", "Classes", "classes.json");
            GameLog.Debug($"Attempting to load classes from: {classesPath} (exists: {File.Exists(classesPath)})");

            if (File.Exists(classesPath))
            {
                var json = File.ReadAllText(classesPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                _classes = JsonSerializer.Deserialize<Dictionary<string, CharacterClass>>(json, options)
                    ?? new Dictionary<string, CharacterClass>();
                GameLog.Debug($"Loaded {_classes.Count} classes");
            }
            else
            {
                Console.WriteLine("WARNING: classes.json not found!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading classes: {ex.Message}");
        }
    }
    
    private void LoadRaces()
    {
        try
        {
            var racesPath = GamePaths.Content("Data", "Races", "races.json");
            GameLog.Debug($"Attempting to load races from: {racesPath} (exists: {File.Exists(racesPath)})");

            if (File.Exists(racesPath))
            {
                var json = File.ReadAllText(racesPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                _races = JsonSerializer.Deserialize<Dictionary<string, CharacterRace>>(json, options)
                    ?? new Dictionary<string, CharacterRace>();
                GameLog.Debug($"Loaded {_races.Count} races");
            }
            else
            {
                Console.WriteLine("WARNING: races.json not found!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading races: {ex.Message}");
        }
    }
    
    public void ApplyClassAndRace(Hero hero, string className, string raceName)
    {
        GameLog.Debug($"ApplyClassAndRace called: {className}, {raceName} (classes: {_classes.Count}, races: {_races.Count})");

        if (!_classes.ContainsKey(className))
        {
            Console.WriteLine($"WARNING: Class '{className}' not found. Available: {string.Join(", ", _classes.Keys)}");
        }

        if (!_races.ContainsKey(raceName))
        {
            Console.WriteLine($"WARNING: Race '{raceName}' not found. Available: {string.Join(", ", _races.Keys)}");
        }
        
        if (!_classes.ContainsKey(className) || !_races.ContainsKey(raceName))
            return;
            
        var characterClass = _classes[className];
        var race = _races[raceName];
        
        hero.Class = className;
        hero.Race = raceName;
        hero.ClassColor = characterClass.Color;
        hero.RaceColor = race.Color;
        hero.ClassData = characterClass; // Store class data for stat growth
        
        GameLog.Debug($"Applied colors - Class: {hero.ClassColor}, Race: {hero.RaceColor}");
        
        // Apply starting stats from class
        if (characterClass.StartingStats.TryGetValue("Strength", out var str))
            hero.Strength = str;
        if (characterClass.StartingStats.TryGetValue("Constitution", out var con))
            hero.Constitution = con;
        if (characterClass.StartingStats.TryGetValue("Agility", out var agi))
            hero.Agility = agi;
        if (characterClass.StartingStats.TryGetValue("Dexterity", out var dex))
            hero.Dexterity = dex;
        if (characterClass.StartingStats.TryGetValue("Intelligence", out var intel))
            hero.Intelligence = intel;
        if (characterClass.StartingStats.TryGetValue("Wisdom", out var wis))
            hero.Wisdom = wis;
        if (characterClass.StartingStats.TryGetValue("Charisma", out var cha))
            hero.Charisma = cha;

        ApplyRaceDetails(hero, race, characterClass.Affinities);
    }

    public void ApplyClasslessAndRace(Hero hero, string raceName)
    {
        if (!_races.TryGetValue(raceName, out CharacterRace? race)) return;
        hero.Class = "Classless";
        hero.ClassColor = "#808080";
        hero.ClassData = null;
        hero.Race = raceName;
        hero.Strength = 4;
        hero.Constitution = 4;
        hero.Agility = 4;
        hero.Dexterity = 4;
        hero.Intelligence = 4;
        hero.Wisdom = 4;
        hero.Charisma = 4;
        ApplyRaceDetails(hero, race, null);
    }

    /// <summary>Apply class identity after an offer is accepted without resetting earned stats.</summary>
    public bool ApplyClassIdentity(Hero hero, string className)
    {
        if (!_classes.TryGetValue(className, out CharacterClass? characterClass)) return false;
        hero.Class = className;
        hero.ClassColor = characterClass.Color;
        hero.ClassData = characterClass;
        ApplyClassAffinities(hero, className);
        return true;
    }

    /// <summary>Add a class's permanent affinity leanings without changing the primary identity.</summary>
    public bool ApplyClassAffinities(Hero hero, string className)
    {
        if (!_classes.TryGetValue(className, out CharacterClass? characterClass)) return false;
        AffinityService.SeedFrom(hero.Affinities, characterClass.Affinities);
        return true;
    }

    private static void ApplyRaceDetails(Hero hero, CharacterRace race,
        IReadOnlyDictionary<string, float>? classAffinities)
    {
        hero.RaceColor = race.Color;

        // Testing races (e.g. Debug) can flat-override base stats after the class stats.
        if (race.StatOverrides != null)
        {
            if (race.StatOverrides.TryGetValue("Strength", out var oStr)) hero.Strength = oStr;
            if (race.StatOverrides.TryGetValue("Constitution", out var oCon)) hero.Constitution = oCon;
            if (race.StatOverrides.TryGetValue("Agility", out var oAgi)) hero.Agility = oAgi;
            if (race.StatOverrides.TryGetValue("Dexterity", out var oDex)) hero.Dexterity = oDex;
            if (race.StatOverrides.TryGetValue("Intelligence", out var oInt)) hero.Intelligence = oInt;
            if (race.StatOverrides.TryGetValue("Wisdom", out var oWis)) hero.Wisdom = oWis;
            if (race.StatOverrides.TryGetValue("Charisma", out var oCha)) hero.Charisma = oCha;
        }

        // Seed the hero's elemental affinity leanings from class + race (added over the neutral
        // baseline). Play shifts them from here via AffinityService.OnElementCast.
        hero.Affinities = new Affinities();
        AffinityService.SeedFrom(hero.Affinities, classAffinities, race.Affinities);

        // Apply racial effectiveness multipliers. These do NOT alter the base stats above;
        // they scale how effectively each stat translates into results (Info/Racial Effectiveness.md).
        hero.StrengthEffectiveness = Effectiveness(race, "Strength");
        hero.ConstitutionEffectiveness = Effectiveness(race, "Constitution");
        hero.AgilityEffectiveness = Effectiveness(race, "Agility");
        hero.DexterityEffectiveness = Effectiveness(race, "Dexterity");
        hero.IntelligenceEffectiveness = Effectiveness(race, "Intelligence");
        hero.WisdomEffectiveness = Effectiveness(race, "Wisdom");
        hero.CharismaEffectiveness = Effectiveness(race, "Charisma");

        // Racial darkvision (D&D-mapped, races.json) — drives night visibility in town.
        hero.HasDarkvision = race.Darkvision;
    }

    // Effectiveness multiplier for a stat, defaulting to 1.0 when the race doesn't specify one.
    private static float Effectiveness(CharacterRace race, string stat) =>
        race.Effectiveness.TryGetValue(stat, out var m) ? m : 1.0f;
}

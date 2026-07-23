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
            var classesPath = Path.Combine("Data", "Classes", "classes.json");
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
            var racesPath = Path.Combine("Data", "Races", "races.json");
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
            
        // Apply race modifiers on top
        if (race.StatModifiers.TryGetValue("Strength", out var strMod))
            hero.Strength += strMod;
        if (race.StatModifiers.TryGetValue("Constitution", out var conMod))
            hero.Constitution += conMod;
        if (race.StatModifiers.TryGetValue("Agility", out var agiMod))
            hero.Agility += agiMod;
        if (race.StatModifiers.TryGetValue("Dexterity", out var dexMod))
            hero.Dexterity += dexMod;
        if (race.StatModifiers.TryGetValue("Intelligence", out var intMod))
            hero.Intelligence += intMod;
        if (race.StatModifiers.TryGetValue("Wisdom", out var wisMod))
            hero.Wisdom += wisMod;
        if (race.StatModifiers.TryGetValue("Charisma", out var chaMod))
            hero.Charisma += chaMod;
            
        // Ensure no stat goes below 0
        hero.Strength = Math.Max(0, hero.Strength);
        hero.Constitution = Math.Max(0, hero.Constitution);
        hero.Agility = Math.Max(0, hero.Agility);
        hero.Dexterity = Math.Max(0, hero.Dexterity);
        hero.Intelligence = Math.Max(0, hero.Intelligence);
        hero.Wisdom = Math.Max(0, hero.Wisdom);
        hero.Charisma = Math.Max(0, hero.Charisma);
    }
}

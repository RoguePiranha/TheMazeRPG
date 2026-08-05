using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Loads Data/Materials/materials.json — the same "JSON content, eagerly loaded into a
/// Dictionary" convention as CharacterDataService's classes/races. Exposed as a static
/// singleton (like GameSettings.Current / CodexService.Instance), not an instance field on
/// GameState like CharacterDataService, because materials are read-only static content that
/// needs to be referenceable from anywhere validating a resource id (e.g. GameState.AddHeroResource)
/// without threading an instance through.
/// </summary>
public class MaterialDataService
{
    private static MaterialDataService? _instance;
    public static MaterialDataService Instance => _instance ??= Load();

    private Dictionary<string, MaterialDef> _materials = new();
    public IReadOnlyDictionary<string, MaterialDef> Materials => _materials;

    public bool IsValidMaterial(string id) => _materials.ContainsKey(id);

    private static MaterialDataService Load()
    {
        var service = new MaterialDataService();
        try
        {
            var path = GamePaths.Content("Data", "Materials", "materials.json");
            GameLog.Debug($"Attempting to load materials from: {path} (exists: {File.Exists(path)})");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                service._materials = JsonSerializer.Deserialize<Dictionary<string, MaterialDef>>(json, options)
                    ?? new Dictionary<string, MaterialDef>();
                GameLog.Debug($"Loaded {service._materials.Count} materials");
            }
            else
            {
                Console.WriteLine("WARNING: materials.json not found!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading materials: {ex.Message}");
        }
        return service;
    }
}

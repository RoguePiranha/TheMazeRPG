using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Loads Data/Recipes/recipes.json — same static-singleton content pattern as
/// MaterialDataService.</summary>
public class RecipeDataService
{
    private static RecipeDataService? _instance;
    public static RecipeDataService Instance => _instance ??= Load();

    private Dictionary<string, RecipeDef> _recipes = new();
    public IReadOnlyDictionary<string, RecipeDef> Recipes => _recipes;

    public RecipeDef? Get(string id) => _recipes.GetValueOrDefault(id);

    private static RecipeDataService Load()
    {
        var service = new RecipeDataService();
        try
        {
            var path = GamePaths.Content("Data", "Recipes", "recipes.json");
            GameLog.Debug($"Attempting to load recipes from: {path} (exists: {File.Exists(path)})");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                service._recipes = JsonSerializer.Deserialize<Dictionary<string, RecipeDef>>(json, options)
                    ?? new Dictionary<string, RecipeDef>();
                GameLog.Debug($"Loaded {service._recipes.Count} recipes");
            }
            else
            {
                Console.WriteLine("WARNING: recipes.json not found!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading recipes: {ex.Message}");
        }
        return service;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Writes/reads hero save slots to Saves/{SaveId}.json — one file per character, the same
/// JSON-file convention as CodexService, but a plain static utility (Save/Load/List) rather than
/// a singleton holding live data, since there's no ongoing in-memory state to track between
/// discrete save/load actions.
/// </summary>
public static class SaveService
{
    // A subdirectory of Saves/, not Saves/ itself — CodexService also writes Saves/codex.json,
    // and a naive glob over Saves/*.json would misparse that as a phantom (empty) save slot.
    private static readonly string SavesDirectory = Path.Combine("Saves", "Characters");
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static string PathFor(string saveId) => Path.Combine(SavesDirectory, $"{saveId}.json");

    public static bool HasAnySaves() =>
        Directory.Exists(SavesDirectory) && Directory.EnumerateFiles(SavesDirectory, "*.json").Any();

    /// <summary>Permanently remove a save slot (the saves picker's Delete action).</summary>
    public static void Delete(string saveId)
    {
        try
        {
            var path = PathFor(saveId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting save '{saveId}': {ex.Message}");
        }
    }

    /// <summary>Summary info for every save slot on disk (for the Continue/saves picker), newest first.</summary>
    public static List<SaveSummary> ListSaves()
    {
        var results = new List<SaveSummary>();
        if (!Directory.Exists(SavesDirectory)) return results;

        foreach (var file in Directory.EnumerateFiles(SavesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<SaveData>(json, ReadOptions);
                if (data == null) continue;
                results.Add(new SaveSummary
                {
                    SaveId = data.SaveId,
                    HeroName = data.HeroName,
                    ClassName = data.ClassName,
                    RaceName = data.RaceName,
                    Level = data.Level,
                    PlaytimeSeconds = data.PlaytimeSeconds,
                    SavedAtUtc = data.SavedAtUtc
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading save '{file}': {ex.Message}");
            }
        }

        return results.OrderByDescending(s => s.SavedAtUtc).ToList();
    }

    /// <summary>Snapshot the hero's current progress to disk, into their own save slot
    /// (GameState.SaveId). Only meaningful in the Overworld or a safe room (the two places
    /// saving is allowed) — regular dungeon floors are transient and never saved.</summary>
    public static void Save(GameState gameState)
    {
        var hero = gameState.Hero;
        var data = new SaveData
        {
            SaveId = gameState.SaveId,
            PlaytimeSeconds = gameState.TotalPlaytimeSeconds,
            SavedAtUtc = DateTime.UtcNow,
            // Where this save resumes, derived from where the hero is right now: a safe-room
            // checkpoint, the Overworld, or (neither — a brand-new character saved at creation,
            // before their first safe room) a fresh dive from floor 1.
            ResumePoint = gameState.IsInSafeRoom ? ResumePoint.SafeRoom
                : gameState.IsInOverworld ? ResumePoint.OverworldEntrance
                : ResumePoint.DungeonStart,
            SafeRoomFloor = gameState.IsInSafeRoom ? gameState.CurrentFloor : null,
            HeroName = hero.Name,
            ClassName = hero.Class,
            RaceName = hero.Race,
            Level = hero.Level,
            Experience = hero.Experience,
            ExperienceToNext = hero.ExperienceToNext,
            MaxHp = hero.MaxHp,
            CurrentHp = hero.CurrentHp,
            Strength = hero.Strength,
            Constitution = hero.Constitution,
            Agility = hero.Agility,
            Dexterity = hero.Dexterity,
            Intelligence = hero.Intelligence,
            Wisdom = hero.Wisdom,
            Charisma = hero.Charisma,
            Gold = hero.Gold,
            Resources = new Dictionary<string, int>(hero.Resources),
            Loadout = new List<Combinable>(hero.Loadout),
            Inventory = new List<Combinable>(hero.Inventory)
        };

        try
        {
            Directory.CreateDirectory(SavesDirectory);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathFor(data.SaveId), json);
            GameLog.Debug($"Saved progress: {hero.Name} the {hero.Race} {hero.Class}, Level {hero.Level}, Gold {hero.Gold}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving progress: {ex.Message}");
        }
    }

    public static SaveData? Load(string saveId)
    {
        try
        {
            var path = PathFor(saveId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SaveData>(json, ReadOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading save '{saveId}': {ex.Message}");
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Writes/reads hero save slots to Saves/Worlds/{worldId}/Characters/{SaveId}.json — one file per
/// character, the same JSON-file convention as CodexService, but a plain static utility
/// (Save/Load/List) rather than a singleton holding live data, since there's no ongoing in-memory
/// state to track between discrete save/load actions.
/// </summary>
public static class SaveService
{
    // Characters live inside a world (see WorldService): a world's generated map is shared by every
    // character in it, and the world outlives them. EnsureActiveWorld resolves (and if necessary
    // creates) that scope, so callers who don't care about worlds — the headless TEST_* demos, the
    // frozen Avalonia client — keep working without a world-selection step of their own.
    // Still a subdirectory rather than the world folder itself: delta.json/world.json live there,
    // and a naive glob would misparse them as phantom save slots.
    private static string SavesDirectory =>
        WorldService.CharactersDirectory(WorldService.EnsureActiveWorld());
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
            Version = 5,
            SaveId = gameState.SaveId,
            WorldId = gameState.WorldId,
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
            CreationSelection = gameState.CreationSelection,
            Level = hero.Progression.CharacterLevel,
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
            UnspentStatPoints = hero.UnspentStatPoints,
            Progression = hero.Progression,
            Gold = hero.Gold,
            Resources = new Dictionary<string, int>(hero.Resources),
            Loadout = new List<Combinable>(hero.Loadout),
            Inventory = new List<Combinable>(hero.Inventory),
            Equipment = new Dictionary<EquipmentSlot, Combinable>(hero.Equipment),
            WeaponTraining = new HashSet<WeaponType>(hero.WeaponTraining),
            Affinities = hero.Affinities.Clone(),
            Kills = hero.Kills,
            WorldGameMinutes = gameState.Clock.TotalGameMinutes,
            HotbarAssignments = new List<string?>(hero.HotbarAssignments),
            HotbarKnownIds = new List<string>(hero.HotbarKnownIds),
            NpcOpinions = new Dictionary<string, int>(hero.NpcOpinions),
            NpcLastTalkDay = new Dictionary<string, int>(hero.NpcLastTalkDay)
        };

        try
        {
            Directory.CreateDirectory(SavesDirectory);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            // Atomic replace: a crash mid-save must never truncate the only copy of the slot.
            AtomicFile.WriteAllText(PathFor(data.SaveId), json);
            // Keep the world's displayed elapsed time in step with its furthest-travelled character.
            WorldService.RecordWorldTime(data.WorldId, data.WorldGameMinutes);
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

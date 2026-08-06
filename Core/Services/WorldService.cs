using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Owns the world layer of the save tree: Saves/Worlds/{worldId}/{world.json, delta.json, Characters/}.
/// A plain static utility in SaveService's mould — worlds are read and written at discrete moments
/// (creation, load, death), so there's no live in-memory state worth holding beyond which world is
/// currently active.
///
/// Why worlds exist above characters (owner rulings 2026-08-05): worlds are generated from player
/// options and then frozen, so every character in a world must share one map; and character death
/// deletes the character but never the world, so the world's memory has to outlive the character
/// file that triggered it.
/// </summary>
public static class WorldService
{
    private static string WorldsRoot => GamePaths.Save("Saves", "Worlds");
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static string? _activeWorldId;
    private static WorldProfile? _activeProfile;
    private static bool _initialized;

    public static string WorldDirectory(string worldId) => Path.Combine(WorldsRoot, worldId);
    public static string CharactersDirectory(string worldId) => Path.Combine(WorldDirectory(worldId), "Characters");
    private static string WorldFile(string worldId) => Path.Combine(WorldDirectory(worldId), "world.json");
    private static string DeltaFile(string worldId) => Path.Combine(WorldDirectory(worldId), "delta.json");

    /// <summary>
    /// The world every save/load operation is scoped to. The Godot worlds screen sets this
    /// explicitly; anything that doesn't (headless demos, the frozen Avalonia client) inherits
    /// EnsureActiveWorld's fallback instead of needing to know worlds exist at all.
    /// </summary>
    public static string? ActiveWorldId
    {
        get => _activeWorldId;
        set
        {
            if (_activeWorldId == value) return;
            _activeWorldId = value;
            _activeProfile = null; // re-resolve the hostility/size knobs for the new world
        }
    }

    /// <summary>
    /// Resolves the world all character saves belong to, creating one if the player has never made
    /// a world. This is what lets every existing caller keep working unchanged: without it, each
    /// of them would need a world-selection step before they could touch a save.
    /// </summary>
    public static string EnsureActiveWorld()
    {
        Initialize();
        if (!string.IsNullOrEmpty(_activeWorldId) && Directory.Exists(WorldDirectory(_activeWorldId!)))
            return _activeWorldId!;

        var mostRecent = ListWorlds().FirstOrDefault();
        if (mostRecent != null)
        {
            ActiveWorldId = mostRecent.WorldId;
            return _activeWorldId!;
        }

        var created = Create(new WorldGenOptions { Seed = new Random().Next() }, "Origins");
        ActiveWorldId = created.WorldId;
        return created.WorldId;
    }

    /// <summary>
    /// One-shot startup housekeeping. Removes the pre-split Saves/Characters/ folder: those saves
    /// predate worlds and are unreachable under the new layout (owner ruling 2026-08-05 — pre-split
    /// saves are playtests, and compatibility machinery never gates updates).
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var legacyCharacters = GamePaths.Save("Saves", "Characters");
            if (Directory.Exists(legacyCharacters))
            {
                int count = Directory.EnumerateFiles(legacyCharacters, "*.json").Count();
                Directory.Delete(legacyCharacters, recursive: true);
                GameLog.Debug($"Removed {count} pre-split character save(s) from Saves/Characters (superseded by the world layout).");
            }
        }
        catch (Exception ex)
        {
            // Never block startup over housekeeping — the folder is simply ignored if it survives.
            GameLog.Debug($"Could not remove pre-split character saves: {ex.Message}");
        }
    }

    public static WorldData Create(WorldGenOptions options, string name)
    {
        Initialize();

        var world = new WorldData
        {
            WorldId = Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed World" : name.Trim(),
            Options = options.Clone(),
            // Generation hasn't run yet (PR 6 owns the region generator). When it does, a failed
            // grammar assert retries at seed+1 and records the seed that actually worked here.
            EffectiveSeed = options.Seed,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            Directory.CreateDirectory(CharactersDirectory(world.WorldId));
            File.WriteAllText(WorldFile(world.WorldId), JsonSerializer.Serialize(world, WriteOptions));
            SaveDelta(world.WorldId, new WorldDelta());
            GameLog.Debug($"Created world '{world.Name}' ({world.Options.Size}, {world.Options.Hostility}, seed {world.EffectiveSeed}).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating world '{world.Name}': {ex.Message}");
        }

        return world;
    }

    public static WorldData? Load(string worldId)
    {
        try
        {
            var path = WorldFile(worldId);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<WorldData>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading world '{worldId}': {ex.Message}");
            return null;
        }
    }

    public static bool HasAnyWorlds()
    {
        Initialize();
        return Directory.Exists(WorldsRoot) && Directory.EnumerateDirectories(WorldsRoot).Any();
    }

    /// <summary>Every world on disk, newest-created first, for the worlds picker.</summary>
    public static List<WorldSummary> ListWorlds()
    {
        Initialize();
        var results = new List<WorldSummary>();
        if (!Directory.Exists(WorldsRoot)) return results;

        foreach (var directory in Directory.EnumerateDirectories(WorldsRoot))
        {
            var worldId = Path.GetFileName(directory);
            var world = Load(worldId);
            if (world == null) continue;

            var delta = LoadDelta(worldId);
            results.Add(new WorldSummary
            {
                WorldId = worldId,
                Name = world.Name,
                Size = world.Options.Size,
                Hostility = world.Options.Hostility,
                Seed = world.EffectiveSeed,
                CreatedAtUtc = world.CreatedAtUtc,
                Day = (int)(delta.HighWaterGameMinutes / 1440) + 1,
                // Counted by direct enumeration rather than through SaveService, which resolves its
                // own path through EnsureActiveWorld — going that way round would recurse.
                LivingCharacters = CountCharacters(worldId),
                FallenCharacters = delta.FallenHeroes.Count
            });
        }

        return results.OrderByDescending(w => w.CreatedAtUtc).ToList();
    }

    private static int CountCharacters(string worldId)
    {
        var directory = CharactersDirectory(worldId);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json").Count() : 0;
    }

    /// <summary>Permanently remove a world and every character inside it (the picker's Delete).</summary>
    public static void Delete(string worldId)
    {
        try
        {
            var directory = WorldDirectory(worldId);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            if (_activeWorldId == worldId) ActiveWorldId = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting world '{worldId}': {ex.Message}");
        }
    }

    public static WorldDelta LoadDelta(string worldId)
    {
        try
        {
            var path = DeltaFile(worldId);
            if (File.Exists(path))
            {
                var delta = JsonSerializer.Deserialize<WorldDelta>(File.ReadAllText(path), ReadOptions);
                if (delta != null) return delta;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Could not read world delta for '{worldId}', starting fresh: {ex.Message}");
        }
        return new WorldDelta();
    }

    public static void SaveDelta(string worldId, WorldDelta delta)
    {
        try
        {
            Directory.CreateDirectory(WorldDirectory(worldId));
            File.WriteAllText(DeltaFile(worldId), JsonSerializer.Serialize(delta, WriteOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing world delta for '{worldId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Record a fallen character in the world's memory. Called before the character file is deleted,
    /// so permadeath removes the hero without erasing that they existed.
    /// </summary>
    public static void RecordFallenHero(string worldId, LegacyHero hero)
    {
        if (string.IsNullOrEmpty(worldId)) return;
        var delta = LoadDelta(worldId);
        delta.FallenHeroes.Add(hero);
        SaveDelta(worldId, delta);
    }

    /// <summary>Advance the world's displayed elapsed time if this character has gone further than
    /// any before them. Called on save, so the worlds picker shows a meaningful day count.</summary>
    public static void RecordWorldTime(string worldId, double totalGameMinutes)
    {
        if (string.IsNullOrEmpty(worldId) || totalGameMinutes <= 0) return;
        var delta = LoadDelta(worldId);
        if (totalGameMinutes <= delta.HighWaterGameMinutes) return;
        delta.HighWaterGameMinutes = totalGameMinutes;
        SaveDelta(worldId, delta);
    }

    // --- Profiles (size + hostility knobs) ---

    /// <summary>
    /// The active world's resolved tunables. Cached like GameSettings.Current and invalidated when
    /// the active world changes, so gameplay code reads a plain property and never learns that a
    /// world option picked the value.
    /// </summary>
    public static WorldProfile Profile => _activeProfile ??= ResolveProfile(
        string.IsNullOrEmpty(_activeWorldId) ? null : Load(_activeWorldId!)?.Options);

    private static WorldGenConfig? _config;

    private static WorldGenConfig Config => _config ??= LoadConfig();

    private static WorldGenConfig LoadConfig()
    {
        try
        {
            var path = GamePaths.Content("Data", "Config", "worldgen.json");
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<WorldGenConfig>(File.ReadAllText(path), ReadOptions);
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to load worldgen.json, using defaults: {ex.Message}");
        }
        return new WorldGenConfig();
    }

    /// <summary>Resolve a profile for explicit options — used by the profile cache and directly by
    /// tests that need to compare two hostility levels without swapping the active world.</summary>
    public static WorldProfile ResolveProfile(WorldGenOptions? options)
    {
        options ??= new WorldGenOptions();
        return new WorldProfile
        {
            Size = Config.Sizes.TryGetValue(options.Size.ToString(), out var size) ? size : new WorldSizeProfile(),
            Hostility = Config.Hostility.TryGetValue(options.Hostility.ToString(), out var hostility)
                ? hostility : new HostilityProfile()
        };
    }

    /// <summary>Test/diagnostic hook: drop cached state so a fresh scope can be established without
    /// restarting the process (the TEST_* demos all share one process).</summary>
    public static void ResetCachesForTesting()
    {
        _activeWorldId = null;
        _activeProfile = null;
        _config = null;
    }
}

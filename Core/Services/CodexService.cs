using System;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Discovery/bestiary log + cross-run play stats, persisted as JSON under Saves/codex.json.
/// Bestiary keyed by "{Race} {Class}" — enemies are generated as full characters (see
/// EnemyFactory), so this gives a richly varied bestiary "for free."
///
/// RecordEncounter only updates in-memory data (it can fire every combat engagement, which is
/// too frequent to justify a disk write each time); RecordKill/RecordDeath/RecordFloorCleared
/// are much rarer events and flush to disk, which also persists any pending encounter data.
/// </summary>
public class CodexService
{
    private static string SavePath => GamePaths.Save("Saves", "codex.json");
    private static CodexService? _instance;
    public static CodexService Instance => _instance ??= Load();

    public CodexData Data { get; private set; } = new();

    private static CodexService Load()
    {
        var service = new CodexService();
        try
        {
            if (File.Exists(SavePath))
            {
                var json = File.ReadAllText(SavePath);
                var loaded = JsonSerializer.Deserialize<CodexData>(json);
                if (loaded != null) service.Data = loaded;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to load codex, starting fresh: {ex.Message}");
        }
        return service;
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to save codex: {ex.Message}");
        }
    }

    private static string KeyFor(Enemy enemy) => $"{enemy.Race} {enemy.Class}";

    private BestiaryEntry GetOrCreateEntry(Enemy enemy, int floor)
    {
        var key = KeyFor(enemy);
        if (!Data.Bestiary.TryGetValue(key, out var entry))
        {
            entry = new BestiaryEntry { Name = key, FirstFloor = floor, LastFloor = floor };
            Data.Bestiary[key] = entry;
        }
        if (floor < entry.FirstFloor) entry.FirstFloor = floor;
        if (floor > entry.LastFloor) entry.LastFloor = floor;
        return entry;
    }

    public void RecordEncounter(Enemy enemy, int floor)
    {
        GetOrCreateEntry(enemy, floor).Seen++;
    }

    public void RecordKill(Enemy enemy, int floor)
    {
        GetOrCreateEntry(enemy, floor).Killed++;
        Data.PlayStats.TotalKills++;
        Save();
    }

    public void RecordDeath(int floor)
    {
        Data.PlayStats.TotalDeaths++;
        if (floor > Data.PlayStats.DeepestFloor) Data.PlayStats.DeepestFloor = floor;
        Save();
    }

    public void RecordFloorCleared(int floor)
    {
        Data.PlayStats.TotalFloorsCleared++;
        if (floor > Data.PlayStats.DeepestFloor) Data.PlayStats.DeepestFloor = floor;
        Save();
    }

    /// <summary>Voluntary exit via a safe-room shrine — distinct from RecordDeath (progress is
    /// preserved, not reset).</summary>
    public void RecordDungeonExit(int floor)
    {
        Data.PlayStats.TotalDungeonExits++;
        if (floor > Data.PlayStats.DeepestFloor) Data.PlayStats.DeepestFloor = floor;
        Save();
    }

    /// <summary>Wipe all codex data. Used by tests that spin up multiple GameStates in one
    /// process; could also back a future "reset save data" UI action.</summary>
    public void Reset()
    {
        Data = new CodexData();
        Save();
    }
}

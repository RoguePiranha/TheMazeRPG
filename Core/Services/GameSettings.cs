using System;
using System.IO;
using System.Text.Json;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Game configuration loaded from Data/Config/settings.json.
/// TickRate is the authoritative simulation rate; all time-based durations
/// are derived from it via SecondsToTicks so they can't drift out of sync again.
/// </summary>
public class GameSettings
{
    public int WindowWidth { get; set; } = 300;
    public int WindowHeight { get; set; } = 420;
    public bool AlwaysOnTop { get; set; } = true;
    public int TickRate { get; set; } = 30;      // simulation ticks per second (authoritative)
    public int MazeWidth { get; set; } = 41;
    public int MazeHeight { get; set; } = 31;
    public int DefaultSeed { get; set; } = 12345;

    /// <summary>Per-slot hotbar bindings (owner request 2026-08-05): Avalonia Key names, one per
    /// slot, editable in settings.json (e.g. ["D1","D2","Q","R"]). Number-row defaults; the
    /// numpad always works as a fallback. Rebinding UI comes with the Settings screen later.</summary>
    public string[] HotbarKeys { get; set; } = { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9" };

    /// <summary>Display labels for the bindings ("D1" → "1", "NumPad3" → "N3", "Q" → "Q").</summary>
    public string[] HotbarKeyLabels =>
        System.Array.ConvertAll(HotbarKeys, k =>
            k.StartsWith("D", StringComparison.Ordinal) && k.Length == 2 && char.IsDigit(k[1]) ? k[1..]
            : k.StartsWith("NumPad", StringComparison.Ordinal) ? "N" + k[6..]
            : k);

    private static GameSettings? _current;

    /// <summary>Lazily-loaded, cached settings instance.</summary>
    public static GameSettings Current => _current ??= Load();

    public static GameSettings Load()
    {
        try
        {
            var path = Path.Combine("Data", "Config", "settings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<GameSettings>(json, options);
                if (loaded != null)
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to load settings.json, using defaults: {ex.Message}");
        }
        return new GameSettings();
    }

    /// <summary>Convert a real-time duration in seconds to a whole number of ticks (minimum 1).</summary>
    public int SecondsToTicks(float seconds) => Math.Max(1, (int)MathF.Round(seconds * TickRate));
}

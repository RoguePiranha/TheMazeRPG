using System;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.GodotClient;

/// <summary>Godot presentation preferences, persisted separately from simulation settings.</summary>
public sealed class ClientSettings
{
    public bool Fullscreen { get; set; }
    public bool CameraSmoothing { get; set; } = true;
    public bool ScreenShake { get; set; } = true;
    public bool VSync { get; set; } = true;
    public float MasterVolume { get; set; } = 0.8f;

    private static string SavePath => GamePaths.Save("client-settings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return new ClientSettings();
            ClientSettings? settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SavePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings == null) return new ClientSettings();
            settings.MasterVolume = Math.Clamp(settings.MasterVolume, 0f, 1f);
            return settings;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(SavePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // A settings write failure must not interrupt gameplay.
        }
    }
}

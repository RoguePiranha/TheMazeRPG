using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Loads curated terrain atlases and reconstructs authored floor patterns from explicit 16px
/// source regions. Unlike actor sprites, terrain files are atlases rather than animation strips,
/// so they need source-pattern mappings instead of SpriteService's first-frame convention.
/// </summary>
public static class TerrainService
{
    private const string ManifestPath = "Data/Sprites/terrain.json";
    private const string AssetRoot = "avares://TheMazeRPG/Assets/Sprites/";
    private static readonly Dictionary<string, TerrainDefinition> Definitions = LoadManifest();
    private static readonly Dictionary<string, SKBitmap?> Atlases = new();

    private sealed class TerrainManifest
    {
        public Dictionary<string, TerrainDefinition> Themes { get; set; } = new();
    }

    private sealed class TerrainDefinition
    {
        public string Atlas { get; set; } = "";
        public int SourceX { get; set; }
        public int SourceY { get; set; }
        public int TileSize { get; set; } = 16;
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
    }

    public static bool DrawFloor(
        SKCanvas canvas,
        DungeonTheme theme,
        int tileX,
        int tileY,
        float x,
        float y,
        float size,
        byte alpha)
    {
        if (!Definitions.TryGetValue(theme.ToString(), out var definition)) return false;
        var atlas = LoadAtlas(definition.Atlas);
        if (atlas == null || definition.TileSize <= 0 || definition.Columns <= 0 || definition.Rows <= 0 ||
            definition.SourceX < 0 || definition.SourceY < 0 ||
            definition.SourceX + definition.TileSize * definition.Columns > atlas.Width ||
            definition.SourceY + definition.TileSize * definition.Rows > atlas.Height)
        {
            return false;
        }

        int sourceX = definition.SourceX + PositiveModulo(tileX, definition.Columns) * definition.TileSize;
        int sourceY = definition.SourceY + PositiveModulo(tileY, definition.Rows) * definition.TileSize;
        var source = new SKRect(
            sourceX,
            sourceY,
            sourceX + definition.TileSize,
            sourceY + definition.TileSize);
        var destination = new SKRect(x, y, x + size, y + size);
        // Orthographic contract: terrain samples are scaled only, never rotated or mirrored.
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };
        canvas.DrawBitmap(atlas, source, destination, paint);
        return true;
    }

    private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

    private static Dictionary<string, TerrainDefinition> LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath))
                return new Dictionary<string, TerrainDefinition>();

            var json = File.ReadAllText(ManifestPath);
            var manifest = JsonSerializer.Deserialize<TerrainManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return manifest?.Themes ?? new Dictionary<string, TerrainDefinition>();
        }
        catch (Exception ex)
        {
            GameLog.Debug($"TerrainService: failed to read {ManifestPath} ({ex.Message}).");
            return new Dictionary<string, TerrainDefinition>();
        }
    }

    private static SKBitmap? LoadAtlas(string relativePath)
    {
        if (Atlases.TryGetValue(relativePath, out var cached)) return cached;

        SKBitmap? atlas = null;
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(AssetRoot + relativePath));
            atlas = SKBitmap.Decode(stream);
        }
        catch (Exception ex)
        {
            GameLog.Debug($"TerrainService: failed to load '{relativePath}' ({ex.Message}).");
        }

        Atlases[relativePath] = atlas;
        return atlas;
    }
}

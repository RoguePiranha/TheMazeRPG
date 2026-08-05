using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Draws semantic dungeon tiles from the grouped catalog in Data/Sprites/terrain.json.
/// Catalog entries carry placement and facing metadata; source sprites are never rotated or
/// mirrored. Multi-cell patterns preserve the atlas's authored adjacency through world position.
/// </summary>
public static class TerrainService
{
    private const string ManifestPath = "Data/Sprites/terrain.json";
    private const string AssetRoot = "avares://TheMazeRPG/Assets/Sprites/";
    private static readonly Dictionary<string, TileSetDefinition> Sets = LoadManifest();
    private static readonly Dictionary<string, SKBitmap?> Atlases = new();

    private sealed class TerrainManifest
    {
        public Dictionary<string, TileSetDefinition> Sets { get; set; } = new();
    }

    private sealed class TileSetDefinition
    {
        public string SourcePack { get; set; } = "";
        public string Atlas { get; set; } = "";
        public int GridSize { get; set; } = 16;
        public Dictionary<string, TileSpriteDefinition> Sprites { get; set; } = new();
    }

    private sealed class TileSpriteDefinition
    {
        public string Placement { get; set; } = "";
        public string Facing { get; set; } = "none";
        public string Layer { get; set; } = "ground";
        public bool Walkable { get; set; }
        public int SourceX { get; set; }
        public int SourceY { get; set; }
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
    }

    public static bool DrawTile(
        SKCanvas canvas,
        DungeonTheme theme,
        string spriteId,
        int tileX,
        int tileY,
        float x,
        float y,
        float size,
        byte alpha)
    {
        if (!Sets.TryGetValue(theme.ToString(), out var set) ||
            !set.Sprites.TryGetValue(spriteId, out var definition))
        {
            return false;
        }

        var atlas = LoadAtlas(set.Atlas);
        if (atlas == null || !IsValid(set, definition, atlas.Width, atlas.Height)) return false;

        int sourceX = definition.SourceX + PositiveModulo(tileX, definition.Columns) * set.GridSize;
        int sourceY = definition.SourceY + PositiveModulo(tileY, definition.Rows) * set.GridSize;
        var source = new SKRect(
            sourceX,
            sourceY,
            sourceX + set.GridSize,
            sourceY + set.GridSize);
        var destination = new SKRect(x, y, x + size, y + size);
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };
        canvas.DrawBitmap(atlas, source, destination, paint);
        return true;
    }

    private static bool IsValid(TileSetDefinition set, TileSpriteDefinition sprite, int width, int height) =>
        set.GridSize > 0 && sprite.Columns > 0 && sprite.Rows > 0 &&
        sprite.SourceX >= 0 && sprite.SourceY >= 0 &&
        sprite.SourceX + set.GridSize * sprite.Columns <= width &&
        sprite.SourceY + set.GridSize * sprite.Rows <= height;

    private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

    private static Dictionary<string, TileSetDefinition> LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return new Dictionary<string, TileSetDefinition>();

            var json = File.ReadAllText(ManifestPath);
            var manifest = JsonSerializer.Deserialize<TerrainManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return manifest?.Sets ?? new Dictionary<string, TileSetDefinition>();
        }
        catch (Exception ex)
        {
            GameLog.Debug($"TerrainService: failed to read {ManifestPath} ({ex.Message}).");
            return new Dictionary<string, TileSetDefinition>();
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

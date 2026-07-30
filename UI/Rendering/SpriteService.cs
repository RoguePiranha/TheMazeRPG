using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Loads entity sprites from the Data/Sprites/sprites.json manifest (same static-singleton content
/// pattern as MaterialDataService/RecipeDataService) and hands the renderer ready-to-draw bitmaps.
///
/// Sheets are horizontal animation strips, so frame size is derived rather than configured:
/// frame size = sheet height, frame count = width / height. This first pass extracts frame 0 only
/// (a static pose); the remaining frames are already available for a later animation pass.
///
/// Every failure path returns null so the renderer falls back to its original procedural shapes —
/// a missing/misnamed sprite degrades to the old look instead of crashing or drawing nothing.
///
/// Lives in UI/Rendering (not Core) because it deals in SkiaSharp bitmaps: Core stays UI-agnostic
/// so the headless TEST_* demos keep working without a rendering stack.
/// </summary>
public static class SpriteService
{
    private const string ManifestPath = "Data/Sprites/sprites.json";
    private const string AssetRoot = "avares://TheMazeRPG/Assets/Sprites/";

    // Manifest key -> asset path, from sprites.json.
    private static readonly Dictionary<string, string> _paths = LoadManifest();

    // Asset path -> decoded frame-0 bitmap. Null is cached too, so a broken path is only
    // attempted (and logged) once rather than every frame.
    private static readonly Dictionary<string, SKBitmap?> _cache = new();

    private sealed class Manifest
    {
        public Dictionary<string, string> Sprites { get; set; } = new();
    }

    private static Dictionary<string, string> LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath))
            {
                GameLog.Debug($"SpriteService: no manifest at {ManifestPath} — using procedural shapes.");
                return new Dictionary<string, string>();
            }
            var json = File.ReadAllText(ManifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var map = manifest?.Sprites ?? new Dictionary<string, string>();
            GameLog.Debug($"SpriteService: loaded {map.Count} sprite mappings.");
            return map;
        }
        catch (Exception ex)
        {
            GameLog.Debug($"SpriteService: failed to read {ManifestPath} ({ex.Message}) — using procedural shapes.");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>The hero's sprite, or null to draw the procedural hero.</summary>
    public static SKBitmap? ForHero(string heroClass) => Lookup($"hero:{heroClass}");

    /// <summary>An enemy's sprite, resolved most-specific-first: race+class, then race, then class.
    /// Null means no mapping — draw the procedural class shape.</summary>
    public static SKBitmap? ForEnemy(string race, string enemyClass) =>
        Lookup($"enemy:{race}:{enemyClass}") ?? Lookup($"enemy:{race}") ?? Lookup($"enemy:{enemyClass}");

    private static SKBitmap? Lookup(string key) =>
        _paths.TryGetValue(key, out var path) ? Load(path) : null;

    /// <summary>Decode a sheet's first frame, cached by asset path.</summary>
    private static SKBitmap? Load(string relativePath)
    {
        if (_cache.TryGetValue(relativePath, out var cached)) return cached;

        SKBitmap? frame = null;
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(AssetRoot + relativePath));
            using var sheet = SKBitmap.Decode(stream);
            if (sheet == null || sheet.Height <= 0)
            {
                GameLog.Debug($"SpriteService: could not decode '{relativePath}'.");
            }
            else
            {
                // Square frames laid out left-to-right: the first frame is the leading NxN block.
                int size = sheet.Height;
                frame = new SKBitmap(size, size, sheet.ColorType, sheet.AlphaType);
                using var canvas = new SKCanvas(frame);
                canvas.Clear(SKColors.Transparent);
                // Copy (rather than subset-alias) so the full sheet can be released here.
                canvas.DrawBitmap(sheet,
                    new SKRect(0, 0, size, size),
                    new SKRect(0, 0, size, size));
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"SpriteService: failed to load '{relativePath}' ({ex.Message}).");
            frame = null;
        }

        _cache[relativePath] = frame;
        return frame;
    }

    /// <summary>The draw size to use for a sprite so pixel art lands on whole-pixel scaling: the
    /// largest integer multiple of the sprite's frame size that still fits <paramref name="maxSize"/>.
    /// (A 32px frame in a 64px cell draws at 2x; a 64px frame draws at 1x — never at 1.8x, which is
    /// what makes scaled pixel art look mushy and uneven.)</summary>
    public static float CrispSize(SKBitmap sprite, float maxSize)
    {
        int scale = Math.Max(1, (int)(maxSize / sprite.Height));
        return sprite.Height * scale;
    }

    /// <summary>Draw a sprite centered on a world point, scaled to <paramref name="targetSize"/>
    /// pixels with nearest-neighbour filtering so pixel art stays crisp rather than blurring.</summary>
    public static void Draw(SKCanvas canvas, SKBitmap sprite, float centerX, float centerY,
        float targetSize, SKPaint? paint = null)
    {
        float half = targetSize / 2f;
        var dest = new SKRect(centerX - half, centerY - half, centerX + half, centerY + half);

        if (paint != null)
        {
            paint.FilterQuality = SKFilterQuality.None;
            canvas.DrawBitmap(sprite, dest, paint);
            return;
        }

        using var crisp = new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false };
        canvas.DrawBitmap(sprite, dest, crisp);
    }
}

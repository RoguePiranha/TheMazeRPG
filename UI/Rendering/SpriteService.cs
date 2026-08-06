using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Loads entity sprites from Data/Sprites/sprites.json and returns normalized static frames.
/// Sheets are horizontal animation strips with square frames; this pass extracts frame zero.
/// Missing or invalid sprites return null so the renderer can use its procedural fallback.
/// </summary>
public static class SpriteService
{
    private const string AssetRoot = "avares://TheMazeRPG/Assets/Sprites/";

    /// <summary>The manifest path: the configured content root first (historical CWD behavior),
    /// then the executable's own directory. Launching from a shortcut or another working
    /// directory used to silently drop every sprite mapping — art "vanished" with only a
    /// debug-log line to say why.</summary>
    private static string ManifestPath
    {
        get
        {
            string configured = GamePaths.Content("Data", "Sprites", "sprites.json");
            return File.Exists(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "Data", "Sprites", "sprites.json");
        }
    }

    private static readonly Dictionary<string, string> Paths = LoadManifest();
    private static readonly Dictionary<string, SKBitmap?> Cache = new();

    private sealed class Manifest
    {
        public Dictionary<string, ActorSetDefinition> Sets { get; set; } = new();
    }

    private sealed class ActorSetDefinition
    {
        public string SourcePack { get; set; } = "";
        public string Placement { get; set; } = "actor";
        public string Facing { get; set; } = "screen-south";
        public string Anchor { get; set; } = "bottom-center";
        public string Animation { get; set; } = "idle";
        public int Frame { get; set; }
        public Dictionary<string, ActorSpriteDefinition> Sprites { get; set; } = new();
    }

    private sealed class ActorSpriteDefinition
    {
        public string Asset { get; set; } = "";
    }

    private static Dictionary<string, string> LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath))
            {
                GameLog.Debug($"SpriteService: no manifest at {ManifestPath}; using procedural shapes.");
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(ManifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (setName, set) in manifest?.Sets ?? new Dictionary<string, ActorSetDefinition>())
            {
                foreach (var (key, sprite) in set.Sprites)
                {
                    if (!map.TryAdd(key, sprite.Asset))
                        GameLog.Debug($"SpriteService: duplicate key '{key}' in set '{setName}'.");
                }
            }
            GameLog.Debug($"SpriteService: loaded {map.Count} sprite mappings from {manifest?.Sets.Count ?? 0} sets.");
            return map;
        }
        catch (Exception ex)
        {
            GameLog.Debug($"SpriteService: failed to read {ManifestPath} ({ex.Message}); using procedural shapes.");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>The hero's sprite, or null to draw the procedural hero.</summary>
    public static SKBitmap? ForHero(string heroClass) => Lookup($"hero:{heroClass}");

    /// <summary>
    /// Resolve an enemy by race and class, then race, then class. Null uses the procedural shape.
    /// </summary>
    public static SKBitmap? ForEnemy(string race, string enemyClass) =>
        Lookup($"enemy:{race}:{enemyClass}") ?? Lookup($"enemy:{race}") ?? Lookup($"enemy:{enemyClass}");

    private static SKBitmap? Lookup(string key) =>
        Paths.TryGetValue(key, out var path) ? Load(path) : null;

    /// <summary>
    /// Decode frame zero and normalize its visible pixels onto a padded, feet-anchored square.
    /// Source packs use both 32px and 64px frames, but their characters occupy roughly 30px in
    /// either case. Cropping transparent padding makes a common draw box produce a common scale.
    /// </summary>
    private static SKBitmap? Load(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached)) return cached;

        SKBitmap? frame = null;
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(AssetRoot + relativePath));
            using var sheet = SKBitmap.Decode(stream);
            if (sheet == null || sheet.Height <= 0)
            {
                GameLog.Debug($"SpriteService: could not decode '{relativePath}'.");
            }
            else if (TryOpaqueBounds(sheet, sheet.Height, out var bounds))
            {
                const int padding = 4;
                int contentWidth = bounds.Width;
                int contentHeight = bounds.Height;
                int normalizedSize = Math.Max(contentWidth, contentHeight) + padding;
                int destinationX = (normalizedSize - contentWidth) / 2;
                int destinationY = normalizedSize - contentHeight;

                frame = new SKBitmap(normalizedSize, normalizedSize, sheet.ColorType, sheet.AlphaType);
                using var canvas = new SKCanvas(frame);
                canvas.Clear(SKColors.Transparent);
                using var copyPaint = new SKPaint
                {
                    FilterQuality = SKFilterQuality.None,
                    IsAntialias = false
                };
                canvas.DrawBitmap(sheet,
                    new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                    new SKRect(destinationX, destinationY,
                        destinationX + contentWidth, destinationY + contentHeight),
                    copyPaint);
            }
            else
            {
                GameLog.Debug($"SpriteService: first frame of '{relativePath}' is empty.");
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"SpriteService: failed to load '{relativePath}' ({ex.Message}).");
            frame = null;
        }

        Cache[relativePath] = frame;
        return frame;
    }

    private static bool TryOpaqueBounds(SKBitmap sheet, int frameSize, out SKRectI bounds)
    {
        int left = frameSize;
        int top = frameSize;
        int right = -1;
        int bottom = -1;

        for (int y = 0; y < frameSize; y++)
        {
            for (int x = 0; x < frameSize; x++)
            {
                if (sheet.GetPixel(x, y).Alpha <= 8) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            bounds = SKRectI.Empty;
            return false;
        }

        bounds = new SKRectI(left, top, right + 1, bottom + 1);
        return true;
    }

    /// <summary>
    /// Draw a normalized sprite into a common square with nearest-neighbor filtering. Sprites keep
    /// their authored orientation and the API intentionally exposes no rotation or mirroring.
    /// </summary>
    public static void Draw(SKCanvas canvas, SKBitmap sprite, float centerX, float centerY,
        float targetSize, SKPaint? paint = null)
    {
        float half = targetSize / 2f;
        var destination = new SKRect(
            centerX - half, centerY - half,
            centerX + half, centerY + half);

        if (paint != null)
        {
            paint.FilterQuality = SKFilterQuality.None;
            canvas.DrawBitmap(sprite, destination, paint);
            return;
        }

        using var crisp = new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false };
        canvas.DrawBitmap(sprite, destination, crisp);
    }
}

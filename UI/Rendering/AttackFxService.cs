using System;
using System.Collections.Generic;
using SkiaSharp;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Avalonia/Skia half of the attack-effect pipeline: renders the AttackFxCatalog's mappings
/// (weapon swings rotated to the aim and anchored on the attacker, projectile sprites rotated to
/// their heading, impact bursts at the point of contact). The catalog owns the data; this owns
/// the avares bitmap cache and the Skia draw math. Art is authored at 16 px per world tile, so
/// every draw scales by CellSize/16.
/// </summary>
public static class AttackFxService
{
    private const string AssetRoot = "avares://TheMazeRPG/Assets/Sprites/";
    private const float ArtPixelsPerTile = 16f;

    private static readonly Dictionary<string, SKBitmap?> Bitmaps = new();

    private static SKBitmap? Bitmap(string relativePath)
    {
        if (Bitmaps.TryGetValue(relativePath, out var cached)) return cached;
        SKBitmap? bitmap = null;
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(AssetRoot + relativePath));
            bitmap = SKBitmap.Decode(stream);
            if (bitmap == null) GameLog.Debug($"AttackFxService: could not decode '{relativePath}'.");
        }
        catch (Exception ex)
        {
            GameLog.Debug($"AttackFxService: failed to load '{relativePath}' ({ex.Message}).");
        }
        Bitmaps[relativePath] = bitmap;
        return bitmap;
    }

    public static bool TryGet(VisualStyle style, out AttackFxDef fx) => AttackFxCatalog.TryGet(style, out fx);

    /// <summary>
    /// Draw a weapon-swing frame anchored on the attacker. The sheet's chosen row is authored
    /// facing screen-south; the whole frame is rotated so that authored facing lands on the aim,
    /// which covers every attack angle with a single row.
    /// </summary>
    public static bool DrawSwing(SKCanvas canvas, AttackFxDef fx, float px, float py,
        float aimAngleRad, float t, float cellSize)
    {
        var sheet = Bitmap(fx.Asset);
        if (sheet == null) return false;

        int frame = Math.Clamp((int)(t * fx.Columns), 0, fx.Columns - 1);
        var src = SKRect.Create(frame * fx.FrameSize, fx.Row * fx.FrameSize, fx.FrameSize, fx.FrameSize);
        float half = fx.FrameSize * (cellSize / ArtPixelsPerTile) / 2f;

        canvas.Save();
        canvas.Translate(px, py);
        canvas.RotateDegrees(aimAngleRad * 180f / MathF.PI - fx.BaseAngleDeg);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false };
        canvas.DrawBitmap(sheet, src, new SKRect(-half, -half, half, half), paint);
        canvas.Restore();
        return true;
    }

    /// <summary>Draw a projectile sprite at its current position, rotated to its heading.</summary>
    public static bool DrawProjectile(SKCanvas canvas, AttackFxDef fx, float px, float py,
        float headingRad, float cellSize)
    {
        var sprite = Bitmap(fx.Asset);
        if (sprite == null) return false;

        var src = SKRect.Create(0, 0, fx.FrameSize, fx.FrameSize);
        float half = fx.FrameSize * (cellSize / ArtPixelsPerTile) / 2f;

        canvas.Save();
        canvas.Translate(px, py);
        canvas.RotateDegrees(headingRad * 180f / MathF.PI - fx.BaseAngleDeg);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false };
        canvas.DrawBitmap(sprite, src, new SKRect(-half, -half, half, half), paint);
        canvas.Restore();
        return true;
    }

    /// <summary>Draw the style's impact burst at the point of contact, if it has one. Impact
    /// strips are authored at 48 px ≈ 1.5 tiles — drawn at half that footprint for punch
    /// without splash.</summary>
    public static bool DrawImpact(SKCanvas canvas, VisualStyle style, float px, float py,
        float t, float cellSize)
    {
        if (!AttackFxCatalog.TryGet(style, out var fx) || fx.ImpactAsset == null) return false;
        var sheet = Bitmap(fx.ImpactAsset);
        if (sheet == null) return false;

        int frame = Math.Clamp((int)(t * fx.ImpactColumns), 0, fx.ImpactColumns - 1);
        var src = SKRect.Create(frame * fx.ImpactFrameSize, 0, fx.ImpactFrameSize, fx.ImpactFrameSize);
        float half = fx.ImpactFrameSize * (cellSize / ArtPixelsPerTile) / 4f;

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None, IsAntialias = false };
        canvas.DrawBitmap(sheet, src, new SKRect(px - half, py - half, px + half, py + half), paint);
        return true;
    }
}

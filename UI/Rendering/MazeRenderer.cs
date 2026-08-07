using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Rendering;

/// <summary>
/// Renders the maze and entities using Skia canvas
/// </summary>
public class MazeRenderer
{
    private const int CellSize = 64; // Size of each maze cell in pixels (much larger for free movement)
    private const float CameraLerpSpeed = 0.15f; // Smooth camera movement

    private float _cameraX = 0;
    private float _cameraY = 0;
    private readonly Random _shakeRandom = new();

    // Shake-free world→screen pixel offset from the last frame (screenPx = worldTile*CellSize +
    // offset). Used by ScreenToWorld for click-to-fire aiming.
    private float _lastOffsetX;
    private float _lastOffsetY;

    /// <summary>Map a screen-space point (pixels, relative to the canvas) to world tile
    /// coordinates, using the last rendered camera position. Entities at world (X, Y) draw at
    /// pixel X*CellSize + CellSize/2 (cell centers), so the inverse subtracts the half-cell —
    /// without it every click aims half a tile down-right of what was clicked.</summary>
    public (float x, float y) ScreenToWorld(double screenX, double screenY) =>
        (((float)screenX - _lastOffsetX) / CellSize - 0.5f,
         ((float)screenY - _lastOffsetY) / CellSize - 0.5f);

    // Color palette — SimpleRPG's dark terminal-roguelike look: pure black void, near-black
    // floors, gray walls; entities/features pop in color against the dark.
    private static readonly SKColor BackgroundColor = new(0, 0, 0);
    private static readonly SKColor WallColor = new(0x66, 0x66, 0x66);
    private static readonly SKColor WallDetailColor = new(0x88, 0x88, 0x88);
    private static readonly SKColor FloorColor = new(0x22, 0x22, 0x22);
    private static readonly SKColor FloorDotColor = new(0x33, 0x33, 0x33);
    private static readonly SKColor CorridorFloorColor = new(0x1B, 0x1D, 0x20);
    private static readonly SKColor StandardRoomFloorColor = new(0x25, 0x25, 0x25);
    private static readonly SKColor EntranceRoomFloorColor = new(0x20, 0x28, 0x2D);
    private static readonly SKColor TreasureRoomFloorColor = new(0x2E, 0x29, 0x1B);
    private static readonly SKColor HazardRoomFloorColor = new(0x30, 0x20, 0x20);
    private static readonly SKColor ExitRoomFloorColor = new(0x20, 0x2C, 0x23);
    private static readonly SKColor HeroColor = new(100, 180, 255);
    private static readonly SKColor EnemyColor = new(255, 80, 80);
    private static readonly SKColor ChestColor = new(255, 215, 0);
    private static readonly SKColor StairsColor = new(150, 255, 150);

    private readonly record struct DungeonPalette(
        SKColor Wall,
        SKColor WallDetail,
        SKColor FloorDetail,
        SKColor Corridor,
        SKColor StandardRoom,
        SKColor EntranceRoom,
        SKColor TreasureRoom,
        SKColor HazardRoom,
        SKColor ExitRoom);

    // Explored-but-out-of-sight tiles render at this alpha over the black void (SimpleRPG's 0.3).
    private const byte DimAlpha = 76;

    // Fog-of-war memory: every floor tile the hero has ever actually SEEN (sight radius + LOS),
    // per maze. Renderer-owned so gameplay's Maze.Explored (the walked trail driving
    // auto-explore) keeps its meaning untouched.
    private Maze? _fogMaze;
    private readonly HashSet<(int x, int y)> _seenFloors = new();

    /// <summary>Per-frame visibility for the fog pass. Disabled outside regular dungeon floors
    /// (Overworld/safe rooms are fully lit spaces).</summary>
    private readonly struct FogView
    {
        public readonly bool Enabled;
        public readonly HashSet<(int x, int y)> VisibleFloors;
        public readonly HashSet<(int x, int y)> SeenFloors;

        public FogView(bool enabled, HashSet<(int x, int y)> visible, HashSet<(int x, int y)> seen)
        {
            Enabled = enabled;
            VisibleFloors = visible;
            SeenFloors = seen;
        }

        public bool FloorVisible(int x, int y) => !Enabled || VisibleFloors.Contains((x, y));
        public bool FloorSeen(int x, int y) => !Enabled || VisibleFloors.Contains((x, y)) || SeenFloors.Contains((x, y));
    }

    // The game's pixel font for all Skia-drawn text (same file the XAML side uses). Falls back
    // to a monospace system font if the asset can't be loaded.
    private static readonly SKTypeface GameTypeface = LoadGameTypeface();
    private static SKTypeface LoadGameTypeface()
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://TheMazeRPG/Assets/Fonts/Odderf Basic.otf"));
            return SKTypeface.FromStream(stream) ?? SKTypeface.FromFamilyName("Consolas");
        }
        catch
        {
            return SKTypeface.FromFamilyName("Consolas");
        }
    }

    // Enemy color by character class.
    private static SKColor ClassColor(string cls) => cls switch
    {
        "Warrior" => new SKColor(205, 65, 60),          // red
        "Rogue" => new SKColor(70, 150, 110),           // teal-green
        "Archer" => new SKColor(110, 200, 80),          // lime
        "Mage Apprentice" => new SKColor(95, 135, 240), // blue
        "Priest" => new SKColor(232, 200, 95),          // gold
        "Bard" => new SKColor(175, 125, 232),           // purple
        "Wanderer" => new SKColor(175, 175, 175),       // gray
        _ => new SKColor(200, 80, 80)
    };

    // 0=square (tank/generalist), 1=diamond (fast melee), 2=triangle (ranged), 3=pentagon (caster/support)
    private static int ClassShape(string cls) => cls switch
    {
        "Warrior" => 0,
        "Wanderer" => 0,
        "Rogue" => 1,
        "Archer" => 2,
        "Mage Apprentice" => 3,
        "Priest" => 3,
        "Bard" => 3,
        _ => 2
    };
    
    /// <summary>
    /// Draws a frame. Runs on the render thread: every mutable collection must come from
    /// <paramref name="snapshot"/> (captured on the UI thread), never live from
    /// <paramref name="gameState"/> — see RenderSnapshot. GameState is still passed for scalar
    /// reads (hero stats, flags, clock), where a torn value is a one-frame blip, not a crash.
    /// </summary>
    public void Render(SKCanvas canvas, GameState gameState, RenderSnapshot snapshot, int viewportWidth, int viewportHeight)
    {
        var maze = snapshot.Maze;

        canvas.Clear(BackgroundColor);

        // Smooth camera lerp to follow hero
        float targetCameraX = gameState.Hero.X * CellSize;
        float targetCameraY = gameState.Hero.Y * CellSize;

        _cameraX += (targetCameraX - _cameraX) * CameraLerpSpeed;
        _cameraY += (targetCameraY - _cameraY) * CameraLerpSpeed;

        // Clamp the CENTERING POINT (not the lerp state itself) to the maze bounds, so the
        // viewport never scrolls past the edge into empty void. Clamping the lerp state instead
        // would make the camera "stick" at the edge and jump when the hero moves back inward.
        float mazePxW = maze.Width * CellSize;
        float mazePxH = maze.Height * CellSize;
        float centerX = mazePxW > viewportWidth
            ? Math.Clamp(_cameraX, viewportWidth / 2f, mazePxW - viewportWidth / 2f)
            : mazePxW / 2f;
        float centerY = mazePxH > viewportHeight
            ? Math.Clamp(_cameraY, viewportHeight / 2f, mazePxH - viewportHeight / 2f)
            : mazePxH / 2f;

        // Center camera on viewport, with a small random jitter while ScreenShake is active
        float shakeX = 0f, shakeY = 0f;
        if (gameState.ScreenShake > 0f)
        {
            shakeX = ((float)_shakeRandom.NextDouble() - 0.5f) * 2f * gameState.ScreenShake;
            shakeY = ((float)_shakeRandom.NextDouble() - 0.5f) * 2f * gameState.ScreenShake;
        }
        float offsetX = viewportWidth / 2f - centerX + shakeX;
        float offsetY = viewportHeight / 2f - centerY + shakeY;

        // Remember the shake-free world→screen offset so input (click-to-fire aim) can map a
        // screen point back to world coordinates without aim jittering during screen shake.
        _lastOffsetX = viewportWidth / 2f - centerX;
        _lastOffsetY = viewportHeight / 2f - centerY;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        // Fog-of-war visibility for this frame (regular dungeon floors only)
        var fog = ComputeFogView(gameState, maze);

        // Draw maze
        DrawMaze(canvas, maze, fog);

        // Draw static room dressing below interactive features and actors.
        DrawDecorations(canvas, maze, fog);

        // Theme landmarks are world-space markers with a fixed orthographic orientation.
        DrawThemeFeatures(canvas, maze, fog);

        // Draw features (chests, stairs)
        DrawFeatures(canvas, maze, snapshot.Features, fog);

        // Ambient critters (dungeon rats/bats, the town dog and cat) — under real entities
        DrawCritters(canvas, snapshot.Critters, fog);

        // Draw enemies (only the ones the hero can currently see, under fog)
        DrawEnemies(canvas, gameState, snapshot.Enemies, fog);

    // Draw projectiles (between enemies and hero)
    DrawProjectiles(canvas, snapshot.Projectiles);

        // Draw on-hit flashes above projectiles, below hero
        DrawHitEffects(canvas, snapshot.HitEffects);
        
    // Draw hero (always on top)
        DrawHero(canvas, gameState.Hero);

        // Awareness alert: a "!" pops over the hero when they notice something (spotting a trap).
        if (gameState.HeroAlertActive)
        {
            float hx = (gameState.Hero.X + gameState.Hero.AnimationOffsetX) * CellSize + CellSize / 2f;
            float hy = (gameState.Hero.Y + gameState.Hero.AnimationOffsetY) * CellSize - CellSize * 0.35f;
            using var alertShadow = new SKPaint { Color = SKColors.Black, TextSize = 26, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
            using var alertPaint = new SKPaint { Color = new SKColor(0xFF, 0xCC, 0x00), TextSize = 26, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
            canvas.DrawText("!", hx + 1.5f, hy + 1.5f, alertShadow);
            canvas.DrawText("!", hx, hy, alertPaint);
        }

        // Rising damage/dodge/level-up feedback (world space, above everything in-world)
        DrawFloatingTexts(canvas, snapshot.FloatingTexts);

    // Debug overlays (hitboxes, LOS) if enabled
    DrawDebugOverlay(canvas, gameState, snapshot.Enemies, snapshot.Projectiles);

        canvas.Restore();

        // Town night lighting: dark layer with lamp/torch/darkvision light punched out
        DrawTownLighting(canvas, gameState, snapshot.Features, viewportWidth, viewportHeight);

        // Low-health vignette (screen space, drawn before the HUD text/bars)
        DrawLowHealthVignette(canvas, gameState.Hero, viewportWidth, viewportHeight);

        // Draw HUD overlay
        DrawHUD(canvas, gameState, viewportWidth, viewportHeight);

        // World-clock readout (town only, top-center)
        DrawClock(canvas, gameState, viewportWidth);

        // Draw the player-facing message log (bottom-left, above the floor info line)
        DrawMessageLog(canvas, snapshot.Messages, viewportHeight);

        // Draw the attack hotbar (bottom-center)
        DrawHotbar(canvas, gameState, snapshot, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// The attack hotbar (bottom-center): one slot per equipped/usable attack (Hero.Attacks),
    /// numbered 1..N, with the current attack highlighted in gold. Number keys select; the
    /// selected attack is what click-to-fire (and Auto combat) uses.
    /// </summary>
    private static void DrawHotbar(SKCanvas canvas, GameState gameState, RenderSnapshot snapshot, int viewportWidth, int viewportHeight)
    {
        // Fixed bar (owner request 2026-08-05): always HotbarCapacity uniform square slots —
        // empty boxes render as empty boxes — instead of the old variable-width text buttons.
        // Slot contents come from the snapshot: resolving them live walks Hero.Attacks/Loadout,
        // which the sim rebuilds mid-frame.
        var hero = gameState.Hero;
        int slots = snapshot.HotbarAttacks.Length;
        const float slotSize = 44f;
        const float gap = 6f;
        float totalW = slots * slotSize + (slots - 1) * gap;
        float startX = (viewportWidth - totalW) / 2f;
        float y = viewportHeight - slotSize - 8f;

        var keyLabels = GameSettings.Current.HotbarKeyLabels;

        for (int i = 0; i < slots; i++)
        {
            var attack = snapshot.HotbarAttacks[i];
            var consumable = snapshot.HotbarConsumables[i];
            bool selected = attack != null && hero.CurrentAttack == attack;
            float x = startX + i * (slotSize + gap);

            using var bg = new SKPaint { Color = new SKColor(0x14, 0x14, 0x14, attack != null || consumable != null ? (byte)0xE6 : (byte)0x90), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(x, y, slotSize, slotSize, 5, 5, bg);

            using var border = new SKPaint
            {
                Color = selected ? new SKColor(0xFF, 0xCC, 0x00) : attack != null ? new SKColor(0x66, 0x66, 0x66) : new SKColor(0x33, 0x33, 0x33),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = selected ? 2f : 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(x, y, slotSize, slotSize, 5, 5, border);

            // Bound key, top-left (configurable via settings.json hotbarKeys)
            string keyLabel = i < keyLabels.Length ? keyLabels[i] : (i + 1).ToString();
            using var keyPaint = new SKPaint { Color = new SKColor(0xFF, 0xCC, 0x00, attack != null || consumable != null ? (byte)0xFF : (byte)0x70), TextSize = 10, IsAntialias = true, Typeface = GameTypeface };
            canvas.DrawText(keyLabel, x + 4, y + 12, keyPaint);

            if (consumable != null)
            {
                // Consumable quick-slot: green monogram + carried count; the key uses one copy.
                using var monogram = new SKPaint { Color = new SKColor(0x73, 0xE6, 0x80), TextSize = 15, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
                canvas.DrawText(AttackAbbrev(consumable.Name), x + slotSize / 2f, y + slotSize / 2f + 5f, monogram);
                using var countPaint = new SKPaint { Color = new SKColor(0xAA, 0xAA, 0xAA), TextSize = 10, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
                canvas.DrawText($"x{snapshot.HotbarConsumableCounts[i]}", x + slotSize / 2f, y + slotSize - 6f, countPaint);
                continue;
            }

            if (attack == null) continue;

            // Compact attack monogram, centered ("Quick Slash" → QS)
            var abbrevColor = selected ? SKColors.White : new SKColor(0xBB, 0xBB, 0xBB);
            using var abbrevPaint = new SKPaint { Color = abbrevColor, TextSize = 16, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
            canvas.DrawText(AttackAbbrev(attack.Name), x + slotSize / 2f, y + slotSize / 2f + 7f, abbrevPaint);

            // Element identity strip along the bottom (magic attacks), gray for physical
            var element = MagicElements.For(attack);
            var (stripColor, _) = MagicColors(element, new SKColor(0x77, 0x77, 0x77), SKColors.White);
            using var strip = new SKPaint { Color = element == MagicElement.None ? new SKColor(0x55, 0x55, 0x55) : stripColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x + 4, y + slotSize - 6f, slotSize - 8f, 3f, strip);

            // Heavy-attack resource cost, top-right
            if (attack.IsHeavyAttack)
            {
                int cost = attack.StaminaCost > 0 ? attack.StaminaCost : attack.ManaCost > 0 ? attack.ManaCost : attack.FaithCost;
                using var costPaint = new SKPaint { Color = new SKColor(0x99, 0x99, 0x99), TextSize = 9, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Right };
                canvas.DrawText(cost.ToString(), x + slotSize - 4f, y + 12f, costPaint);
            }
        }
    }

    /// <summary>Two-letter monogram for a hotbar slot: initials of the first two words, or the
    /// first three letters of a single-word name ("Quick Slash" → QS, "Bow" → BOW).</summary>
    private static string AttackAbbrev(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        return name.Length <= 3 ? name.ToUpperInvariant() : name[..3].ToUpperInvariant();
    }

    private static readonly SKColor MsgSystemColor = new(0xAA, 0xAA, 0xAA);
    private static readonly SKColor MsgCombatColor = new(0xFF, 0x66, 0x66);
    private static readonly SKColor MsgLootColor = new(0xFF, 0xCC, 0x00);
    private static readonly SKColor MsgLevelUpColor = new(0x66, 0xDD, 0x66);
    private static readonly SKColor MsgWarningColor = new(0xFF, 0x99, 0x33);

    private static SKColor MessageColor(MessageKind kind) => kind switch
    {
        MessageKind.Combat => MsgCombatColor,
        MessageKind.Loot => MsgLootColor,
        MessageKind.LevelUp => MsgLevelUpColor,
        MessageKind.Warning => MsgWarningColor,
        _ => MsgSystemColor
    };

    /// <summary>
    /// The scrolling event feed (SimpleRPG's message log), drawn bottom-left above the floor info
    /// line. Shows the most recent messages, newest at the bottom; older lines fade toward the top.
    /// </summary>
    private static void DrawMessageLog(SKCanvas canvas, List<GameMessage> messages, int viewportHeight)
    {
        if (messages.Count == 0) return;

        const int maxLines = 6;
        const float lineHeight = 15f;
        const float textSize = 12f;
        float x = 10f;
        // Sit just above the bottom floor-info line (which is drawn at viewportHeight - 10).
        float bottomY = viewportHeight - 26f;

        int shown = Math.Min(maxLines, messages.Count);
        int startIndex = messages.Count - shown;

        for (int i = 0; i < shown; i++)
        {
            var msg = messages[startIndex + i];
            // Oldest visible line is dimmest; the newest is fully opaque.
            float ageT = (i + 1) / (float)shown; // (0,1], newest = 1
            byte alpha = (byte)(70 + ageT * 185);

            float y = bottomY - (shown - 1 - i) * lineHeight;

            using var shadow = new SKPaint
            {
                Color = SKColors.Black.WithAlpha((byte)(alpha * 0.8f)),
                TextSize = textSize,
                IsAntialias = true,
                Typeface = GameTypeface
            };
            using var paint = new SKPaint
            {
                Color = MessageColor(msg.Kind).WithAlpha(alpha),
                TextSize = textSize,
                IsAntialias = true,
                Typeface = GameTypeface
            };
            canvas.DrawText(msg.Text, x + 1, y + 1, shadow);
            canvas.DrawText(msg.Text, x, y, paint);
        }
    }

    /// <summary>Ambient critters: dungeon rats (dart + pause, flee the hero) and bats (drift,
    /// bob), the town's dog and cat. Fog rule matches enemies — dungeon critters render only on
    /// currently-visible tiles.</summary>
    private static void DrawCritters(SKCanvas canvas, List<Critter> critters, FogView fog)
    {
        if (critters.Count == 0) return;
        foreach (var c in critters)
        {
            int cellX = (int)MathF.Round(c.X);
            int cellY = (int)MathF.Round(c.Y);
            if (fog.Enabled && !fog.VisibleFloors.Contains((cellX, cellY))) continue;

            float px = c.X * CellSize + CellSize / 2f;
            float py = c.Y * CellSize + CellSize / 2f;

            switch (c.Kind)
            {
                case CritterKind.Rat:
                {
                    using var body = new SKPaint { Color = new SKColor(0x55, 0x4A, 0x42), IsAntialias = true };
                    canvas.DrawOval(px, py + 4f, 5f, 3f, body);
                    using var tail = new SKPaint { Color = new SKColor(0x77, 0x66, 0x5C), StrokeWidth = 1.2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
                    canvas.DrawLine(px - 5f, py + 4f, px - 10f, py + 6f, tail);
                    break;
                }
                case CritterKind.Bat:
                {
                    // Wing flap + vertical bob driven by position so it animates as it moves.
                    float flap = MathF.Sin((c.X + c.Y) * 9f) * 3f;
                    float by = py - 12f + MathF.Sin((c.X - c.Y) * 6f) * 3f;
                    using var wing = new SKPaint { Color = new SKColor(0x3A, 0x33, 0x44), StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
                    canvas.DrawLine(px - 6f, by - flap, px, by, wing);
                    canvas.DrawLine(px, by, px + 6f, by - flap, wing);
                    break;
                }
                case CritterKind.Dog:
                {
                    using var body = new SKPaint { Color = new SKColor(0x8B, 0x5A, 0x2B), IsAntialias = true };
                    canvas.DrawRoundRect(px - 8f, py - 2f, 15f, 8f, 3f, 3f, body);
                    canvas.DrawCircle(px + 9f, py - 1f, 4f, body);
                    using var tail = new SKPaint { Color = new SKColor(0x8B, 0x5A, 0x2B), StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
                    canvas.DrawLine(px - 8f, py, px - 12f, py - 5f, tail);
                    break;
                }
                case CritterKind.Cat:
                {
                    using var body = new SKPaint { Color = new SKColor(0x6E, 0x6E, 0x76), IsAntialias = true };
                    canvas.DrawRoundRect(px - 6f, py, 11f, 6f, 3f, 3f, body);
                    canvas.DrawCircle(px + 6f, py + 1f, 3f, body);
                    using var tail = new SKPaint { Color = new SKColor(0x6E, 0x6E, 0x76), StrokeWidth = 1.6f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
                    canvas.DrawLine(px - 6f, py + 2f, px - 10f, py - 3f, tail);
                    break;
                }
            }
        }
    }

    /// <summary>Rising, fading world-space combat feedback ("14", "Dodge!", "LEVEL UP!").</summary>
    private static void DrawFloatingTexts(SKCanvas canvas, List<FloatingText> floatingTexts)
    {
        foreach (var ft in floatingTexts)
        {
            float t = ft.LifeT;                       // 1 → 0 over lifetime
            float rise = (1f - t) * 26f;              // px risen so far
            float px = ft.X * CellSize + CellSize / 2f;
            float py = ft.Y * CellSize + CellSize / 2f - rise;
            byte alpha = (byte)(255 * (t < 0.35f ? t / 0.35f : 1f)); // fade out over the last third

            (SKColor color, float size) = ft.Kind switch
            {
                FloatingTextKind.HeroDamage => (new SKColor(0xFF, 0x55, 0x55), 15f),
                FloatingTextKind.Dodge => (new SKColor(0x66, 0xCC, 0xFF), 12f),
                FloatingTextKind.LevelUp => (new SKColor(0x66, 0xDD, 0x66), 16f),
                FloatingTextKind.Heal => (new SKColor(0x73, 0xE6, 0x80), 14f),
                _ => (new SKColor(0xFF, 0xEE, 0xCC), 13f), // EnemyDamage
            };

            using var shadow = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(alpha * 0.8f)), TextSize = size, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
            using var paint = new SKPaint { Color = color.WithAlpha(alpha), TextSize = size, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
            canvas.DrawText(ft.Text, px + 1f, py + 1f, shadow);
            canvas.DrawText(ft.Text, px, py, paint);
        }
    }

    /// <summary>Night in town (owner ruling 2026-08-05: "night to be night, mostly"): a heavy
    /// dark layer with light punched out of it — the hero's own light (darkvision races see
    /// their full range; a torch buys a pool; bare eyes barely arm's reach), street lamps, and
    /// the smithy's forge glow. The Night Sight skill thins the whole layer instead of widening
    /// a radius. Dungeon floors untouched — their darkness is the fog system.</summary>
    private void DrawTownLighting(SKCanvas canvas, GameState gameState, List<MazeFeature> features, int viewportWidth, int viewportHeight)
    {
        if (!gameState.IsInOverworld) return;
        float darkness = gameState.Clock.Darkness;
        if (darkness <= 0f) return;

        // Night Sight brightens the entire screen — dark becomes dusk, everywhere.
        byte maxAlpha = gameState.HasNightSight ? (byte)85 : (byte)215;
        byte alpha = (byte)(maxAlpha * darkness);
        if (alpha == 0) return;

        // Fire flicker: organic per-light variation from render-clock Perlin noise — a slow
        // wander plus a faster dance, out of phase per light so the town never pulses in sync.
        // Render-side clock: flames keep moving while the sim is paused, and the sim can't be
        // perturbed. Eyesight (darkvision / bare eyes) does not flicker — only fire does.
        float now = _flickerClock.ElapsedMilliseconds / 1000f;
        float Flicker(float phaseX, float phaseY, float amount)
        {
            float phase = phaseX * 7.31f + phaseY * 13.77f;
            float fast = (float)_flickerNoise.Noise(now * 9f + phase, phase * 0.37f);
            float slow = (float)_flickerNoise.Noise(now * 1.6f + phase * 0.5f, 42.0);
            return 1f + amount * (0.6f * fast + 0.4f * slow);
        }

        canvas.SaveLayer(null);
        using (var dark = new SKPaint { Color = new SKColor(0x07, 0x0D, 0x20, alpha) })
            canvas.DrawRect(0, 0, viewportWidth, viewportHeight, dark);

        // Punch a hole in the dark layer: a long, gradual falloff (bright core, slow tail)
        // rather than the old plateau-then-linear edge.
        void Punch(float wx, float wy, float radiusTiles, byte strength, float flicker)
        {
            float px = wx * CellSize + CellSize / 2f + _lastOffsetX;
            float py = wy * CellSize + CellSize / 2f + _lastOffsetY;
            float r = radiusTiles * CellSize * flicker;
            byte s = (byte)Math.Clamp((int)(strength * (0.9f + 0.1f * flicker)), 0, 255);
            using var hole = new SKPaint
            {
                BlendMode = SKBlendMode.DstOut,
                Shader = SKShader.CreateRadialGradient(new SKPoint(px, py), r,
                    new[]
                    {
                        SKColors.White.WithAlpha(s),
                        SKColors.White.WithAlpha((byte)(s * 0.92f)),
                        SKColors.White.WithAlpha((byte)(s * 0.72f)),
                        SKColors.White.WithAlpha((byte)(s * 0.45f)),
                        SKColors.White.WithAlpha((byte)(s * 0.20f)),
                        SKColors.White.WithAlpha(0)
                    },
                    new[] { 0f, 0.30f, 0.50f, 0.68f, 0.84f, 1f }, SKShaderTileMode.Clamp)
            };
            canvas.DrawCircle(px, py, r, hole);
        }

        // Hero light: a carried torch is open flame (dances hardest); darkvision and plain
        // eyesight are steady.
        bool heroLightIsFire = gameState.HasTorch && !gameState.Hero.HasDarkvision;
        Punch(gameState.Hero.X, gameState.Hero.Y, gameState.HeroLightRadius, 255,
            heroLightIsFire ? Flicker(0f, 0f, 0.08f) : 1f);
        foreach (var f in features)
        {
            if (f.Type == MazeFeatureType.Lamp)
                Punch(f.X, f.Y, 3.8f, 235, Flicker(f.X, f.Y, 0.035f)); // glass-caged: gentle
            else if (f.Type == MazeFeatureType.Smithy)
                Punch(f.X, f.Y, 2.4f, 190, Flicker(f.X, f.Y, 0.09f));  // open embers: lively
        }
        canvas.Restore();

        // A faint warm wash over each lamp so night lighting reads as firelight, not gray fog —
        // breathing with the same flicker as its own light pool.
        foreach (var f in features)
        {
            if (f.Type != MazeFeatureType.Lamp) continue;
            float fl = Flicker(f.X, f.Y, 0.035f);
            float px = f.X * CellSize + CellSize / 2f + _lastOffsetX;
            float py = f.Y * CellSize + CellSize / 2f + _lastOffsetY;
            using var warm = new SKPaint
            {
                BlendMode = SKBlendMode.Screen,
                Color = new SKColor(0xFF, 0xA6, 0x3C, (byte)Math.Clamp(38 * darkness * fl, 0, 255)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18)
            };
            canvas.DrawCircle(px, py, CellSize * 1.4f * fl, warm);
        }
    }

    // Fire-flicker machinery: render-only noise + clock (never touches sim state or RNG).
    private static readonly PerlinNoise _flickerNoise = new(0xF1A3);
    private static readonly System.Diagnostics.Stopwatch _flickerClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>A street lamp: iron post with a warm lantern head (the light itself is the
    /// night layer's job — this is just the fixture).</summary>
    private static void DrawLamp(SKCanvas canvas, float px, float py)
    {
        using var post = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x40), StrokeWidth = 3f, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(px, py + 14f, px, py - 12f, post);
        using var head = new SKPaint { Color = new SKColor(0xFF, 0xD2, 0x7A), IsAntialias = true };
        canvas.DrawCircle(px, py - 15f, 4.5f, head);
        using var cap = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x40), StrokeWidth = 2f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(px - 5f, py - 19f, px + 5f, py - 19f, cap);
    }

    /// <summary>"Day 3, 14:20" — top-center, town only (the dungeon keeps time to itself).</summary>
    private static void DrawClock(SKCanvas canvas, GameState gameState, int viewportWidth)
    {
        if (!gameState.IsInOverworld) return;
        string text = gameState.Clock.TimeDisplay;
        using var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(200), TextSize = 13, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
        using var paint = new SKPaint { Color = new SKColor(0xFF, 0xCC, 0x00), TextSize = 13, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Center };
        canvas.DrawText(text, viewportWidth / 2f + 1f, 21f, shadow);
        canvas.DrawText(text, viewportWidth / 2f, 20f, paint);
    }

    /// <summary>Red radial vignette that creeps in as the hero's HP drops below 50%.</summary>
    private void DrawLowHealthVignette(SKCanvas canvas, Hero hero, int viewportWidth, int viewportHeight)
    {
        float hpRatio = hero.MaxHp > 0 ? Math.Clamp((float)hero.CurrentHp / hero.MaxHp, 0f, 1f) : 0f;
        if (hpRatio >= 0.5f) return;

        float t = 1f - (hpRatio / 0.5f); // 0 at half HP, 1 at zero HP
        byte alpha = (byte)(t * 160);
        float cx = viewportWidth / 2f;
        float cy = viewportHeight / 2f;
        float r = MathF.Sqrt(viewportWidth * viewportWidth + viewportHeight * viewportHeight) / 2f;

        using var vignettePaint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                r * 0.75f,
                new[] { new SKColor(80, 0, 0, 0), new SKColor(80, 0, 0, alpha) },
                new[] { 0.45f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, viewportWidth, viewportHeight, vignettePaint);
    }
    
    private void DrawDebugOverlay(SKCanvas canvas, GameState gameState, List<Enemy> enemies, List<Projectile> projectiles)
    {
        // Early out if nothing is enabled
        if (!gameState.DebugDrawHitboxes && !gameState.DebugDrawLOS) return;

        // Hitboxes: hero, enemies, projectiles
        if (gameState.DebugDrawHitboxes)
        {
            // Hero
            DrawDebugCircle(canvas, gameState.Hero.X, gameState.Hero.Y, gameState.Hero.Radius, new SKColor(0, 200, 255, 160));
            // Enemies
            foreach (var e in enemies)
            {
                var col = e.IsAlive ? new SKColor(255, 120, 120, 160) : new SKColor(120, 60, 60, 100);
                DrawDebugCircle(canvas, e.X, e.Y, e.Radius, col);
            }
            // Projectiles
            foreach (var p in projectiles)
            {
                var col = p.Team == ProjectileTeam.Hero ? new SKColor(120, 220, 255, 140) : new SKColor(255, 180, 120, 140);
                DrawDebugCircle(canvas, p.CurrentX, p.CurrentY, p.Radius, col, dashed: true);
            }
        }

        // LOS rays: hero-to-enemy lines colored by blockage
        if (gameState.DebugDrawLOS)
        {
            foreach (var e in enemies)
            {
                bool los = gameState.CheckLOS(gameState.Hero.X, gameState.Hero.Y, e.X, e.Y);
                DrawDebugLine(canvas, gameState.Hero.X, gameState.Hero.Y, e.X, e.Y, los ? new SKColor(80, 220, 120, 150) : new SKColor(220, 80, 80, 150));
            }
        }
    }

    private void DrawDebugCircle(SKCanvas canvas, float cx, float cy, float radiusTiles, SKColor color, bool dashed = false)
    {
        float px = cx * CellSize + CellSize / 2f;
        float py = cy * CellSize + CellSize / 2f;
        float r = radiusTiles * CellSize;
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        if (dashed)
        {
            paint.PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0);
        }
        canvas.DrawCircle(px, py, r, paint);
    }

    private void DrawDebugLine(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color)
    {
        float p1x = x1 * CellSize + CellSize / 2f;
        float p1y = y1 * CellSize + CellSize / 2f;
        float p2x = x2 * CellSize + CellSize / 2f;
        float p2y = y2 * CellSize + CellSize / 2f;
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawLine(p1x, p1y, p2x, p2y, paint);
    }

    private void DrawHitEffects(SKCanvas canvas, List<HitEffect> effects)
    {
        foreach (var fx in effects)
        {
            float px = fx.X * CellSize + CellSize / 2f;
            float py = fx.Y * CellSize + CellSize / 2f;
            float t = Math.Clamp((float)fx.LifeTime / fx.MaxLifeTime, 0f, 1f);

            // A style with an authored impact burst (arrow shatter, dart burst) plays it in
            // place of the generic flash.
            if (AttackFxService.DrawImpact(canvas, fx.Visual, px, py, t, CellSize)) continue;

            byte alpha = (byte)(255 * (1f - t));
            float radius = 6f + 8f * t;

            // Color by team: hero hits = cyan/white, enemy hits = orange/red
            SKColor glowColor = fx.Team == ProjectileTeam.Hero
                ? new SKColor(180, 255, 255, (byte)(alpha * 0.7f))
                : new SKColor(255, 170, 100, (byte)(alpha * 0.7f));
            SKColor coreColor = fx.Team == ProjectileTeam.Hero
                ? new SKColor(230, 255, 255, alpha)
                : new SKColor(255, 200, 150, alpha);

            using (var glow = new SKPaint
            {
                Color = glowColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
                BlendMode = SKBlendMode.Screen
            })
            {
                canvas.DrawCircle(px, py, radius, glow);
            }
            using (var core = new SKPaint
            {
                Color = coreColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            })
            {
                canvas.DrawCircle(px, py, Math.Max(2f, radius * 0.35f), core);
            }
        }
    }
    
    /// <summary>
    /// Compute this frame's fog view: which floor tiles the hero can currently see (sight radius
    /// + line of sight), unioned into the persistent seen-set. Fog only applies on regular
    /// dungeon floors — the Overworld and safe rooms are fully lit spaces.
    /// </summary>
    private FogView ComputeFogView(GameState gameState, Maze maze)
    {
        bool enabled = !gameState.IsInOverworld && !gameState.IsInSafeRoom;

        // New maze (new floor/space): forget the old maze's seen tiles.
        if (!ReferenceEquals(_fogMaze, maze))
        {
            _fogMaze = maze;
            _seenFloors.Clear();
        }

        var visible = new HashSet<(int x, int y)>();
        if (!enabled) return new FogView(false, visible, _seenFloors);

        float heroX = gameState.Hero.X;
        float heroY = gameState.Hero.Y;
        float range = gameState.VisionRange;
        int minX = Math.Max(0, (int)MathF.Floor(heroX - range));
        int maxX = Math.Min(maze.Width - 1, (int)MathF.Ceiling(heroX + range));
        int minY = Math.Max(0, (int)MathF.Floor(heroY - range));
        int maxY = Math.Min(maze.Height - 1, (int)MathF.Ceiling(heroY + range));

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (maze.Walls[x, y]) continue;
                float dx = x - heroX;
                float dy = y - heroY;
                if (dx * dx + dy * dy > range * range) continue;
                if (!gameState.CheckLOS(heroX, heroY, x, y)) continue;
                visible.Add((x, y));
                _seenFloors.Add((x, y));
            }
        }

        // The walked trail also counts as remembered, so pre-fog saves/old floors stay sane.
        for (int x = 0; x < maze.Width; x++)
            for (int y = 0; y < maze.Height; y++)
                if (maze.Explored[x, y] && !maze.Walls[x, y])
                    _seenFloors.Add((x, y));

        return new FogView(true, visible, _seenFloors);
    }

    /// <summary>A wall is lit by the floors around it: fully if any neighboring floor is
    /// currently visible, dimly if any was ever seen (walls themselves block LOS, so they're
    /// never in the floor sets).</summary>
    private static (bool visible, bool seen) WallLight(FogView fog, Maze maze, int x, int y)
    {
        if (!fog.Enabled) return (true, true);
        bool visible = false, seen = false;
        for (int nx = x - 1; nx <= x + 1; nx++)
        {
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                if (nx < 0 || ny < 0 || nx >= maze.Width || ny >= maze.Height || maze.Walls[nx, ny]) continue;
                if (fog.VisibleFloors.Contains((nx, ny))) return (true, true);
                if (fog.SeenFloors.Contains((nx, ny))) seen = true;
            }
        }
        return (visible, seen);
    }

    // Door slab colors — deliberately warm wood against the cool masonry palettes so a shut door
    // reads as "openable" at a glance. Locked doors carry a gold lock plate (gold = key).
    private static readonly SKColor DoorWoodColor = new(0x9A, 0x6A, 0x35);
    private static readonly SKColor DoorWoodDarkColor = new(0x5C, 0x3C, 0x1E);
    private static readonly SKColor DoorLockedWoodColor = new(0x7E, 0x52, 0x2A);
    private static readonly SKColor DoorHandleColor = new(0xD8, 0xD8, 0xD8);
    private static readonly SKColor DoorLockColor = new(0xFF, 0xCC, 0x00);

    /// <summary>
    /// A closed or locked door: dark opening in the wall line with a wood slab across it,
    /// oriented along the wall run it sits in. Distinct from walls by design — the bump-to-open
    /// rule only works if the player can tell a door from masonry.
    /// </summary>
    private static void DrawShutDoor(SKCanvas canvas, Maze maze, int x, int y, float px, float py,
        bool locked, bool visible)
    {
        byte alpha = visible ? (byte)0xFF : DimAlpha;

        // The cell reads as an opening: dark floor behind the slab, not wall fill.
        using var backPaint = new SKPaint { Color = FloorColor.WithAlpha(alpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        canvas.DrawRect(px, py, CellSize, CellSize, backPaint);

        // Slab orientation follows the wall run: solid masonry left+right → horizontal door;
        // above+below → vertical; anything else (freestanding, double doors) → full-cell slab.
        bool SolidAt(int nx, int ny)
        {
            var t = maze.TileAt(nx, ny);
            return t.BlocksSight() && !t.IsDoor();
        }
        bool horizontal = SolidAt(x - 1, y) && SolidAt(x + 1, y);
        bool vertical = !horizontal && SolidAt(x, y - 1) && SolidAt(x, y + 1);

        float inset = CellSize * 0.08f;
        float thickness = CellSize * 0.45f;
        SKRect slab = horizontal
            ? new SKRect(px + inset, py + (CellSize - thickness) / 2f, px + CellSize - inset, py + (CellSize + thickness) / 2f)
            : vertical
                ? new SKRect(px + (CellSize - thickness) / 2f, py + inset, px + (CellSize + thickness) / 2f, py + CellSize - inset)
                : new SKRect(px + inset, py + inset, px + CellSize - inset, py + CellSize - inset);

        var wood = (locked ? DoorLockedWoodColor : DoorWoodColor).WithAlpha(alpha);
        using var slabPaint = new SKPaint { Color = wood, Style = SKPaintStyle.Fill, IsAntialias = false };
        canvas.DrawRect(slab, slabPaint);

        using var framePaint = new SKPaint { Color = DoorWoodDarkColor.WithAlpha(alpha), Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = false };
        canvas.DrawRect(slab, framePaint);

        // Plank seams across the slab.
        using var seamPaint = new SKPaint { Color = DoorWoodDarkColor.WithAlpha(alpha), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false };
        for (int i = 1; i <= 2; i++)
        {
            if (horizontal)
            {
                float sx = slab.Left + slab.Width * i / 3f;
                canvas.DrawLine(sx, slab.Top, sx, slab.Bottom, seamPaint);
            }
            else
            {
                float sy = slab.Top + slab.Height * i / 3f;
                canvas.DrawLine(slab.Left, sy, slab.Right, sy, seamPaint);
            }
        }

        if (locked)
        {
            // Gold lock plate with a dark keyhole, dead center.
            using var lockPaint = new SKPaint { Color = DoorLockColor.WithAlpha(alpha), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(slab.MidX, slab.MidY, CellSize * 0.09f, lockPaint);
            using var holePaint = new SKPaint { Color = DoorWoodDarkColor.WithAlpha(alpha), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(slab.MidX, slab.MidY, CellSize * 0.035f, holePaint);
        }
        else
        {
            // Plain handle dot, off-center along the slab.
            using var handlePaint = new SKPaint { Color = DoorHandleColor.WithAlpha(alpha), Style = SKPaintStyle.Fill, IsAntialias = true };
            float hx = horizontal ? slab.Right - slab.Width * 0.18f : slab.MidX;
            float hy = horizontal ? slab.MidY : slab.Bottom - slab.Height * 0.18f;
            canvas.DrawCircle(hx, hy, CellSize * 0.05f, handlePaint);
        }
    }

    private static DungeonPalette PaletteFor(Maze maze) => maze.Dungeon?.Theme switch
    {
        DungeonTheme.Castle => new DungeonPalette(
            new SKColor(0x65, 0x63, 0x68), new SKColor(0x91, 0x8D, 0x91), new SKColor(0x3B, 0x39, 0x3E),
            new SKColor(0x1C, 0x1C, 0x21), new SKColor(0x29, 0x28, 0x2D), new SKColor(0x24, 0x2B, 0x32),
            new SKColor(0x35, 0x30, 0x21), new SKColor(0x36, 0x25, 0x29), new SKColor(0x25, 0x32, 0x2A)),
        DungeonTheme.Sewer => new DungeonPalette(
            new SKColor(0x3F, 0x52, 0x49), new SKColor(0x66, 0x78, 0x6A), new SKColor(0x30, 0x42, 0x36),
            new SKColor(0x15, 0x1D, 0x1A), new SKColor(0x20, 0x2B, 0x25), new SKColor(0x20, 0x30, 0x2C),
            new SKColor(0x31, 0x31, 0x1D), new SKColor(0x32, 0x26, 0x20), new SKColor(0x20, 0x35, 0x29)),
        DungeonTheme.Cemetery => new DungeonPalette(
            new SKColor(0x50, 0x52, 0x45), new SKColor(0x76, 0x78, 0x68), new SKColor(0x42, 0x3C, 0x30),
            new SKColor(0x1C, 0x1D, 0x19), new SKColor(0x2B, 0x28, 0x22), new SKColor(0x28, 0x2E, 0x29),
            new SKColor(0x36, 0x30, 0x1E), new SKColor(0x34, 0x24, 0x25), new SKColor(0x25, 0x31, 0x28)),
        DungeonTheme.Library => new DungeonPalette(
            new SKColor(0x68, 0x47, 0x34), new SKColor(0x9B, 0x70, 0x49), new SKColor(0x4D, 0x35, 0x28),
            new SKColor(0x20, 0x18, 0x17), new SKColor(0x31, 0x24, 0x20), new SKColor(0x29, 0x2A, 0x32),
            new SKColor(0x3A, 0x31, 0x1D), new SKColor(0x38, 0x23, 0x22), new SKColor(0x24, 0x31, 0x2B)),
        DungeonTheme.Forge => new DungeonPalette(
            new SKColor(0x67, 0x35, 0x38), new SKColor(0xA0, 0x55, 0x40), new SKColor(0x55, 0x31, 0x2E),
            new SKColor(0x22, 0x17, 0x19), new SKColor(0x32, 0x22, 0x25), new SKColor(0x2C, 0x29, 0x32),
            new SKColor(0x41, 0x31, 0x1D), new SKColor(0x42, 0x22, 0x20), new SKColor(0x29, 0x35, 0x29)),
        DungeonTheme.Hideout => new DungeonPalette(
            new SKColor(0x53, 0x4A, 0x3C), new SKColor(0x7B, 0x70, 0x56), new SKColor(0x41, 0x39, 0x2C),
            new SKColor(0x1C, 0x1B, 0x17), new SKColor(0x2A, 0x28, 0x20), new SKColor(0x27, 0x2D, 0x2B),
            new SKColor(0x39, 0x30, 0x1D), new SKColor(0x36, 0x25, 0x20), new SKColor(0x25, 0x32, 0x28)),
        _ => new DungeonPalette(
            WallColor, WallDetailColor, FloorDotColor, CorridorFloorColor, StandardRoomFloorColor,
            EntranceRoomFloorColor, TreasureRoomFloorColor, HazardRoomFloorColor, ExitRoomFloorColor)
    };

    private static SKColor DungeonFloorColor(Maze maze, int x, int y, DungeonPalette palette)
    {
        var layout = maze.Dungeon;
        if (layout == null) return FloorColor;
        if (layout.Tiles[x, y] is DungeonTileType.CorridorFloor or DungeonTileType.Doorway)
            return palette.Corridor;

        return layout.RoomAt(x, y)?.Role switch
        {
            DungeonRoomRole.Entrance => palette.EntranceRoom,
            DungeonRoomRole.Treasure => palette.TreasureRoom,
            DungeonRoomRole.Hazard => palette.HazardRoom,
            DungeonRoomRole.Exit => palette.ExitRoom,
            _ => palette.StandardRoom
        };
    }

    private void DrawMaze(SKCanvas canvas, Maze maze, FogView fog)
    {
        var palette = PaletteFor(maze);
        using var floorPaint = new SKPaint { Color = FloorColor, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var floorDotPaint = new SKPaint { Color = palette.FloorDetail, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallPaint = new SKPaint { Color = palette.Wall, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallDetailPaint = new SKPaint { Color = palette.WallDetail, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };
        using var doorwayBackPaint = new SKPaint { Color = palette.Wall, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, StrokeCap = SKStrokeCap.Round, IsAntialias = false };
        using var doorwayPaint = new SKPaint { Color = palette.WallDetail, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, IsAntialias = false };

        using var floorPaintDim = new SKPaint { Color = FloorColor.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var floorDotPaintDim = new SKPaint { Color = palette.FloorDetail.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallPaintDim = new SKPaint { Color = palette.Wall.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallDetailPaintDim = new SKPaint { Color = palette.WallDetail.WithAlpha(DimAlpha), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };
        using var doorwayBackPaintDim = new SKPaint { Color = palette.Wall.WithAlpha(DimAlpha), Style = SKPaintStyle.Stroke, StrokeWidth = 7f, StrokeCap = SKStrokeCap.Round, IsAntialias = false };
        using var doorwayPaintDim = new SKPaint { Color = palette.WallDetail.WithAlpha(DimAlpha), Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round, IsAntialias = false };

        float dotSize = CellSize * 0.2f;
        float dotOffset = CellSize * 0.4f;

        for (int x = 0; x < maze.Width; x++)
        {
            for (int y = 0; y < maze.Height; y++)
            {
                float px = x * CellSize;
                float py = y * CellSize;

                var tile = maze.Tiles[x, y];
                if (tile is TileType.DoorClosed or TileType.DoorLocked)
                {
                    // Shut doors block movement and sight like walls, so they take wall-style
                    // fog lighting — but they must never LOOK like walls (a door drawn as
                    // masonry is an invisible door the player walks past).
                    var (doorVisible, doorSeen) = WallLight(fog, maze, x, y);
                    if (!doorVisible && !doorSeen) continue; // never seen: pure black void
                    DrawShutDoor(canvas, maze, x, y, px, py,
                        locked: tile == TileType.DoorLocked, visible: doorVisible);
                }
                else if (maze.Walls[x, y])
                {
                    var (visible, seen) = WallLight(fog, maze, x, y);
                    if (!visible && !seen) continue; // never seen: pure black void

                    canvas.DrawRect(px, py, CellSize, CellSize, visible ? wallPaint : wallPaintDim);
                    if (maze.Dungeon != null)
                    {
                        TerrainService.DrawTile(canvas, maze.Dungeon.Theme, "wall.fill",
                            x, y, px, py, CellSize, visible ? (byte)180 : (byte)54);
                        DrawWallTopology(canvas, maze, x, y, px, py,
                            visible ? wallDetailPaint : wallDetailPaintDim);
                    }
                    else
                    {
                        canvas.DrawRect(px + 2, py + 2, CellSize - 4, CellSize - 4,
                            visible ? wallDetailPaint : wallDetailPaintDim);
                    }
                }
                else
                {
                    if (!fog.FloorSeen(x, y)) continue; // never seen: pure black void
                    bool visible = fog.FloorVisible(x, y);

                    var tileFloorColor = DungeonFloorColor(maze, x, y, palette);
                    floorPaint.Color = tileFloorColor;
                    floorPaintDim.Color = tileFloorColor.WithAlpha(DimAlpha);

                    canvas.DrawRect(px, py, CellSize, CellSize, visible ? floorPaint : floorPaintDim);
                    string terrainSprite = TerrainSpriteId(maze, x, y);
                    bool textured = maze.Dungeon != null && TerrainService.DrawTile(
                        canvas, maze.Dungeon.Theme, terrainSprite, x, y, px, py, CellSize,
                        visible ? (byte)170 : (byte)51);
                    // Threshold marking for doorway cells — including an opened door outside a
                    // dungeon (town buildings), so the doorway stays readable once it's open.
                    if (maze.Dungeon?.Tiles[x, y] == DungeonTileType.Doorway ||
                        maze.Tiles[x, y] == TileType.DoorOpen)
                    {
                        DrawDoorwayThreshold(canvas, terrainSprite, px, py,
                            visible ? doorwayBackPaint : doorwayBackPaintDim,
                            visible ? doorwayPaint : doorwayPaintDim);
                    }
                    bool isRoomFloor = maze.Dungeon == null ||
                        maze.Dungeon.Tiles[x, y] == DungeonTileType.RoomFloor;
                    if (isRoomFloor && !textured)
                    {
                        canvas.DrawRect(px + dotOffset, py + dotOffset, dotSize, dotSize,
                            visible ? floorDotPaint : floorDotPaintDim);
                    }
                }
            }
        }
    }

    private static string TerrainSpriteId(Maze maze, int x, int y)
    {
        var tile = maze.Dungeon?.Tiles[x, y] ?? DungeonTileType.RoomFloor;
        if (tile == DungeonTileType.CorridorFloor) return "floor.corridor";
        if (tile != DungeonTileType.Doorway) return "floor.room";

        return maze.Dungeon!.DoorwayOrientationAt(x, y) == DungeonPassageOrientation.EastWest
            ? "doorway.east-west"
            : "doorway.north-south";
    }

    private static void DrawWallTopology(
        SKCanvas canvas,
        Maze maze,
        int tileX,
        int tileY,
        float x,
        float y,
        SKPaint edgePaint)
    {
        const float inset = 2f;
        float right = x + CellSize;
        float bottom = y + CellSize;

        if (IsOpen(maze, tileX, tileY - 1))
            canvas.DrawLine(x + inset, y + inset, right - inset, y + inset, edgePaint);
        if (IsOpen(maze, tileX + 1, tileY))
            canvas.DrawLine(right - inset, y + inset, right - inset, bottom - inset, edgePaint);
        if (IsOpen(maze, tileX, tileY + 1))
            canvas.DrawLine(x + inset, bottom - inset, right - inset, bottom - inset, edgePaint);
        if (IsOpen(maze, tileX - 1, tileY))
            canvas.DrawLine(x + inset, y + inset, x + inset, bottom - inset, edgePaint);
    }

    private static void DrawDoorwayThreshold(
        SKCanvas canvas,
        string spriteId,
        float x,
        float y,
        SKPaint backPaint,
        SKPaint frontPaint)
    {
        const float margin = 9f;
        float centerX = x + CellSize / 2f;
        float centerY = y + CellSize / 2f;
        if (spriteId == "doorway.east-west")
        {
            canvas.DrawLine(centerX, y + margin, centerX, y + CellSize - margin, backPaint);
            canvas.DrawLine(centerX, y + margin, centerX, y + CellSize - margin, frontPaint);
        }
        else
        {
            canvas.DrawLine(x + margin, centerY, x + CellSize - margin, centerY, backPaint);
            canvas.DrawLine(x + margin, centerY, x + CellSize - margin, centerY, frontPaint);
        }
    }

    private static bool IsOpen(Maze maze, int x, int y) =>
        x >= 0 && y >= 0 && x < maze.Width && y < maze.Height && !maze.Walls[x, y];
    
    private void DrawFeatures(SKCanvas canvas, Maze maze, List<MazeFeature> features, FogView fog)
    {
        foreach (var feature in features)
        {
            if (feature.IsUsed) continue;

            // Fog: features on never-seen tiles don't exist yet; on remembered-but-out-of-sight
            // tiles they render dimmed (a translucent layer, same 30% as the tiles under them).
            int cellX = (int)MathF.Round(feature.X);
            int cellY = (int)MathF.Round(feature.Y);
            if (!fog.FloorSeen(cellX, cellY)) continue;
            bool dimmed = !fog.FloorVisible(cellX, cellY);
            // A hidden, not-yet-perceived trap is nearly invisible until the hero notices it.
            bool unperceived = feature.Hidden && !feature.Perceived;
            int layer = 0;
            if (dimmed || unperceived)
            {
                byte a = unperceived ? (byte)28 : DimAlpha;
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha(a) };
                layer = canvas.SaveLayer(layerPaint);
            }

            float px = feature.X * CellSize + CellSize / 2f;
            float py = feature.Y * CellSize + CellSize / 2f;

            switch (feature.Type)
            {
                case MazeFeatureType.Stairs:
                    DrawStairs(canvas, px, py);
                    break;
                    
                case MazeFeatureType.Chest:
                    DrawChest(canvas, px, py, feature);
                    break;

                case MazeFeatureType.Shrine:
                    DrawShrine(canvas, px, py);
                    break;

                case MazeFeatureType.GuardianDoor:
                    DrawGuardianDoor(canvas, px, py);
                    break;

                case MazeFeatureType.Trap:
                    DrawTrap(canvas, px, py);
                    break;

                case MazeFeatureType.DungeonEntrance:
                    DrawOverworldPoint(canvas, px, py, new SKColor(90, 90, 100), "▲"); // mountain-ish triangle glyph
                    break;

                case MazeFeatureType.MineEntrance:
                    DrawOverworldPoint(canvas, px, py, new SKColor(120, 90, 60), "⛏"); // pickaxe glyph
                    break;

                case MazeFeatureType.Smithy:
                    DrawOverworldPoint(canvas, px, py, new SKColor(200, 100, 40), "⚒"); // hammer-and-pick glyph
                    break;

                case MazeFeatureType.Stall:
                    DrawOverworldPoint(canvas, px, py, new SKColor(210, 180, 60), "$");
                    break;

                case MazeFeatureType.Lamp:
                    DrawLamp(canvas, px, py);
                    break;
            }

            if (dimmed || unperceived) canvas.RestoreToCount(layer);
        }
    }

    private static void DrawDecorations(SKCanvas canvas, Maze maze, FogView fog)
    {
        var decorations = maze.Dungeon?.Decorations;
        if (decorations == null) return;

        foreach (var decoration in decorations)
        {
            if (!fog.FloorSeen(decoration.X, decoration.Y)) continue;
            bool dimmed = !fog.FloorVisible(decoration.X, decoration.Y);
            int layer = 0;
            if (dimmed)
            {
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha(DimAlpha) };
                layer = canvas.SaveLayer(layerPaint);
            }

            float x = decoration.X * CellSize + CellSize / 2f;
            float y = decoration.Y * CellSize + CellSize / 2f;
            DrawDecoration(canvas, x, y, decoration);

            if (dimmed) canvas.RestoreToCount(layer);
        }
    }

    private static void DrawThemeFeatures(SKCanvas canvas, Maze maze, FogView fog)
    {
        var features = maze.Dungeon?.ThemeFeatures;
        if (features == null) return;

        foreach (var feature in features)
        {
            if (!fog.FloorSeen(feature.X, feature.Y)) continue;
            bool dimmed = !fog.FloorVisible(feature.X, feature.Y);
            bool exhausted = feature.IsTriggered && feature.Type is
                DungeonThemeFeatureType.CastleAlarm or
                DungeonThemeFeatureType.RestlessGrave or
                DungeonThemeFeatureType.ArcaneWard or
                DungeonThemeFeatureType.HideoutTripwire;
            int layer = 0;
            if (dimmed || exhausted)
            {
                byte alpha = dimmed ? DimAlpha : (byte)150;
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
                layer = canvas.SaveLayer(layerPaint);
            }

            float x = feature.X * CellSize + CellSize / 2f;
            float y = feature.Y * CellSize + CellSize / 2f;
            DrawThemeFeature(canvas, x, y, feature);

            if (dimmed || exhausted) canvas.RestoreToCount(layer);
        }
    }

    private static void DrawThemeFeature(
        SKCanvas canvas,
        float x,
        float y,
        DungeonThemeFeature feature)
    {
        using var dark = new SKPaint
        {
            Color = new SKColor(0x18, 0x18, 0x18),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var solid = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var detail = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        // No canvas transforms belong here. Every landmark retains this authored, screen-facing
        // orientation regardless of room layout or the direction an actor travels through it.
        switch (feature.Type)
        {
            case DungeonThemeFeatureType.CastleAlarm:
                detail.Color = new SKColor(0x71, 0x55, 0x35);
                canvas.DrawLine(x - 17, y + 16, x - 17, y - 16, detail);
                canvas.DrawLine(x + 17, y + 16, x + 17, y - 16, detail);
                canvas.DrawLine(x - 17, y - 16, x + 17, y - 16, detail);
                solid.Color = feature.IsTriggered
                    ? new SKColor(0x74, 0x6B, 0x58)
                    : new SKColor(0xD4, 0xA8, 0x3F);
                canvas.DrawOval(new SKRect(x - 12, y - 10, x + 12, y + 10), solid);
                canvas.DrawOval(new SKRect(x - 12, y - 10, x + 12, y + 10), dark);
                canvas.DrawCircle(x, y + 12, 3, solid);
                break;

            case DungeonThemeFeatureType.SewerRunoff:
                solid.Color = new SKColor(0x48, 0x79, 0x3A, 0xD8);
                canvas.DrawOval(new SKRect(x - 22, y - 12, x + 20, y + 13), solid);
                canvas.DrawCircle(x - 19, y + 8, 7, solid);
                canvas.DrawCircle(x + 18, y - 7, 6, solid);
                detail.Color = new SKColor(0x91, 0xB0, 0x54);
                detail.StrokeWidth = 2f;
                canvas.DrawCircle(x - 7, y - 2, 4, detail);
                canvas.DrawCircle(x + 8, y + 5, 3, detail);
                break;

            case DungeonThemeFeatureType.RestlessGrave:
                solid.Color = feature.IsTriggered
                    ? new SKColor(0x61, 0x62, 0x5D)
                    : new SKColor(0x83, 0x84, 0x78);
                canvas.DrawRoundRect(x - 13, y - 20, 26, 38, 3, 3, solid);
                canvas.DrawRoundRect(x - 13, y - 20, 26, 38, 3, 3, dark);
                detail.Color = new SKColor(0xB0, 0xAD, 0x94);
                detail.StrokeWidth = 2f;
                canvas.DrawLine(x, y - 13, x, y + 7, detail);
                canvas.DrawLine(x - 7, y - 6, x + 7, y - 6, detail);
                break;

            case DungeonThemeFeatureType.ArcaneWard:
                solid.Color = feature.IsTriggered
                    ? new SKColor(0x4C, 0x55, 0x66, 0x80)
                    : new SKColor(0x55, 0xA7, 0xD8, 0x70);
                canvas.DrawCircle(x, y, 22, solid);
                detail.Color = feature.IsTriggered
                    ? new SKColor(0x69, 0x72, 0x80)
                    : new SKColor(0x8C, 0xD9, 0xFF);
                detail.StrokeWidth = 2.5f;
                canvas.DrawCircle(x, y, 18, detail);
                canvas.DrawRect(x - 10, y - 10, 20, 20, detail);
                canvas.DrawLine(x, y - 17, x, y + 17, detail);
                canvas.DrawLine(x - 17, y, x + 17, y, detail);
                break;

            case DungeonThemeFeatureType.HeatVent:
                solid.Color = feature.CooldownTicks > 0
                    ? new SKColor(0xD9, 0x59, 0x24, 0xA0)
                    : new SKColor(0x6B, 0x2D, 0x1F, 0x70);
                canvas.DrawCircle(x, y, 23, solid);
                solid.Color = new SKColor(0x3D, 0x3E, 0x3C);
                canvas.DrawRect(x - 17, y - 17, 34, 34, solid);
                canvas.DrawRect(x - 17, y - 17, 34, 34, dark);
                detail.Color = new SKColor(0xA0, 0x86, 0x6A);
                detail.StrokeWidth = 2.5f;
                for (int offset = -10; offset <= 10; offset += 10)
                {
                    canvas.DrawLine(x - 13, y + offset, x + 13, y + offset, detail);
                    canvas.DrawLine(x + offset, y - 13, x + offset, y + 13, detail);
                }
                break;

            case DungeonThemeFeatureType.HideoutTripwire:
                detail.Color = feature.IsTriggered
                    ? new SKColor(0x6B, 0x65, 0x59)
                    : new SKColor(0xC5, 0xB6, 0x8A);
                detail.StrokeWidth = 2f;
                canvas.DrawLine(x - 23, y + 9, x + 23, y + 9, detail);
                solid.Color = new SKColor(0x75, 0x60, 0x43);
                canvas.DrawCircle(x - 23, y + 9, 5, solid);
                canvas.DrawCircle(x + 23, y + 9, 5, solid);
                solid.Color = new SKColor(0x9A, 0x8C, 0x68);
                canvas.DrawRect(x - 10, y - 10, 8, 14, solid);
                canvas.DrawRect(x + 4, y - 7, 8, 14, solid);
                canvas.DrawLine(x - 6, y + 4, x - 3, y + 9, detail);
                canvas.DrawLine(x + 8, y + 7, x + 6, y + 9, detail);
                break;
        }
    }

    private static void DrawDecoration(SKCanvas canvas, float x, float y, DungeonDecoration decoration)
    {
        using var outline = new SKPaint
        {
            Color = new SKColor(0x12, 0x12, 0x12),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        using var primary = new SKPaint
        {
            Color = new SKColor(0x72, 0x68, 0x56),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var secondary = new SKPaint
        {
            Color = new SKColor(0xA0, 0x91, 0x70),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        // Orthographic contract: environmental art keeps its authored screen-facing orientation.
        // Variant may select non-directional details later, but must never rotate the asset.
        switch (decoration.Type)
        {
            case DungeonDecorationType.Rubble:
                primary.Color = new SKColor(0x59, 0x5A, 0x58);
                canvas.DrawCircle(x - 7, y + 4, 5, primary);
                canvas.DrawCircle(x + 2, y - 3, 7, primary);
                canvas.DrawCircle(x + 9, y + 6, 4, primary);
                break;

            case DungeonDecorationType.Bones:
                secondary.Color = new SKColor(0xC0, 0xB8, 0x9A);
                canvas.DrawLine(x - 10, y - 7, x + 10, y + 7, secondary);
                canvas.DrawLine(x - 9, y + 8, x + 8, y - 9, secondary);
                canvas.DrawCircle(x - 10, y - 7, 2.5f, secondary);
                canvas.DrawCircle(x + 10, y + 7, 2.5f, secondary);
                break;

            case DungeonDecorationType.Crate:
                primary.Color = new SKColor(0x70, 0x4F, 0x2E);
                canvas.DrawRect(x - 12, y - 12, 24, 24, primary);
                canvas.DrawRect(x - 12, y - 12, 24, 24, outline);
                canvas.DrawLine(x - 9, y - 9, x + 9, y + 9, secondary);
                canvas.DrawLine(x + 9, y - 9, x - 9, y + 9, secondary);
                break;

            case DungeonDecorationType.Barrel:
                primary.Color = new SKColor(0x69, 0x49, 0x2C);
                canvas.DrawOval(new SKRect(x - 10, y - 14, x + 10, y + 14), primary);
                canvas.DrawOval(new SKRect(x - 10, y - 14, x + 10, y + 14), outline);
                canvas.DrawLine(x - 9, y - 5, x + 9, y - 5, secondary);
                canvas.DrawLine(x - 9, y + 5, x + 9, y + 5, secondary);
                break;

            case DungeonDecorationType.Bedroll:
                primary.Color = new SKColor(0x3F, 0x65, 0x67);
                canvas.DrawRoundRect(x - 15, y - 8, 30, 16, 4, 4, primary);
                canvas.DrawRoundRect(x - 15, y - 8, 30, 16, 4, 4, outline);
                canvas.DrawLine(x + 7, y - 7, x + 7, y + 7, secondary);
                break;

            case DungeonDecorationType.Banner:
            {
                primary.Color = new SKColor(0x86, 0x32, 0x35);
                canvas.DrawRect(x - 11, y - 13, 22, 23, primary);
                using var bannerTail = BannerTail(x, y);
                canvas.DrawPath(bannerTail, primary);
                canvas.DrawLine(x - 14, y - 14, x + 14, y - 14, secondary);
                break;
            }

            case DungeonDecorationType.Brazier:
                primary.Color = new SKColor(0x58, 0x5D, 0x60);
                canvas.DrawCircle(x, y, 12, primary);
                canvas.DrawCircle(x, y, 12, outline);
                primary.Color = new SKColor(0xD8, 0x73, 0x2F);
                canvas.DrawCircle(x, y, 7, primary);
                primary.Color = new SKColor(0xF1, 0xC7, 0x57);
                canvas.DrawCircle(x - 1, y - 1, 3.5f, primary);
                break;

            case DungeonDecorationType.Mushrooms:
                secondary.Color = new SKColor(0x9A, 0x9C, 0x82);
                canvas.DrawLine(x - 7, y + 9, x - 7, y, secondary);
                canvas.DrawLine(x + 5, y + 8, x + 5, y - 4, secondary);
                primary.Color = new SKColor(0x69, 0x75, 0x52);
                canvas.DrawOval(new SKRect(x - 14, y - 4, x, y + 4), primary);
                canvas.DrawOval(new SKRect(x - 3, y - 9, x + 13, y), primary);
                break;

            case DungeonDecorationType.BrokenTable:
                primary.Color = new SKColor(0x62, 0x47, 0x30);
                canvas.DrawRect(x - 14, y - 8, 23, 16, primary);
                canvas.DrawLine(x - 12, y - 10, x + 12, y + 10, outline);
                canvas.DrawLine(x - 12, y + 11, x - 16, y + 16, secondary);
                canvas.DrawLine(x + 8, y + 8, x + 13, y + 15, secondary);
                break;

            case DungeonDecorationType.Rune:
                secondary.Color = new SKColor(0x62, 0x9A, 0xA8);
                canvas.DrawCircle(x, y, 12, secondary);
                canvas.DrawLine(x, y - 9, x + 8, y + 7, secondary);
                canvas.DrawLine(x + 8, y + 7, x - 8, y + 7, secondary);
                canvas.DrawLine(x - 8, y + 7, x, y - 9, secondary);
                break;

            case DungeonDecorationType.Campfire:
                secondary.Color = new SKColor(0x65, 0x43, 0x2A);
                canvas.DrawLine(x - 11, y - 7, x + 11, y + 7, secondary);
                canvas.DrawLine(x + 11, y - 7, x - 11, y + 7, secondary);
                primary.Color = new SKColor(0xD4, 0x62, 0x2D);
                canvas.DrawCircle(x, y, 8, primary);
                primary.Color = new SKColor(0xF0, 0xB8, 0x45);
                canvas.DrawCircle(x, y + 1, 4, primary);
                break;

            case DungeonDecorationType.WeaponRack:
                secondary.Color = new SKColor(0x82, 0x64, 0x42);
                canvas.DrawLine(x - 13, y - 9, x - 13, y + 10, secondary);
                canvas.DrawLine(x + 13, y - 9, x + 13, y + 10, secondary);
                canvas.DrawLine(x - 14, y - 5, x + 14, y - 5, secondary);
                canvas.DrawLine(x - 8, y - 12, x - 2, y + 11, outline);
                canvas.DrawLine(x + 7, y - 12, x + 2, y + 11, outline);
                break;
        }
    }

    private static SKPath BannerTail(float x, float y)
    {
        var path = new SKPath();
        path.MoveTo(x - 11, y + 8);
        path.LineTo(x, y + 15);
        path.LineTo(x + 11, y + 8);
        path.Close();
        return path;
    }

    // Simple placeholder marker for Overworld points of interest: a colored circle with a glyph.
    // Deliberately minimal — this is a first-slice visual, not final town art.
    private void DrawOverworldPoint(SKCanvas canvas, float x, float y, SKColor color, string glyph)
    {
        using var circlePaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, 12, circlePaint);

        using var ringPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, 12, ringPaint);

        using var glyphPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 14,
            TextAlign = SKTextAlign.Center,
            IsAntialias = true
        };
        canvas.DrawText(glyph, x, y + 5, glyphPaint);
    }

    // Faint mana-blue glow, per spec: a soft pulsing halo around a small bright core.
    private void DrawShrine(SKCanvas canvas, float x, float y)
    {
        using var glowPaint = new SKPaint
        {
            Color = new SKColor(90, 140, 255, 60),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, 14, glowPaint);

        using var corePaint = new SKPaint
        {
            Color = new SKColor(130, 170, 255, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(x, y, 5, corePaint);
    }

    // A dark archway hinting at what's beyond it.
    private void DrawGuardianDoor(SKCanvas canvas, float x, float y)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(140, 30, 30),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true
        };
        canvas.DrawRect(x - 10, y - 14, 20, 28, paint);
        using var fillPaint = new SKPaint
        {
            Color = new SKColor(60, 10, 10, 150),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(x - 8, y - 12, 16, 24, fillPaint);
    }

    // A subtle warning glyph — visible but easy to miss, consistent with "hazard" framing.
    private void DrawTrap(SKCanvas canvas, float x, float y)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(220, 160, 40, 140),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        var path = new SKPath();
        path.MoveTo(x, y - 7);
        path.LineTo(x + 7, y + 5);
        path.LineTo(x - 7, y + 5);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawStairs(SKCanvas canvas, float x, float y)
    {
        using var paint = new SKPaint
        {
            Color = StairsColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        
        // Draw concentric circles (spiral stairs)
        canvas.DrawCircle(x, y, 6, paint);
        canvas.DrawCircle(x, y, 4, paint);
        canvas.DrawCircle(x, y, 2, paint);
    }
    
    private void DrawChest(SKCanvas canvas, float x, float y, MazeFeature feature)
    {
        float openProgress = feature.IsOpened ? 1f : 0f;
        
        // Brown chest colors
        SKColor chestBrown = new SKColor(139, 90, 43);      // Saddle brown
        SKColor chestDark = new SKColor(101, 67, 33);       // Darker brown
        SKColor chestLock = new SKColor(218, 165, 32);      // Goldenrod
        
        using var chestPaint = new SKPaint
        {
            Color = chestBrown,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        
        using var chestStroke = new SKPaint
        {
            Color = chestDark,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        
        // Chest dimensions
        float chestWidth = 12;
        float chestHeight = 10;
        float lidHeight = 5;
        
        // Draw chest base (bottom part)
        SKRect chestBase = new SKRect(x - chestWidth/2, y - chestHeight/2 + lidHeight/2, 
                                       x + chestWidth/2, y + chestHeight/2);
        canvas.DrawRect(chestBase, chestPaint);
        canvas.DrawRect(chestBase, chestStroke);
        
        // The lid lifts and foreshortens without rotating, preserving the fixed orthographic view.
        float lidLift = openProgress * 5f;
        float visibleLidHeight = MathF.Max(1.5f, lidHeight * (1f - openProgress * 0.65f));
        SKRect chestLid = new SKRect(
            x - chestWidth / 2,
            y - chestHeight / 2 - visibleLidHeight / 2 - lidLift,
            x + chestWidth / 2,
            y - chestHeight / 2 + visibleLidHeight / 2 - lidLift);
        canvas.DrawRect(chestLid, chestPaint);
        canvas.DrawRect(chestLid, chestStroke);
        
        // Draw lock/clasp on front (fades as chest opens)
        if (openProgress < 0.5f)
        {
            using var lockPaint = new SKPaint
            {
                Color = chestLock.WithAlpha((byte)(255 * (1 - openProgress * 2))),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            
            canvas.DrawCircle(x, y - chestHeight/2 + lidHeight/2, 2, lockPaint);
        }
        
        // Loot glow rising out of the chest as the lid opens. Chests drop rolled gear
        // (see LootService), so the reveal is a generic golden glint rather than any
        // specific item shape.
        if (openProgress > 0.3f)
        {
            float glowAlpha = Math.Min((openProgress - 0.3f) / 0.7f, 1.0f);
            float glowX = x;
            float glowY = y - 2 - (openProgress * 8); // Rises up as it opens

            using var glowPaint = new SKPaint
            {
                Color = new SKColor(255, 215, 0, (byte)(90 * glowAlpha)),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(glowX, glowY, 5f, glowPaint);

            using var corePaint = new SKPaint
            {
                Color = new SKColor(255, 240, 160, (byte)(220 * glowAlpha)),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(glowX, glowY, 2.2f, corePaint);

            // Four-point sparkle
            using var sparklePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 220, (byte)(255 * glowAlpha)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            float r = 4.5f;
            canvas.DrawLine(glowX - r, glowY, glowX + r, glowY, sparklePaint);
            canvas.DrawLine(glowX, glowY - r, glowX, glowY + r, sparklePaint);
        }
    }
    
    private void DrawEnemies(SKCanvas canvas, GameState gameState, List<Enemy> enemies, FogView fog)
    {
        foreach (var enemy in enemies)
        {
            // Fog: an enemy only renders while the hero can actually see it (in sight range with
            // clear line of sight) — SimpleRPG's rule; no minimap-style omniscience.
            if (fog.Enabled)
            {
                float dx = enemy.X - gameState.Hero.X;
                float dy = enemy.Y - gameState.Hero.Y;
                float range = gameState.VisionRange;
                if (dx * dx + dy * dy > range * range) continue;
                if (!gameState.CheckLOS(gameState.Hero.X, gameState.Hero.Y, enemy.X, enemy.Y)) continue;
            }

            float px = enemy.X * CellSize + CellSize / 2f;
            float py = enemy.Y * CellSize + CellSize / 2f;
            
            // Size scales with radius (bosses have a larger radius, so they read bigger).
            float sz = 10f * (enemy.Radius / 0.35f);

            // Sprite (resolved race+class -> race -> class in Data/Sprites/sprites.json) replaces
            // the procedural class shape when one exists; unmapped enemies keep their old shape.
            var sprite = SpriteService.ForEnemy(enemy.Race, enemy.Class);
            if (sprite != null)
            {
                using var spritePaint = new SKPaint();
                if (!enemy.IsAlive)
                {
                    // Corpses read as darkened, matching the dimmed-color treatment shapes get.
                    spritePaint.ColorFilter = SKColorFilter.CreateBlendMode(
                        new SKColor(0, 0, 0, 140), SKBlendMode.SrcATop);
                }
                // Same scale basis as the hero: a normal-radius enemy fills a cell.
                float maxSize = CellSize * (enemy.Radius / 0.35f);
                SpriteService.Draw(canvas, sprite, px, py, maxSize, spritePaint);
            }
            else
            {
                // Color + shape derived from the enemy's character class.
                SKColor baseColor = ClassColor(enemy.Class);
                SKColor enemyColor = enemy.IsAlive ? baseColor
                    : new SKColor((byte)(baseColor.Red / 2), (byte)(baseColor.Green / 2), (byte)(baseColor.Blue / 2));

                using var paint = new SKPaint
                {
                    Color = enemyColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                switch (ClassShape(enemy.Class))
                {
                    case 0: // square (melee tank / generalist)
                        canvas.DrawRect(px - sz, py - sz, sz * 2, sz * 2, paint);
                        break;
                    case 1: // diamond (fast melee)
                        var diamond = new SKPath();
                        diamond.MoveTo(px, py - sz);
                        diamond.LineTo(px + sz, py);
                        diamond.LineTo(px, py + sz);
                        diamond.LineTo(px - sz, py);
                        diamond.Close();
                        canvas.DrawPath(diamond, paint);
                        break;
                    case 2: // triangle (ranged)
                        var tri = new SKPath();
                        tri.MoveTo(px, py - sz);
                        tri.LineTo(px + sz * 0.9f, py + sz * 0.8f);
                        tri.LineTo(px - sz * 0.9f, py + sz * 0.8f);
                        tri.Close();
                        canvas.DrawPath(tri, paint);
                        break;
                    default: // pentagon (caster / support)
                        var penta = new SKPath();
                        for (int i = 0; i < 5; i++)
                        {
                            float angle = (float)(i * 2 * Math.PI / 5 - Math.PI / 2);
                            float x = px + sz * MathF.Cos(angle);
                            float y = py + sz * MathF.Sin(angle);
                            if (i == 0) penta.MoveTo(x, y);
                            else penta.LineTo(x, y);
                        }
                        penta.Close();
                        canvas.DrawPath(penta, paint);
                        break;
                }
            }

            // Elite/Boss get a distinguishing halo ring (gold for Boss, silver for Elite) on top
            // of the shape, independent of which shape it is.
            if (enemy.IsAlive && (enemy.IsElite || enemy.IsBoss))
            {
                SKColor ringColor = enemy.IsBoss ? new SKColor(255, 215, 0) : new SKColor(220, 220, 230);
                using var ringPaint = new SKPaint
                {
                    Color = ringColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = enemy.IsBoss ? 2.5f : 1.5f,
                    IsAntialias = true
                };
                canvas.DrawCircle(px, py, sz + 4f, ringPaint);
            }

            // Draw health bar for living enemies
            if (enemy.IsAlive)
            {
                float healthBarWidth = 24f;
                float healthBarHeight = 3f;
                float healthBarX = px - healthBarWidth / 2f;
                float healthBarY = py - 18f; // Above the enemy
                
                // Background (red for missing health)
                using var bgPaint = new SKPaint
                {
                    Color = new SKColor(80, 20, 20),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawRect(healthBarX, healthBarY, healthBarWidth, healthBarHeight, bgPaint);
                
                // Foreground (green for current health)
                float healthPercent = (float)enemy.Hp / enemy.MaxHp;
                using var fgPaint = new SKPaint
                {
                    Color = new SKColor(100, 220, 100),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawRect(healthBarX, healthBarY, healthBarWidth * healthPercent, healthBarHeight, fgPaint);
                
                // Border
                using var borderPaint = new SKPaint
                {
                    Color = new SKColor(40, 40, 40),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1f,
                    IsAntialias = true
                };
                canvas.DrawRect(healthBarX, healthBarY, healthBarWidth, healthBarHeight, borderPaint);
            }
        }
    }
    
    /// <summary>
    /// (glow, core) base colors for a magic projectile's element. Returns the supplied defaults
    /// for <see cref="MagicElement.None"/> so non-elemental spells keep their original look. The
    /// caller re-applies its own per-layer alpha.
    /// </summary>
    private static (SKColor glow, SKColor core) MagicColors(MagicElement e, SKColor defGlow, SKColor defCore) => e switch
    {
        MagicElement.Mana => (new SKColor(150, 190, 255), new SKColor(210, 230, 255)),
        MagicElement.Arcane => (new SKColor(170, 100, 255), new SKColor(215, 170, 255)),
        MagicElement.Fire => (new SKColor(255, 110, 40), new SKColor(255, 200, 130)),
        MagicElement.Ice => (new SKColor(150, 220, 255), new SKColor(225, 248, 255)),
        MagicElement.Poison => (new SKColor(120, 255, 60), new SKColor(205, 255, 150)),
        MagicElement.Water => (new SKColor(60, 130, 255), new SKColor(150, 195, 255)),
        MagicElement.Lightning => (new SKColor(255, 230, 60), new SKColor(255, 255, 190)),
        MagicElement.Life => (new SKColor(89, 214, 111), new SKColor(216, 255, 210)),
        MagicElement.Light => (new SKColor(255, 243, 166), new SKColor(255, 255, 255)),
        MagicElement.Void => (new SKColor(59, 49, 90), new SKColor(0, 0, 0)),
        MagicElement.Holy => (new SKColor(255, 210, 90), new SKColor(255, 242, 185)),
        // Black cores (opaque, so they read on any background) with a colored glow halo — dark
        // gray for Death, purple for Shadow. The glow uses a Screen blend, so it needs a
        // non-black color to show; the core does not, so black stays black.
        MagicElement.Death => (new SKColor(120, 120, 120), new SKColor(0, 0, 0)),
        MagicElement.Shadow => (new SKColor(150, 90, 200), new SKColor(0, 0, 0)),
        MagicElement.Earth => (new SKColor(170, 120, 60), new SKColor(215, 185, 130)),
        MagicElement.Air => (new SKColor(225, 225, 220), new SKColor(248, 248, 245)),
        MagicElement.Sonic => (new SKColor(100, 200, 255), new SKColor(205, 238, 255)),
        _ => (defGlow, defCore)
    };

    private void DrawProjectiles(SKCanvas canvas, List<Projectile> projectiles)
    {
        foreach (var projectile in projectiles)
        {
            float px = projectile.CurrentX * CellSize + CellSize / 2f;
            float py = projectile.CurrentY * CellSize + CellSize / 2f;
            float startPx = projectile.StartX * CellSize + CellSize / 2f;
            float startPy = projectile.StartY * CellSize + CellSize / 2f;
            
            // Calculate fade based on lifetime
            float alpha = 1.0f - ((float)projectile.LifeTime / projectile.MaxLifeTime);
            alpha = Math.Clamp(alpha, 0.3f, 1.0f);
            byte alphaVal = (byte)(alpha * 255);

            // Sprite-based effect when the style is mapped (Data/Sprites/attackfx.json):
            // weapon swings play rotated on the attacker, arrows/darts fly as rotated sprites.
            // Anything unmapped (all magic, deliberately) falls through to procedural drawing.
            if (AttackFxService.TryGet(projectile.Visual, out var fx))
            {
                float fxT = Math.Clamp((float)projectile.LifeTime / projectile.MaxLifeTime, 0f, 1f);
                float heading = MathF.Atan2(projectile.TargetY - projectile.StartY,
                    projectile.TargetX - projectile.StartX);
                bool drawn = fx.Kind switch
                {
                    "swing" => AttackFxService.DrawSwing(canvas, fx, startPx, startPy, heading, fxT, CellSize),
                    "projectile" => AttackFxService.DrawProjectile(canvas, fx, px, py, heading, CellSize),
                    _ => false
                };
                if (drawn) continue;
            }

            switch (projectile.Type)
            {
                case AttackAnimation.Melee:
                    // Different visuals based on weapon type
                    if (projectile.Visual == VisualStyle.SwordArc)
                    {
                        // Sword - Draw a sweeping arc
                        float swordDx = px - startPx;
                        float swordDy = py - startPy;
                        float swordAngle = MathF.Atan2(swordDy, swordDx);
                        float progress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        
                        // Create arc path
                        using var arcPath = new SKPath();
                        float radius = CellSize * 0.7f;
                        float arcAngle = progress * 120f; // 120 degree sweep
                        float startAngle = (swordAngle * 180f / MathF.PI) - 60f;
                        
                        arcPath.AddArc(
                            new SKRect(
                                startPx - radius, startPy - radius,
                                startPx + radius, startPy + radius
                            ),
                            startAngle,
                            arcAngle
                        );
                        
                        using var swordPaint = new SKPaint
                        {
                            Color = new SKColor(192, 192, 192, alphaVal), // Silver
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 4,
                            IsAntialias = true,
                            StrokeCap = SKStrokeCap.Round
                        };
                        canvas.DrawPath(arcPath, swordPaint);
                        
                        // Add gleam effect at the tip
                        float tipX = startPx + radius * MathF.Cos((startAngle + arcAngle) * MathF.PI / 180f);
                        float tipY = startPy + radius * MathF.Sin((startAngle + arcAngle) * MathF.PI / 180f);
                        
                        using var gleamPaint = new SKPaint
                        {
                            Color = new SKColor(255, 255, 255, (byte)(alphaVal * 0.8f)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2),
                            BlendMode = SKBlendMode.Screen
                        };
                        canvas.DrawCircle(tipX, tipY, 3, gleamPaint);
                    }
                    else if (projectile.Visual == VisualStyle.HolyStrike)
                    {
                        // Holy Smite - Draw radiant hammer strike
                        using (var holyPaint = new SKPaint
                        {
                            Color = new SKColor(255, 215, 0, alphaVal), // Gold
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 5,
                            IsAntialias = true,
                            StrokeCap = SKStrokeCap.Round
                        })
                        {
                            canvas.DrawLine(startPx, startPy, px, py, holyPaint);
                        }
                        
                        // Radiant glow
                        using var glowPaint = new SKPaint
                        {
                            Color = new SKColor(255, 255, 200, (byte)(alphaVal * 0.5f)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4),
                            BlendMode = SKBlendMode.Screen
                        };
                        canvas.DrawCircle(px, py, 8, glowPaint);
                    }
                    else if (projectile.Visual == VisualStyle.ImpactBurst)
                    {
                        // Unarmed Strike - Draw impact burst and motion lines
                        float progress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        
                        // Impact burst at target
                        using var impactPaint = new SKPaint
                        {
                            Color = new SKColor(255, 200, 100, alphaVal), // Orange impact
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
                            BlendMode = SKBlendMode.Screen
                        };
                        
                        // Expanding impact circle
                        float impactSize = 8 + (progress * 6);
                        canvas.DrawCircle(px, py, impactSize, impactPaint);
                        
                        // Impact lines radiating outward (like manga/comic impact)
                        using var linePaint = new SKPaint
                        {
                            Color = new SKColor(255, 255, 255, alphaVal),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        };
                        
                        for (int i = 0; i < 6; i++)
                        {
                            float impactAngle = (i * 60f + progress * 30f) * MathF.PI / 180f;
                            float lineLength = 8 + progress * 8;
                            canvas.DrawLine(
                                px,
                                py,
                                px + MathF.Cos(impactAngle) * lineLength,
                                py + MathF.Sin(impactAngle) * lineLength,
                                linePaint
                            );
                        }
                        
                        // Motion blur trail from hero to target
                        using var motionPaint = new SKPaint
                        {
                            Color = new SKColor(220, 220, 220, (byte)(alphaVal * 0.4f)),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 4,
                            IsAntialias = true,
                            StrokeCap = SKStrokeCap.Round
                        };
                        canvas.DrawLine(startPx, startPy, px, py, motionPaint);
                    }
                    else
                    {
                        // Default dagger - Draw grey blade line shooting forward
                        using (var bladePaint = new SKPaint
                        {
                            Color = new SKColor(200, 200, 200, alphaVal),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawLine(startPx, startPy, px, py, bladePaint);
                            
                            // Small blade tip
                            using var tipPaint = new SKPaint
                            {
                                Color = new SKColor(220, 220, 220, alphaVal),
                                Style = SKPaintStyle.Fill,
                                IsAntialias = true
                            };
                            canvas.DrawCircle(px, py, 2, tipPaint);
                        }
                    }
                    break;
                    
                case AttackAnimation.Ranged:
                    float dx = px - startPx;
                    float dy = py - startPy;
                    float angle = MathF.Atan2(dy, dx);
                    
                    if (projectile.Visual == VisualStyle.Arrow)
                    {
                        // Stable flight heading (Start→Target), so the arrow points where it's going
                        // even at the very start of its travel.
                        float hdx = projectile.TargetX - projectile.StartX;
                        float hdy = projectile.TargetY - projectile.StartY;
                        float aAngle = (hdx * hdx + hdy * hdy) > 0.0001f ? MathF.Atan2(hdy, hdx) : angle;
                        float ca = MathF.Cos(aAngle), sa = MathF.Sin(aAngle);

                        // A short arrow at the current position (no growing trail). Tail is just
                        // behind the head along the heading.
                        const float shaftLen = 12f;
                        float tailX = px - ca * shaftLen;
                        float tailY = py - sa * shaftLen;

                        // Elemental arrows leave a sparse particle trail in the element's color;
                        // plain arrows leave (almost) nothing.
                        if (projectile.Element != MagicElement.None)
                        {
                            var (glow, _) = MagicColors(projectile.Element, new SKColor(200, 200, 200), new SKColor(255, 255, 255));
                            for (int i = 1; i <= 3; i++)
                            {
                                float back = shaftLen + i * 9f;
                                float sx = px - ca * back;
                                float sy = py - sa * back;
                                byte pa = (byte)(alphaVal * (0.5f - i * 0.13f));
                                using var particle = new SKPaint
                                {
                                    Color = glow.WithAlpha(pa),
                                    Style = SKPaintStyle.Fill,
                                    IsAntialias = true,
                                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.5f),
                                    BlendMode = SKBlendMode.Screen
                                };
                                canvas.DrawCircle(sx, sy, 2.2f - i * 0.4f, particle);
                            }
                        }

                        using (var shaftPaint = new SKPaint
                        {
                            Color = new SKColor(139, 69, 19, alphaVal), // Brown shaft
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawLine(tailX, tailY, px, py, shaftPaint);
                        }

                        // Arrowhead (metal) at the front
                        float headSize = 5;
                        using var headPath = new SKPath();
                        headPath.MoveTo(px, py);
                        headPath.LineTo(px - headSize * MathF.Cos(aAngle - 0.4f), py - headSize * MathF.Sin(aAngle - 0.4f));
                        headPath.LineTo(px - headSize * MathF.Cos(aAngle + 0.4f), py - headSize * MathF.Sin(aAngle + 0.4f));
                        headPath.Close();

                        using var headFill = new SKPaint
                        {
                            Color = new SKColor(160, 160, 160, alphaVal), // Silver tip
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        };
                        canvas.DrawPath(headPath, headFill);

                        // Fletching (feathers) at the tail
                        using var fletchPaint = new SKPaint
                        {
                            Color = new SKColor(200, 0, 0, alphaVal), // Red feathers
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        };
                        canvas.DrawCircle(tailX, tailY, 2, fletchPaint);
                    }
                    else if (projectile.Visual == VisualStyle.PoisonDart)
                    {
                        // Poison Dart - Draw with green trail
                        using (var dartPaint = new SKPaint
                        {
                            Color = new SKColor(100, 50, 30, alphaVal), // Dark brown
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawLine(startPx, startPy, px, py, dartPaint);
                        }
                        
                        // Poison drip effect
                        using var poisonPaint = new SKPaint
                        {
                            Color = new SKColor(50, 200, 50, (byte)(alphaVal * 0.7f)), // Green poison
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2),
                            BlendMode = SKBlendMode.Screen
                        };
                        canvas.DrawCircle(px, py, 3, poisonPaint);
                        
                        // Trailing poison droplets
                        for (int i = 1; i <= 3; i++)
                        {
                            float t = i * 0.25f;
                            float dropX = startPx + dx * t;
                            float dropY = startPy + dy * t;
                            canvas.DrawCircle(dropX, dropY, 1.5f, poisonPaint);
                        }
                    }
                    else
                    {
                        // Generic projectile
                        using (var projectilePaint = new SKPaint
                        {
                            Color = new SKColor(139, 69, 19, alphaVal),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawLine(startPx, startPy, px, py, projectilePaint);
                        }
                    }
                    break;
                    
                case AttackAnimation.Magic:
                    if (projectile.Visual == VisualStyle.MagicMissile)
                    {
                        // Glowing orb with sparkles — purple by default, tinted by element.
                        var (mmGlow, mmCore) = MagicColors(projectile.Element, new SKColor(138, 43, 226), new SKColor(255, 105, 255));
                        using (var magicGlow = new SKPaint
                        {
                            Color = mmGlow.WithAlpha((byte)(alphaVal * 0.5f)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4),
                            BlendMode = SKBlendMode.Screen
                        })
                        {
                            canvas.DrawCircle(px, py, 10, magicGlow);
                        }

                        using (var magicCore = new SKPaint
                        {
                            Color = mmCore.WithAlpha(alphaVal),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawCircle(px, py, 5, magicCore);
                        }

                        // Sparkle trail
                        float trailDx = px - startPx;
                        float trailDy = py - startPy;
                        for (int i = 1; i <= 4; i++)
                        {
                            float t = i * 0.2f;
                            float sparkleX = startPx + trailDx * t;
                            float sparkleY = startPy + trailDy * t;
                            using var sparklePaint = new SKPaint
                            {
                                Color = mmGlow.WithAlpha((byte)(alphaVal * 0.4f)),
                                Style = SKPaintStyle.Fill,
                                IsAntialias = true
                            };
                            canvas.DrawCircle(sparkleX, sparkleY, 2, sparklePaint);
                        }
                    }
                    else if (projectile.Visual == VisualStyle.MagicComet)
                    {
                        // Magic Dart - cyan/blue comet with additive glow and tapered trail
                        float trailDx = px - startPx;
                        float trailDy = py - startPy;
                        float len = MathF.Sqrt(trailDx * trailDx + trailDy * trailDy);
                        float nx = len > 0 ? trailDx / len : 0f;
                        float ny = len > 0 ? trailDy / len : 0f;

                        // Comet with tapered trail — cyan by default, tinted by element.
                        var (cometGlow, cometCore) = MagicColors(projectile.Element, new SKColor(100, 255, 255), new SKColor(180, 255, 255));
                        using (var glow = new SKPaint
                        {
                            Color = cometGlow.WithAlpha((byte)(alphaVal * 160)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4),
                            BlendMode = SKBlendMode.Screen
                        })
                        {
                            canvas.DrawCircle(px, py, 8, glow);
                        }
                        using (var core = new SKPaint
                        {
                            Color = cometCore.WithAlpha(alphaVal),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawCircle(px, py, 3.5f, core);
                        }
                        // Tapered trail dots behind the projectile
                        for (int i = 1; i <= 4; i++)
                        {
                            float t = i * 0.18f;
                            float tx = px - nx * t * 24f;
                            float ty = py - ny * t * 24f;
                            float size = 3.2f - i * 0.5f;
                            using var trail = new SKPaint
                            {
                                Color = cometGlow.WithAlpha((byte)(alphaVal * (200 - i * 30) / 255f * 255)),
                                Style = SKPaintStyle.Fill,
                                IsAntialias = true,
                                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2),
                                BlendMode = SKBlendMode.Screen
                            };
                            canvas.DrawCircle(tx, ty, size, trail);
                        }
                    }
                    else if (projectile.Visual == VisualStyle.ArcaneRing)
                    {
                        // Arcane Blast - expanding teal ring (shockwave)
                        float progress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        float radius = 6 + progress * 18f;
                        var (ringColor, _) = MagicColors(projectile.Element, new SKColor(100, 220, 220), new SKColor(100, 220, 220));
                        using var ring = new SKPaint
                        {
                            Color = ringColor.WithAlpha((byte)(alphaVal * 0.8f)),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2),
                            BlendMode = SKBlendMode.Screen
                        };
                        canvas.DrawCircle(px, py, radius, ring);
                    }
                    else if (projectile.Visual == VisualStyle.Sonic)
                    {
                        // Sonic Blast - Sound wave rings (tinted by element if any)
                        var (waveColor, noteColor) = MagicColors(projectile.Element, new SKColor(100, 200, 255), new SKColor(150, 220, 255));
                        float waveProgress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        for (int i = 0; i < 3; i++)
                        {
                            float waveRadius = (waveProgress + i * 0.3f) * 20f;
                            using var wavePaint = new SKPaint
                            {
                                Color = waveColor.WithAlpha((byte)(alphaVal * 0.5f)),
                                Style = SKPaintStyle.Stroke,
                                StrokeWidth = 2,
                                IsAntialias = true,
                                BlendMode = SKBlendMode.Screen
                            };
                            canvas.DrawCircle(px, py, waveRadius, wavePaint);
                        }

                        // Central note symbol
                        using var notePaint = new SKPaint
                        {
                            Color = noteColor.WithAlpha(alphaVal),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            BlendMode = SKBlendMode.Screen
                        };
                        canvas.DrawCircle(px, py, 4, notePaint);
                    }
                    else
                    {
                        // Generic magic effect
                        using (var magicGlow = new SKPaint
                        {
                            Color = new SKColor(138, 43, 226, (byte)(alphaVal * 0.5f)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3),
                            BlendMode = SKBlendMode.Screen
                        })
                        {
                            canvas.DrawCircle(px, py, 8, magicGlow);
                        }
                    }
                    break;
                    
                case AttackAnimation.Heavy:
                    // Draw heavy weapon arc (thick line)
                    using (var heavyPaint = new SKPaint
                    {
                        Color = new SKColor(192, 192, 192, alphaVal), // Silver
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 6,
                        IsAntialias = true,
                        StrokeCap = SKStrokeCap.Round
                    })
                    {
                        canvas.DrawLine(startPx, startPy, px, py, heavyPaint);
                    }
                    break;
                    
                case AttackAnimation.Quick:
                    if (projectile.Visual == VisualStyle.Backstab)
                    {
                        // Backstab - Multiple quick dagger slashes in a cross pattern
                        float quickDx = px - startPx;
                        float quickDy = py - startPy;
                        
                        using var backstabPaint = new SKPaint
                        {
                            Color = new SKColor(200, 50, 50, alphaVal), // Dark red
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3,
                            IsAntialias = true
                        };
                        
                        // Draw X pattern
                        float offset = 5;
                        canvas.DrawLine(px - offset, py - offset, px + offset, py + offset, backstabPaint);
                        canvas.DrawLine(px - offset, py + offset, px + offset, py - offset, backstabPaint);
                        
                        // Blood effect
                        using var bloodPaint = new SKPaint
                        {
                            Color = new SKColor(150, 0, 0, (byte)(alphaVal * 0.6f)),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        };
                        canvas.DrawCircle(px, py, 3, bloodPaint);
                    }
                    else if (projectile.Visual == VisualStyle.Parry)
                    {
                        // Parry - Defensive arc flash
                        using var parryPaint = new SKPaint
                        {
                            Color = new SKColor(150, 200, 255, alphaVal), // Blue shield color
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
                        };
                        
                        // Draw arc in front of hero
                        float parrydx = px - startPx;
                        float parrydy = py - startPy;
                        float parryAngle = MathF.Atan2(parrydy, parrydx);
                        float radius = 15;
                        
                        using var arcPath = new SKPath();
                        arcPath.AddArc(
                            new SKRect(startPx - radius, startPy - radius, startPx + radius, startPy + radius),
                            (parryAngle * 180f / MathF.PI) - 45f,
                            90f
                        );
                        canvas.DrawPath(arcPath, parryPaint);
                    }
                    else
                    {
                        // Quick Strike - Triple rapid slashes
                        using var quickPaint = new SKPaint
                        {
                            Color = new SKColor(255, 255, 100, alphaVal), // Yellow flash
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
                        };
                        
                        // Draw three parallel lines for rapid strikes
                        float quickDx = px - startPx;
                        float quickDy = py - startPy;
                        float perpX = -quickDy * 0.15f;
                        float perpY = quickDx * 0.15f;
                        
                        for (int i = -1; i <= 1; i++)
                        {
                            canvas.DrawLine(
                                startPx + perpX * i, startPy + perpY * i,
                                px + perpX * i, py + perpY * i,
                                quickPaint
                            );
                        }
                    }
                    break;
            }
        }
    }
    
    private void DrawHero(SKCanvas canvas, Hero hero)
    {
        float px = (hero.X + hero.AnimationOffsetX) * CellSize + CellSize / 2f;
        float py = (hero.Y + hero.AnimationOffsetY) * CellSize + CellSize / 2f;
        float heroRadius = CellSize / 6f;

        // Sprite (mapped by class in Data/Sprites/sprites.json) replaces the procedural
        // race/class circles when one exists; unmapped classes fall through to the circles below.
        if (SpriteService.ForHero(hero.Class) is { } sprite)
        {
            SpriteService.Draw(canvas, sprite, px, py, CellSize);
            return;
        }

        // Parse race color for inner circle
        SKColor raceColor = HeroColor; // default
        try
        {
            if (!string.IsNullOrEmpty(hero.RaceColor))
            {
                var color = System.Drawing.ColorTranslator.FromHtml(hero.RaceColor);
                raceColor = new SKColor(color.R, color.G, color.B);
            }
        }
        catch { }
        
        // Draw inner circle (race color)
        using var racePaint = new SKPaint
        {
            Color = raceColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(px, py, heroRadius, racePaint);
        
        // Parse class color for outer ring
        SKColor classColor = SKColors.White; // default
        try
        {
            if (!string.IsNullOrEmpty(hero.ClassColor))
            {
                var color = System.Drawing.ColorTranslator.FromHtml(hero.ClassColor);
                classColor = new SKColor(color.R, color.G, color.B);
            }
        }
        catch { }
        
        // Draw outer ring (class color)
        using var outlinePaint = new SKPaint
        {
            Color = classColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true
        };
        canvas.DrawCircle(px, py, heroRadius, outlinePaint);
    }
    
    /// <summary>SimpleRPG-style stat bar: dark tinted back, vertical-gradient fill, thin gray
    /// border, centered shadowed text in the game font.</summary>
    private static void DrawStatBar(SKCanvas canvas, float x, float y, float width, float height,
        float percent, SKColor back, SKColor fillTop, SKColor fillBottom, string text)
    {
        percent = Math.Clamp(percent, 0f, 1f);

        using var backPaint = new SKPaint { Color = back, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x, y, width, height, backPaint);

        if (percent > 0f)
        {
            using var fillPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y), new SKPoint(x, y + height),
                    new[] { fillTop, fillBottom }, null, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(x, y, width * percent, height, fillPaint);
        }

        using var borderPaint = new SKPaint { Color = new SKColor(0x55, 0x55, 0x55), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(x, y, width, height, borderPaint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 11,
            IsAntialias = true,
            Typeface = GameTypeface,
            TextAlign = SKTextAlign.Center
        };
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 11,
            IsAntialias = true,
            Typeface = GameTypeface,
            TextAlign = SKTextAlign.Center
        };
        float tx = x + width / 2f;
        float ty = y + height / 2f + 4f;
        canvas.DrawText(text, tx + 1, ty + 1, shadowPaint);
        canvas.DrawText(text, tx, ty, textPaint);
    }

    private static void DrawHudLine(SKCanvas canvas, string text, float x, float y, SKColor color, float size = 12)
    {
        using var shadowPaint = new SKPaint { Color = SKColors.Black, TextSize = size, IsAntialias = true, Typeface = GameTypeface };
        using var textPaint = new SKPaint { Color = color, TextSize = size, IsAntialias = true, Typeface = GameTypeface };
        canvas.DrawText(text, x + 1, y + 1, shadowPaint);
        canvas.DrawText(text, x, y, textPaint);
    }

    private static void DrawHUD(SKCanvas canvas, GameState gameState, int viewportWidth, int viewportHeight)
    {
        var hero = gameState.Hero;
        float barWidth = 170;
        float barHeight = 14;
        float barX = 10;
        float y = 10;
        float gap = 4;

        // Identity line (SimpleRPG's "Race Class D:x Lv:y" info style, in gold)
        DrawHudLine(canvas, $"{hero.Race} {hero.Class}  Lv:{hero.Level}", barX, y + 8, new SKColor(0xFF, 0xCC, 0x00));
        y += 16;

        DrawStatBar(canvas, barX, y, barWidth, barHeight,
            (float)hero.CurrentHp / hero.MaxHp,
            new SKColor(0x33, 0x00, 0x00), new SKColor(0xCC, 0x44, 0x44), new SKColor(0x88, 0x00, 0x00),
            $"HP {hero.CurrentHp}/{hero.MaxHp}");
        y += barHeight + gap;

        DrawStatBar(canvas, barX, y, barWidth, barHeight,
            (float)hero.CurrentStamina / hero.MaxStamina,
            new SKColor(0x00, 0x33, 0x00), new SKColor(0x44, 0xCC, 0x44), new SKColor(0x00, 0x77, 0x00),
            $"SP {hero.CurrentStamina}/{hero.MaxStamina}");
        y += barHeight + gap;

        DrawStatBar(canvas, barX, y, barWidth, barHeight,
            (float)hero.CurrentMana / hero.MaxMana,
            new SKColor(0x00, 0x00, 0x66), new SKColor(0x44, 0x88, 0xFF), new SKColor(0x22, 0x22, 0x88),
            $"MP {hero.CurrentMana}/{hero.MaxMana}");
        y += barHeight + gap;

        DrawStatBar(canvas, barX, y, barWidth, barHeight,
            (float)hero.CurrentFaith / hero.MaxFaith,
            new SKColor(0x33, 0x33, 0x00), new SKColor(0xFF, 0xCC, 0x00), new SKColor(0xAA, 0x88, 0x00),
            $"FP {hero.CurrentFaith}/{hero.MaxFaith}");
        y += barHeight + gap;

        float xpPercent = hero.ExperienceToNext > 0
            ? (float)hero.Experience / hero.ExperienceToNext
            : 0;
        DrawStatBar(canvas, barX, y, barWidth, barHeight,
            xpPercent,
            new SKColor(0x11, 0x11, 0x33), new SKColor(0x66, 0xAA, 0xFF), new SKColor(0x22, 0x33, 0x88),
            $"XP {hero.Experience}/{hero.ExperienceToNext}");
        y += barHeight + gap + 4;

        // Current attack info
        var currentAttack = hero.CurrentAttack;
        if (currentAttack != null)
        {
            string attackInfo = $"Attack: {currentAttack.Name}";
            if (currentAttack.IsHeavyAttack)
            {
                if (currentAttack.StaminaCost > 0) attackInfo += $" ({currentAttack.StaminaCost} SP)";
                else if (currentAttack.ManaCost > 0) attackInfo += $" ({currentAttack.ManaCost} MP)";
                else if (currentAttack.FaithCost > 0) attackInfo += $" ({currentAttack.FaithCost} FP)";
            }
            DrawHudLine(canvas, attackInfo, barX, y + 8, new SKColor(0xAA, 0xAA, 0xAA));
        }

        // Floor / location info (bottom-left, gold like SimpleRPG's info spans)
        string location = gameState.IsInOverworld ? "Town"
            : gameState.IsInSafeRoom ? $"Safe Room {gameState.CurrentFloor}.5"
            : gameState.CurrentMaze.Dungeon is { } dungeon
                ? $"{dungeon.Theme} - Floor {gameState.CurrentFloor}"
                : $"Floor {gameState.CurrentFloor}";
        DrawHudLine(canvas, $"{location}  Atk:{hero.Attack} Def:{hero.Defense} Gold:{hero.Gold}",
            10, viewportHeight - 10, new SKColor(0xFF, 0xCC, 0x00));

        // Control-mode indicator (bottom-right, clear of the top-right Stats button): AUTO in
        // green, MANUAL in gold, with a hint line above it.
        bool manual = gameState.ControlMode == ControlMode.Manual;
        var modeColor = manual ? new SKColor(0xFF, 0xCC, 0x00) : new SKColor(0x66, 0xDD, 0x66);
        DrawHudLineRight(canvas,
            manual ? "WASD move  ·  M: auto" : "M / WASD: take control",
            viewportWidth - 12, viewportHeight - 28, new SKColor(0x88, 0x88, 0x88), 11);
        DrawHudLineRight(canvas, manual ? "MANUAL" : "AUTO", viewportWidth - 12, viewportHeight - 12, modeColor, 14);

        // Dash (Space) readiness — a small bar above the mode line. Green when ready, gray/filling
        // while on cooldown. Only shown in Manual mode (dash is a player action).
        if (manual)
        {
            float ready = gameState.DashReadyFraction;
            float bw = 90f, bh = 6f;
            float bx = viewportWidth - 12 - bw, by = viewportHeight - 46;
            using var back = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), Style = SKPaintStyle.Fill };
            canvas.DrawRect(bx, by, bw, bh, back);
            var fill = ready >= 1f ? new SKColor(0x66, 0xDD, 0x66) : new SKColor(0x88, 0x88, 0x88);
            using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill };
            canvas.DrawRect(bx, by, bw * ready, bh, fillPaint);
            using var border = new SKPaint { Color = new SKColor(0x55, 0x55, 0x55), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRect(bx, by, bw, bh, border);
            DrawHudLineRight(canvas, ready >= 1f ? "DODGE [Space] ready" : "DODGE …",
                viewportWidth - 12, by - 3, new SKColor(0x88, 0x88, 0x88), 10);
        }
    }

    private static void DrawHudLineRight(SKCanvas canvas, string text, float rightX, float y, SKColor color, float size = 12)
    {
        using var shadow = new SKPaint { Color = SKColors.Black, TextSize = size, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Right };
        using var paint = new SKPaint { Color = color, TextSize = size, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Right };
        canvas.DrawText(text, rightX + 1, y + 1, shadow);
        canvas.DrawText(text, rightX, y, paint);
    }
}

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
    /// coordinates, using the last rendered camera position.</summary>
    public (float x, float y) ScreenToWorld(double screenX, double screenY) =>
        (((float)screenX - _lastOffsetX) / CellSize, ((float)screenY - _lastOffsetY) / CellSize);

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
    
    public void Render(SKCanvas canvas, GameState gameState, int viewportWidth, int viewportHeight)
    {
        if (gameState.CurrentMaze == null || gameState.Hero == null)
            return;
        
        canvas.Clear(BackgroundColor);

        // Smooth camera lerp to follow hero
        float targetCameraX = gameState.Hero.X * CellSize;
        float targetCameraY = gameState.Hero.Y * CellSize;

        _cameraX += (targetCameraX - _cameraX) * CameraLerpSpeed;
        _cameraY += (targetCameraY - _cameraY) * CameraLerpSpeed;

        // Clamp the CENTERING POINT (not the lerp state itself) to the maze bounds, so the
        // viewport never scrolls past the edge into empty void. Clamping the lerp state instead
        // would make the camera "stick" at the edge and jump when the hero moves back inward.
        float mazePxW = gameState.CurrentMaze.Width * CellSize;
        float mazePxH = gameState.CurrentMaze.Height * CellSize;
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
        var fog = ComputeFogView(gameState);

        // Draw maze
        DrawMaze(canvas, gameState.CurrentMaze, fog);

        // Draw features (chests, stairs)
        DrawFeatures(canvas, gameState.CurrentMaze, fog);

        // Draw enemies (only the ones the hero can currently see, under fog)
        DrawEnemies(canvas, gameState, fog);
        
    // Draw projectiles (between enemies and hero)
    DrawProjectiles(canvas, gameState);
        
        // Draw on-hit flashes above projectiles, below hero
        DrawHitEffects(canvas, gameState.HitEffects);
        
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

    // Debug overlays (hitboxes, LOS) if enabled
    DrawDebugOverlay(canvas, gameState);
        
        canvas.Restore();

        // Low-health vignette (screen space, drawn before the HUD text/bars)
        DrawLowHealthVignette(canvas, gameState.Hero, viewportWidth, viewportHeight);

        // Draw HUD overlay
        DrawHUD(canvas, gameState, viewportWidth, viewportHeight);

        // Draw the player-facing message log (bottom-left, above the floor info line)
        DrawMessageLog(canvas, gameState, viewportHeight);

        // Draw the attack hotbar (bottom-center)
        DrawHotbar(canvas, gameState, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// The attack hotbar (bottom-center): one slot per equipped/usable attack (Hero.Attacks),
    /// numbered 1..N, with the current attack highlighted in gold. Number keys select; the
    /// selected attack is what click-to-fire (and Auto combat) uses.
    /// </summary>
    private static void DrawHotbar(SKCanvas canvas, GameState gameState, int viewportWidth, int viewportHeight)
    {
        var attacks = gameState.Hero.Attacks;
        if (attacks.Count == 0) return;

        const float slotW = 108f;
        const float slotH = 30f;
        const float gap = 6f;
        int n = Math.Min(attacks.Count, 9);
        float totalW = n * slotW + (n - 1) * gap;
        float startX = (viewportWidth - totalW) / 2f;
        float y = viewportHeight - slotH - 8f;

        for (int i = 0; i < n; i++)
        {
            var attack = attacks[i];
            bool selected = gameState.Hero.CurrentAttack == attack;
            float x = startX + i * (slotW + gap);

            using var bg = new SKPaint { Color = new SKColor(0x1A, 0x1A, 0x1A, 0xE0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(x, y, slotW, slotH, 4, 4, bg);

            using var border = new SKPaint
            {
                Color = selected ? new SKColor(0xFF, 0xCC, 0x00) : new SKColor(0x55, 0x55, 0x55),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = selected ? 2f : 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(x, y, slotW, slotH, 4, 4, border);

            // Slot number (gold)
            using var numPaint = new SKPaint { Color = new SKColor(0xFF, 0xCC, 0x00), TextSize = 12, IsAntialias = true, Typeface = GameTypeface };
            canvas.DrawText($"{i + 1}", x + 5, y + 20, numPaint);

            // Attack name (white if selected, gray otherwise), truncated to fit
            var nameColor = selected ? SKColors.White : new SKColor(0xAA, 0xAA, 0xAA);
            using var namePaint = new SKPaint { Color = nameColor, TextSize = 11, IsAntialias = true, Typeface = GameTypeface };
            string name = attack.Name.Length > 13 ? attack.Name.Substring(0, 12) + "…" : attack.Name;
            canvas.DrawText(name, x + 18, y + 19, namePaint);

            // Heavy-attack resource cost (small, bottom-right of the slot)
            if (attack.IsHeavyAttack)
            {
                int cost = attack.StaminaCost > 0 ? attack.StaminaCost : attack.ManaCost > 0 ? attack.ManaCost : attack.FaithCost;
                string costKind = attack.StaminaCost > 0 ? "SP" : attack.ManaCost > 0 ? "MP" : "FP";
                using var costPaint = new SKPaint { Color = new SKColor(0x88, 0x88, 0x88), TextSize = 9, IsAntialias = true, Typeface = GameTypeface, TextAlign = SKTextAlign.Right };
                canvas.DrawText($"{cost}{costKind}", x + slotW - 4, y + slotH - 4, costPaint);
            }
        }
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
    private static void DrawMessageLog(SKCanvas canvas, GameState gameState, int viewportHeight)
    {
        var messages = gameState.Messages.Messages;
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
    
    private void DrawDebugOverlay(SKCanvas canvas, GameState gameState)
    {
        // Early out if nothing is enabled
        if (!gameState.DebugDrawHitboxes && !gameState.DebugDrawLOS) return;

        // Hitboxes: hero, enemies, projectiles
        if (gameState.DebugDrawHitboxes)
        {
            // Hero
            DrawDebugCircle(canvas, gameState.Hero.X, gameState.Hero.Y, gameState.Hero.Radius, new SKColor(0, 200, 255, 160));
            // Enemies
            foreach (var e in gameState.Enemies)
            {
                var col = e.IsAlive ? new SKColor(255, 120, 120, 160) : new SKColor(120, 60, 60, 100);
                DrawDebugCircle(canvas, e.X, e.Y, e.Radius, col);
            }
            // Projectiles
            foreach (var p in gameState.Projectiles)
            {
                var col = p.Team == ProjectileTeam.Hero ? new SKColor(120, 220, 255, 140) : new SKColor(255, 180, 120, 140);
                DrawDebugCircle(canvas, p.CurrentX, p.CurrentY, p.Radius, col, dashed: true);
            }
        }

        // LOS rays: hero-to-enemy lines colored by blockage
        if (gameState.DebugDrawLOS)
        {
            foreach (var e in gameState.Enemies)
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
    private FogView ComputeFogView(GameState gameState)
    {
        var maze = gameState.CurrentMaze;
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

    private static SKColor DungeonFloorColor(Maze maze, int x, int y)
    {
        var layout = maze.Dungeon;
        if (layout == null) return FloorColor;
        if (layout.Tiles[x, y] is DungeonTileType.CorridorFloor or DungeonTileType.Doorway)
            return CorridorFloorColor;

        return layout.RoomAt(x, y)?.Role switch
        {
            DungeonRoomRole.Entrance => EntranceRoomFloorColor,
            DungeonRoomRole.Treasure => TreasureRoomFloorColor,
            DungeonRoomRole.Hazard => HazardRoomFloorColor,
            DungeonRoomRole.Exit => ExitRoomFloorColor,
            _ => StandardRoomFloorColor
        };
    }

    private void DrawMaze(SKCanvas canvas, Maze maze, FogView fog)
    {
        using var floorPaint = new SKPaint { Color = FloorColor, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var floorDotPaint = new SKPaint { Color = FloorDotColor, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallPaint = new SKPaint { Color = WallColor, Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallDetailPaint = new SKPaint { Color = WallDetailColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };

        using var floorPaintDim = new SKPaint { Color = FloorColor.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var floorDotPaintDim = new SKPaint { Color = FloorDotColor.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallPaintDim = new SKPaint { Color = WallColor.WithAlpha(DimAlpha), Style = SKPaintStyle.Fill, IsAntialias = false };
        using var wallDetailPaintDim = new SKPaint { Color = WallDetailColor.WithAlpha(DimAlpha), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = false };

        float dotSize = CellSize * 0.2f;
        float dotOffset = CellSize * 0.4f;

        for (int x = 0; x < maze.Width; x++)
        {
            for (int y = 0; y < maze.Height; y++)
            {
                float px = x * CellSize;
                float py = y * CellSize;

                if (maze.Walls[x, y])
                {
                    var (visible, seen) = WallLight(fog, maze, x, y);
                    if (!visible && !seen) continue; // never seen: pure black void

                    canvas.DrawRect(px, py, CellSize, CellSize, visible ? wallPaint : wallPaintDim);
                    canvas.DrawRect(px + 2, py + 2, CellSize - 4, CellSize - 4, visible ? wallDetailPaint : wallDetailPaintDim);
                }
                else
                {
                    if (!fog.FloorSeen(x, y)) continue; // never seen: pure black void
                    bool visible = fog.FloorVisible(x, y);

                    var tileFloorColor = DungeonFloorColor(maze, x, y);
                    floorPaint.Color = tileFloorColor;
                    floorPaintDim.Color = tileFloorColor.WithAlpha(DimAlpha);

                    canvas.DrawRect(px, py, CellSize, CellSize, visible ? floorPaint : floorPaintDim);
                    bool isRoomFloor = maze.Dungeon == null ||
                        maze.Dungeon.Tiles[x, y] == DungeonTileType.RoomFloor;
                    if (isRoomFloor)
                    {
                        canvas.DrawRect(px + dotOffset, py + dotOffset, dotSize, dotSize,
                            visible ? floorDotPaint : floorDotPaintDim);
                    }
                }
            }
        }
    }
    
    private void DrawFeatures(SKCanvas canvas, Maze maze, FogView fog)
    {
        foreach (var feature in maze.Features)
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
            }

            if (dimmed || unperceived) canvas.RestoreToCount(layer);
        }
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
        // Draw light glow if chest is opening
        if (feature.IsOpening && feature.LightRadius > 0)
        {
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            // Draw expanding golden light
            var colors = new SKColor[]
            {
                new SKColor(255, 215, 0, 180), // Gold center
                new SKColor(255, 215, 0, 100), // Mid
                new SKColor(255, 215, 0, 0)    // Fade out
            };
            var positions = new float[] { 0, 0.5f, 1.0f };
            
            glowPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(x, y),
                feature.LightRadius * CellSize,
                colors,
                positions,
                SKShaderTileMode.Clamp
            );
            
            canvas.DrawCircle(x, y, feature.LightRadius * CellSize, glowPaint);
        }
        
        // Opening progress (0 = closed, 1 = fully open), computed by GameState from the tick rate
        float openProgress = feature.OpenProgress;
        
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
        
        // Draw chest lid (top part) - rotates when opening
        canvas.Save();
        
        // Rotate lid based on opening progress (0 to -90 degrees)
        float lidAngle = -90f * openProgress;
        canvas.RotateDegrees(lidAngle, x - chestWidth/2, y - chestHeight/2 + lidHeight/2);
        
        SKRect chestLid = new SKRect(x - chestWidth/2, y - chestHeight/2 - lidHeight/2,
                                      x + chestWidth/2, y - chestHeight/2 + lidHeight/2);
        canvas.DrawRect(chestLid, chestPaint);
        canvas.DrawRect(chestLid, chestStroke);
        
        canvas.Restore();
        
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
    
    private void DrawEnemies(SKCanvas canvas, GameState gameState, FogView fog)
    {
        foreach (var enemy in gameState.Enemies)
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
                SpriteService.Draw(canvas, sprite, px, py,
                    SpriteService.CrispSize(sprite, maxSize), spritePaint);
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

    private void DrawProjectiles(SKCanvas canvas, GameState gameState)
    {
        var projectiles = gameState.Projectiles;
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
            SpriteService.Draw(canvas, sprite, px, py, SpriteService.CrispSize(sprite, CellSize));
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

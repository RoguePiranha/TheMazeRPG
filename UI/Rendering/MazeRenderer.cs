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


    // Color palette
    private static readonly SKColor BackgroundColor = new(26, 26, 26);
    private static readonly SKColor WallColor = new(128, 128, 128);
    private static readonly SKColor ExploredPathColor = new(64, 64, 64);
    private static readonly SKColor UnexploredColor = new(40, 40, 40);
    private static readonly SKColor HeroColor = new(100, 180, 255);
    private static readonly SKColor EnemyColor = new(255, 80, 80);
    private static readonly SKColor ChestColor = new(255, 215, 0);
    private static readonly SKColor StairsColor = new(150, 255, 150);

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

        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        
        // Draw maze
        DrawMaze(canvas, gameState.CurrentMaze);
        
        // Draw features (chests, stairs)
        DrawFeatures(canvas, gameState.CurrentMaze);
        
        // Draw enemies
        DrawEnemies(canvas, gameState.Enemies);
        
    // Draw projectiles (between enemies and hero)
    DrawProjectiles(canvas, gameState);
        
        // Draw on-hit flashes above projectiles, below hero
        DrawHitEffects(canvas, gameState.HitEffects);
        
    // Draw hero (always on top)
        DrawHero(canvas, gameState.Hero);
        
    // Debug overlays (hitboxes, LOS) if enabled
    DrawDebugOverlay(canvas, gameState);
        
        canvas.Restore();

        // Low-health vignette (screen space, drawn before the HUD text/bars)
        DrawLowHealthVignette(canvas, gameState.Hero, viewportWidth, viewportHeight);

        // Draw HUD overlay
        DrawHUD(canvas, gameState, viewportWidth, viewportHeight);
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
    
    private void DrawMaze(SKCanvas canvas, Maze maze)
    {
        using var wallPaint = new SKPaint
        {
            Color = WallColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };
        
        using var pathPaint = new SKPaint
        {
            Color = ExploredPathColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };
        
        using var unexploredPaint = new SKPaint
        {
            Color = UnexploredColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };
        
        for (int x = 0; x < maze.Width; x++)
        {
            for (int y = 0; y < maze.Height; y++)
            {
                float px = x * CellSize;
                float py = y * CellSize;
                
                if (maze.Walls[x, y])
                {
                    // Draw wall
                    canvas.DrawRect(px, py, CellSize, CellSize, wallPaint);
                }
                else if (maze.Explored[x, y])
                {
                    // Draw explored path
                    canvas.DrawRect(px, py, CellSize, CellSize, pathPaint);
                }
                else
                {
                    // Draw unexplored (darker)
                    canvas.DrawRect(px, py, CellSize, CellSize, unexploredPaint);
                }
            }
        }
    }
    
    private void DrawFeatures(SKCanvas canvas, Maze maze)
    {
        foreach (var feature in maze.Features)
        {
            if (feature.IsUsed) continue;
            
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
    
    private void DrawEnemies(SKCanvas canvas, List<Enemy> enemies)
    {
        foreach (var enemy in enemies)
        {
            float px = enemy.X * CellSize + CellSize / 2f;
            float py = enemy.Y * CellSize + CellSize / 2f;
            
            // Color + shape derived from the enemy's character class; size scales with radius
            // (bosses have a larger radius, so they read bigger).
            SKColor baseColor = ClassColor(enemy.Class);
            SKColor enemyColor = enemy.IsAlive ? baseColor
                : new SKColor((byte)(baseColor.Red / 2), (byte)(baseColor.Green / 2), (byte)(baseColor.Blue / 2));

            using var paint = new SKPaint
            {
                Color = enemyColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            float sz = 10f * (enemy.Radius / 0.35f);
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
                        // Bow Shot - Draw arrow with fletching
                        using (var shaftPaint = new SKPaint
                        {
                            Color = new SKColor(139, 69, 19, alphaVal), // Brown shaft
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            canvas.DrawLine(startPx, startPy, px, py, shaftPaint);
                        }
                        
                        // Arrowhead (metal)
                        float headSize = 5;
                        using var headPath = new SKPath();
                        headPath.MoveTo(px, py);
                        headPath.LineTo(
                            px - headSize * MathF.Cos(angle - 0.4f),
                            py - headSize * MathF.Sin(angle - 0.4f)
                        );
                        headPath.LineTo(
                            px - headSize * MathF.Cos(angle + 0.4f),
                            py - headSize * MathF.Sin(angle + 0.4f)
                        );
                        headPath.Close();
                        
                        using var headFill = new SKPaint
                        {
                            Color = new SKColor(160, 160, 160, alphaVal), // Silver tip
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        };
                        canvas.DrawPath(headPath, headFill);
                        
                        // Fletching (feathers)
                        float fletchX = startPx + dx * 0.2f;
                        float fletchY = startPy + dy * 0.2f;
                        using var fletchPaint = new SKPaint
                        {
                            Color = new SKColor(200, 0, 0, alphaVal), // Red feathers
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
                        };
                        canvas.DrawCircle(fletchX, fletchY, 2, fletchPaint);
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
                        // Magic Missile - Purple glowing orb with sparkles
                        using (var magicGlow = new SKPaint
                        {
                            Color = new SKColor(138, 43, 226, (byte)(alphaVal * 0.5f)), // Purple glow
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
                            Color = new SKColor(255, 105, 255, alphaVal), // Bright pink
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
                                Color = new SKColor(200, 100, 255, (byte)(alphaVal * 0.4f)),
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

                        using (var glow = new SKPaint
                        {
                            Color = new SKColor(100, 255, 255, (byte)(alphaVal * 160)), // Cyan glow
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
                            Color = new SKColor(180, 255, 255, (byte)alphaVal),
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
                                Color = new SKColor(120, 240, 255, (byte)(alphaVal * (200 - i * 30) / 255f * 255)),
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
                        using var ring = new SKPaint
                        {
                            Color = new SKColor(100, 220, 220, (byte)(alphaVal * 0.8f)),
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
                        // Sonic Blast - Sound wave rings
                        float waveProgress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        for (int i = 0; i < 3; i++)
                        {
                            float waveRadius = (waveProgress + i * 0.3f) * 20f;
                            using var wavePaint = new SKPaint
                            {
                                Color = new SKColor(100, 200, 255, (byte)(alphaVal * 0.5f)),
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
                            Color = new SKColor(150, 220, 255, alphaVal),
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
    
    private void DrawHUD(SKCanvas canvas, GameState gameState, int viewportWidth, int viewportHeight)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        using var textOutlinePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };
        
        using var barBgPaint = new SKPaint
        {
            Color = new SKColor(40, 40, 40),
            Style = SKPaintStyle.Fill
        };
        
        using var hpPaint = new SKPaint
        {
            Color = new SKColor(255, 80, 80),
            Style = SKPaintStyle.Fill
        };
        
        using var xpPaint = new SKPaint
        {
            Color = new SKColor(100, 180, 255),
            Style = SKPaintStyle.Fill
        };
        
        using var staminaPaint = new SKPaint
        {
            Color = new SKColor(100, 255, 100), // Green
            Style = SKPaintStyle.Fill
        };
        
        using var manaPaint = new SKPaint
        {
            Color = new SKColor(100, 150, 255), // Blue
            Style = SKPaintStyle.Fill
        };
        
        using var faithPaint = new SKPaint
        {
            Color = new SKColor(255, 215, 100), // Gold
            Style = SKPaintStyle.Fill
        };
        
        // HP Bar
        float barWidth = 200;
        float barHeight = 16;
        float barX = 10;
        float barY = 10;
        
        canvas.DrawRect(barX, barY, barWidth, barHeight, barBgPaint);
        
        float hpPercent = (float)gameState.Hero.CurrentHp / gameState.Hero.MaxHp;
        canvas.DrawRect(barX, barY, barWidth * hpPercent, barHeight, hpPaint);
        
        string hpText = $"HP: {gameState.Hero.CurrentHp}/{gameState.Hero.MaxHp}";
        canvas.DrawText(hpText, barX + 5, barY + 12, textOutlinePaint);
        canvas.DrawText(hpText, barX + 5, barY + 12, textPaint);
        
        // Resource bars - always show all three
        float resourceBarY = barY + 22;
        
        // Stamina Bar (Green)
        canvas.DrawRect(barX, resourceBarY, barWidth, barHeight, barBgPaint);
        float staminaPercent = (float)gameState.Hero.CurrentStamina / gameState.Hero.MaxStamina;
        canvas.DrawRect(barX, resourceBarY, barWidth * staminaPercent, barHeight, staminaPaint);
        string staminaText = $"Stamina: {gameState.Hero.CurrentStamina}/{gameState.Hero.MaxStamina}";
        canvas.DrawText(staminaText, barX + 5, resourceBarY + 12, textOutlinePaint);
        canvas.DrawText(staminaText, barX + 5, resourceBarY + 12, textPaint);
        
        // Mana Bar (Blue)
        float manaBarY = resourceBarY + 18;
        canvas.DrawRect(barX, manaBarY, barWidth, barHeight, barBgPaint);
        float manaPercent = (float)gameState.Hero.CurrentMana / gameState.Hero.MaxMana;
        canvas.DrawRect(barX, manaBarY, barWidth * manaPercent, barHeight, manaPaint);
        string manaText = $"Mana: {gameState.Hero.CurrentMana}/{gameState.Hero.MaxMana}";
        canvas.DrawText(manaText, barX + 5, manaBarY + 12, textOutlinePaint);
        canvas.DrawText(manaText, barX + 5, manaBarY + 12, textPaint);
        
        // Faith Bar (Gold)
        float faithBarY = manaBarY + 18;
        canvas.DrawRect(barX, faithBarY, barWidth, barHeight, barBgPaint);
        float faithPercent = (float)gameState.Hero.CurrentFaith / gameState.Hero.MaxFaith;
        canvas.DrawRect(barX, faithBarY, barWidth * faithPercent, barHeight, faithPaint);
        string faithText = $"Faith: {gameState.Hero.CurrentFaith}/{gameState.Hero.MaxFaith}";
        canvas.DrawText(faithText, barX + 5, faithBarY + 12, textOutlinePaint);
        canvas.DrawText(faithText, barX + 5, faithBarY + 12, textPaint);
        
        // XP Bar
        float xpBarY = faithBarY + 22;
        float xpPercent = gameState.Hero.ExperienceToNext > 0 
            ? (float)gameState.Hero.Experience / gameState.Hero.ExperienceToNext 
            : 0;
        canvas.DrawRect(barX, xpBarY, barWidth, barHeight, barBgPaint);
        canvas.DrawRect(barX, xpBarY, barWidth * xpPercent, barHeight, xpPaint);
        string levelText = $"Level {gameState.Hero.Level}";
        canvas.DrawText(levelText, barX + 5, xpBarY + 12, textOutlinePaint);
        canvas.DrawText(levelText, barX + 5, xpBarY + 12, textPaint);
        
        // Current attack info
        var currentAttack = gameState.Hero.CurrentAttack;
        if (currentAttack != null)
        {
            string attackInfo = $"Attack: {currentAttack.Name}";
            if (currentAttack.IsHeavyAttack)
            {
                if (currentAttack.StaminaCost > 0) attackInfo += $" ({currentAttack.StaminaCost} Stamina)";
                else if (currentAttack.ManaCost > 0) attackInfo += $" ({currentAttack.ManaCost} Mana)";
                else if (currentAttack.FaithCost > 0) attackInfo += $" ({currentAttack.FaithCost} Faith)";
            }
            canvas.DrawText(attackInfo, barX, xpBarY + 30, textOutlinePaint);
            canvas.DrawText(attackInfo, barX, xpBarY + 30, textPaint);
        }
        
        // Floor info (bottom)
        string floorText = $"Floor {gameState.CurrentFloor} | ATK: {gameState.Hero.Attack} | DEF: {gameState.Hero.Defense}";
        canvas.DrawText(floorText, 10, viewportHeight - 10, textOutlinePaint);
        canvas.DrawText(floorText, 10, viewportHeight - 10, textPaint);
    }
}

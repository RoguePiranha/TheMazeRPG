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
    
    // Color palette
    private static readonly SKColor BackgroundColor = new(26, 26, 26);
    private static readonly SKColor WallColor = new(128, 128, 128);
    private static readonly SKColor ExploredPathColor = new(64, 64, 64);
    private static readonly SKColor UnexploredColor = new(40, 40, 40);
    private static readonly SKColor HeroColor = new(100, 180, 255);
    private static readonly SKColor EnemyColor = new(255, 80, 80);
    private static readonly SKColor ChestColor = new(255, 215, 0);
    private static readonly SKColor StairsColor = new(150, 255, 150);
    
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
        
        // Center camera on viewport
        float offsetX = viewportWidth / 2f - _cameraX;
        float offsetY = viewportHeight / 2f - _cameraY;
        
        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        
        // Draw maze
        DrawMaze(canvas, gameState.CurrentMaze);
        
        // Draw features (chests, stairs)
        DrawFeatures(canvas, gameState.CurrentMaze);
        
        // Draw enemies
        DrawEnemies(canvas, gameState.Enemies);
        
        // Draw projectiles (between enemies and hero)
        DrawProjectiles(canvas, gameState.Projectiles);
        
        // Draw hero (always on top)
        DrawHero(canvas, gameState.Hero);
        
        canvas.Restore();
        
        // Draw HUD overlay
        DrawHUD(canvas, gameState, viewportWidth, viewportHeight);
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
                    DrawChest(canvas, px, py);
                    break;
            }
        }
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
    
    private void DrawChest(SKCanvas canvas, float x, float y)
    {
        using var paint = new SKPaint
        {
            Color = ChestColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        
        // Draw small diamond
        var path = new SKPath();
        path.MoveTo(x, y - 4);
        path.LineTo(x + 4, y);
        path.LineTo(x, y + 4);
        path.LineTo(x - 4, y);
        path.Close();
        
        canvas.DrawPath(path, paint);
    }
    
    private void DrawEnemies(SKCanvas canvas, List<Enemy> enemies)
    {
        foreach (var enemy in enemies)
        {
            float px = enemy.X * CellSize + CellSize / 2f;
            float py = enemy.Y * CellSize + CellSize / 2f;
            
            // Determine color and shape based on enemy class
            SKColor enemyColor;
            switch (enemy.Class)
            {
                case "Brute":
                    enemyColor = enemy.IsAlive ? new SKColor(200, 50, 50) : new SKColor(100, 25, 25); // Red
                    break;
                case "Striker":
                    enemyColor = enemy.IsAlive ? new SKColor(255, 165, 0) : new SKColor(127, 82, 0); // Orange
                    break;
                case "Archer":
                    enemyColor = enemy.IsAlive ? new SKColor(100, 200, 100) : new SKColor(50, 100, 50); // Green
                    break;
                case "Caster":
                    enemyColor = enemy.IsAlive ? new SKColor(150, 100, 255) : new SKColor(75, 50, 127); // Purple
                    break;
                default:
                    enemyColor = enemy.IsAlive ? EnemyColor : new SKColor(100, 40, 40);
                    break;
            }
            
            using var paint = new SKPaint
            {
                Color = enemyColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            
            // Draw different shapes based on enemy class
            switch (enemy.Class)
            {
                case "Brute":
                    // Square for tank
                    canvas.DrawRect(px - 10, py - 10, 20, 20, paint);
                    break;
                    
                case "Striker":
                    // Diamond for fast melee
                    var strikerPath = new SKPath();
                    strikerPath.MoveTo(px, py - 10);
                    strikerPath.LineTo(px + 10, py);
                    strikerPath.LineTo(px, py + 10);
                    strikerPath.LineTo(px - 10, py);
                    strikerPath.Close();
                    canvas.DrawPath(strikerPath, paint);
                    break;
                    
                case "Archer":
                    // Triangle pointing up for ranged
                    var archerPath = new SKPath();
                    archerPath.MoveTo(px, py - 10);
                    archerPath.LineTo(px + 9, py + 8);
                    archerPath.LineTo(px - 9, py + 8);
                    archerPath.Close();
                    canvas.DrawPath(archerPath, paint);
                    break;
                    
                case "Caster":
                    // Pentagon/Star for magic user
                    var casterPath = new SKPath();
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = (float)(i * 2 * Math.PI / 5 - Math.PI / 2);
                        float x = px + 10 * MathF.Cos(angle);
                        float y = py + 10 * MathF.Sin(angle);
                        if (i == 0)
                            casterPath.MoveTo(x, y);
                        else
                            casterPath.LineTo(x, y);
                    }
                    casterPath.Close();
                    canvas.DrawPath(casterPath, paint);
                    break;
                    
                default:
                    // Default triangle
                    var defaultPath = new SKPath();
                    defaultPath.MoveTo(px, py - 8);
                    defaultPath.LineTo(px + 8, py + 6);
                    defaultPath.LineTo(px - 8, py + 6);
                    defaultPath.Close();
                    canvas.DrawPath(defaultPath, paint);
                    break;
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
            
            switch (projectile.Type)
            {
                case AttackAnimation.Melee:
                    // Different visuals based on weapon type
                    if (projectile.AttackName.Contains("Slash") || projectile.AttackName.Contains("Sword"))
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
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
                        };
                        canvas.DrawCircle(tipX, tipY, 3, gleamPaint);
                    }
                    else if (projectile.AttackName.Contains("Holy") || projectile.AttackName.Contains("Smite"))
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
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
                        };
                        canvas.DrawCircle(px, py, 8, glowPaint);
                    }
                    else if (projectile.AttackName.Contains("Unarmed") || projectile.AttackName.Contains("Punch") || projectile.AttackName.Contains("Kick"))
                    {
                        // Unarmed Strike - Draw impact burst and motion lines
                        float progress = (float)projectile.LifeTime / projectile.MaxLifeTime;
                        
                        // Impact burst at target
                        using var impactPaint = new SKPaint
                        {
                            Color = new SKColor(255, 200, 100, alphaVal), // Orange impact
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
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
                    
                    if (projectile.AttackName.Contains("Bow") || projectile.AttackName.Contains("Arrow"))
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
                    else if (projectile.AttackName.Contains("Poison") || projectile.AttackName.Contains("Dart"))
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
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
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
                    if (projectile.AttackName.Contains("Magic Missile"))
                    {
                        // Magic Missile - Purple glowing orb with sparkles
                        using (var magicGlow = new SKPaint
                        {
                            Color = new SKColor(138, 43, 226, (byte)(alphaVal * 0.5f)), // Purple glow
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true,
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
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
                    else if (projectile.AttackName.Contains("Sonic"))
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
                                IsAntialias = true
                            };
                            canvas.DrawCircle(px, py, waveRadius, wavePaint);
                        }
                        
                        // Central note symbol
                        using var notePaint = new SKPaint
                        {
                            Color = new SKColor(150, 220, 255, alphaVal),
                            Style = SKPaintStyle.Fill,
                            IsAntialias = true
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
                            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3)
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
                    if (projectile.AttackName.Contains("Backstab"))
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
                    else if (projectile.AttackName.Contains("Parry"))
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
        
        // HP Bar
        float barWidth = 200;
        float barHeight = 16;
        float barX = 10;
        float barY = 10;
        
        canvas.DrawRect(barX, barY, barWidth, barHeight, barBgPaint);
        
        float hpPercent = (float)gameState.Hero.CurrentHp / gameState.Hero.MaxHp;
        canvas.DrawRect(barX, barY, barWidth * hpPercent, barHeight, hpPaint);
        
        canvas.DrawText($"HP: {gameState.Hero.CurrentHp}/{gameState.Hero.MaxHp}", barX + 5, barY + 12, textPaint);
        
        // XP Bar
        float xpPercent = gameState.Hero.ExperienceToNext > 0 
            ? (float)gameState.Hero.Experience / gameState.Hero.ExperienceToNext 
            : 0;
        canvas.DrawRect(barX, barY + 25, barWidth, barHeight, barBgPaint);
        canvas.DrawRect(barX, barY + 25, barWidth * xpPercent, barHeight, xpPaint);
        canvas.DrawText($"Level {gameState.Hero.Level}", barX + 5, barY + 37, textPaint);
        
        // Floor info (bottom)
        string floorText = $"Floor {gameState.CurrentFloor} | ATK: {gameState.Hero.Attack} | DEF: {gameState.Hero.Defense}";
        canvas.DrawText(floorText, 10, viewportHeight - 10, textPaint);
    }
}

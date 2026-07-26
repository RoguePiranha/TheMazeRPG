using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using TheMazeRPG.Core.Services;
using TheMazeRPG.UI.Rendering;

namespace TheMazeRPG.UI.Controls;

/// <summary>
/// Custom control that renders the game using Skia
/// </summary>
public class GameCanvas : Control
{
    private readonly MazeRenderer _renderer;
    private GameState? _gameState;
    
    public GameCanvas()
    {
        _renderer = new MazeRenderer();

        // Request render updates at ~60 FPS
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (s, e) => InvalidateVisual();
        timer.Start();

        // Click-to-fire: aim the hero's current attack toward the clicked point (Manual mode).
        PointerPressed += OnPointerPressed;
        // Scroll wheel cycles the selected hotbar attack.
        PointerWheelChanged += OnPointerWheel;
    }

    public void SetGameState(GameState gameState)
    {
        _gameState = gameState;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameState == null) return;

        var pos = e.GetPosition(this);
        var (worldX, worldY) = _renderer.ScreenToWorld(pos.X, pos.Y);
        float dx = worldX - _gameState.Hero.X;
        float dy = worldY - _gameState.Hero.Y;

        // No-op in Auto mode / on cooldown / when unaffordable (FireManualAttack guards all that).
        _gameState.FireManualAttack(dx, dy);
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_gameState == null) return;
        // Wheel up (Delta.Y > 0) → previous slot; wheel down → next.
        _gameState.CycleAttack(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }
    
    public override void Render(DrawingContext context)
    {
        if (_gameState == null) return;
        
        context.Custom(new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), _renderer, _gameState));
    }
    
    private class CustomDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly MazeRenderer _renderer;
        private readonly GameState _gameState;
        
        public CustomDrawOp(Rect bounds, MazeRenderer renderer, GameState gameState)
        {
            _bounds = bounds;
            _renderer = renderer;
            _gameState = gameState;
        }
        
        public void Dispose() { }
        
        public Rect Bounds => _bounds;
        
        public bool HitTest(Point p) => _bounds.Contains(p);
        
        public bool Equals(ICustomDrawOperation? other) => false;
        
        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            
            _renderer.Render(canvas, _gameState, (int)_bounds.Width, (int)_bounds.Height);
        }
    }
}

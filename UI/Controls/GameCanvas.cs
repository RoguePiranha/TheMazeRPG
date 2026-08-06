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
    
    private readonly DispatcherTimer _renderTimer;

    public GameCanvas()
    {
        _renderer = new MazeRenderer();

        // Request render updates at ~60 FPS. Held as a field and started/stopped with visual-tree
        // attachment: a running DispatcherTimer is rooted by the dispatcher, so an always-on
        // constructor timer would pin this canvas — and through it the whole retired GameState —
        // forever after every quit-to-menu.
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (s, e) => InvalidateVisual();

        // Click-to-fire: aim the hero's current attack toward the clicked point (Manual mode).
        PointerPressed += OnPointerPressed;
        // Scroll wheel cycles the selected hotbar attack.
        PointerWheelChanged += OnPointerWheel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _renderTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _renderTimer.Stop();
    }

    public void SetGameState(GameState gameState)
    {
        _gameState = gameState;
    }

    /// <summary>Raised on a right-click that lands on an interactable (a trap or a corpse). The
    /// shell (GameView) shows the context menu at the given canvas-relative point.</summary>
    public event Action<InteractTarget>? InteractRequested;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameState == null) return;

        var pos = e.GetPosition(this);
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;

        if (kind == PointerUpdateKind.RightButtonPressed)
        {
            e.Handled = true;
            HandleRightClick(pos);
            return;
        }

        // Left-click → click-to-fire toward the point (no-op in Auto / on cooldown / unaffordable).
        var (worldX, worldY) = _renderer.ScreenToWorld(pos.X, pos.Y);
        _gameState.FireManualAttack(worldX - _gameState.Hero.X, worldY - _gameState.Hero.Y);
    }

    // Hit-test the nearest interactable (trap feature or corpse) near a right-click and, if one is
    // in reach, raise InteractRequested for the shell to show a menu.
    private void HandleRightClick(Point pos)
    {
        if (_gameState == null) return;
        var (wx, wy) = _renderer.ScreenToWorld(pos.X, pos.Y);
        const float pickRadius = 0.8f;
        float best = pickRadius;
        InteractTarget? target = null;

        foreach (var f in _gameState.CurrentMaze.Features)
        {
            if (f.IsUsed || f.Type != Core.Models.MazeFeatureType.Trap) continue;
            float d = Dist(wx, wy, f.X, f.Y);
            if (d <= best) { best = d; target = new InteractTarget { Feature = f, ScreenPoint = pos }; }
        }
        foreach (var enemy in _gameState.Enemies)
        {
            if (enemy.IsAlive) continue; // corpses only
            float d = Dist(wx, wy, enemy.X, enemy.Y);
            if (d <= best) { best = d; target = new InteractTarget { Corpse = enemy, ScreenPoint = pos }; }
        }

        if (target != null) InteractRequested?.Invoke(target);
    }

    private static float Dist(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return (float)Math.Sqrt(dx * dx + dy * dy);
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

        // Snapshot the sim's mutable collections HERE, on the UI thread — the draw op below
        // executes on the render thread while Tick keeps mutating the live lists, and
        // enumerating those there crashes intermittently (see RenderSnapshot).
        var snapshot = RenderSnapshot.Capture(_gameState);
        if (snapshot == null) return;

        context.Custom(new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), _renderer, _gameState, snapshot));
    }

    private class CustomDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly MazeRenderer _renderer;
        private readonly GameState _gameState;
        private readonly RenderSnapshot _snapshot;

        public CustomDrawOp(Rect bounds, MazeRenderer renderer, GameState gameState, RenderSnapshot snapshot)
        {
            _bounds = bounds;
            _renderer = renderer;
            _gameState = gameState;
            _snapshot = snapshot;
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

            _renderer.Render(canvas, _gameState, _snapshot, (int)_bounds.Width, (int)_bounds.Height);
        }
    }
}

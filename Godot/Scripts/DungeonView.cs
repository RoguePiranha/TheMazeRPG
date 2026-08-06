using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.GodotClient;

public partial class DungeonView : Node2D
{
    public const int CellSize = 48;
    private const float FogMemoryAlpha = 0.3f;

    private Maze? _visibilityMaze;
    private readonly HashSet<(int x, int y)> _visibleFloors = new();
    private readonly HashSet<(int x, int y)> _seenFloors = new();
    private bool _fogEnabled;
    private int _visibilityTick = -1;
    private float _visibilityHeroX = float.NaN;
    private float _visibilityHeroY = float.NaN;
    private bool _visibilitySafeRoom;
    private bool _visibilityOverworld;

    public GameState? State { get; set; }
    public IReadOnlyList<(int x, int y)> TacticalPathPreview { get; set; } =
        Array.Empty<(int x, int y)>();
    public TacticalAttackPreview? TacticalAttackPreview { get; set; }
    public MazeFeature? HoveredFeature { get; set; }
    public Enemy? HoveredCorpse { get; set; }

    public override void _Draw()
    {
        if (State == null)
            return;

        Maze maze = State.CurrentMaze;
        Palette palette = State.IsInOverworld ? Palette.Town : Palette.For(maze.Dungeon?.Theme);
        UpdateVisibility(State);

        // Only the cells on screen. Regions are person-scaled (one tile ~= one metre), so a real
        // town runs into the hundreds of tiles per axis — walking the whole grid would cost a
        // quarter of a million iterations and as many draw calls per frame, and the town is fully
        // lit so every one of them would actually draw.
        var (minX, minY, maxX, maxY) = VisibleCellBounds(maze);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (maze.Walls[x, y])
                {
                    var (visible, seen) = WallVisibility(maze, x, y);
                    if (seen) DrawCell(maze, x, y, palette, dimmed: !visible);
                }
                else if (IsCellSeenCached(x, y))
                {
                    DrawCell(maze, x, y, palette, dimmed: !IsCellVisibleCached(x, y));
                }
            }
        }

        DrawTacticalMovementRange(State);
        DrawTacticalEnemyIntents(State);
        DrawTacticalAttackPreview(State);
        DrawTacticalPathPreview(State);
        DrawDungeonDetails(maze, palette);
        DrawMazeFeatures(maze, palette);
        DrawCritters(State);
        DrawProjectiles(State, palette);
        DrawEnemies(State, palette);
        DrawHero(State, palette);
        DrawFloatingTexts(State);
    }

    public static Vector2 WorldToPixel(float x, float y) =>
        new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    public void RefreshVisibility()
    {
        if (State != null) UpdateVisibility(State, force: true);
    }

    public bool IsCellVisible(int x, int y)
    {
        if (State == null || x < 0 || y < 0 || x >= State.CurrentMaze.Width || y >= State.CurrentMaze.Height)
            return false;
        UpdateVisibility(State);
        return IsTileVisibleCached(State.CurrentMaze, x, y);
    }

    public bool IsCellSeen(int x, int y)
    {
        if (State == null || x < 0 || y < 0 || x >= State.CurrentMaze.Width || y >= State.CurrentMaze.Height)
            return false;
        UpdateVisibility(State);
        return IsTileSeenCached(State.CurrentMaze, x, y);
    }

    private void UpdateVisibility(GameState state, bool force = false)
    {
        Maze maze = state.CurrentMaze;
        bool newMaze = !ReferenceEquals(_visibilityMaze, maze);
        if (newMaze)
        {
            _visibilityMaze = maze;
            _seenFloors.Clear();
        }

        bool enabled = !state.IsInOverworld && !state.IsInSafeRoom;
        if (!force && !newMaze && _visibilityTick == state.TickCount &&
            MathF.Abs(_visibilityHeroX - state.Hero.X) < 0.001f &&
            MathF.Abs(_visibilityHeroY - state.Hero.Y) < 0.001f &&
            _visibilitySafeRoom == state.IsInSafeRoom && _visibilityOverworld == state.IsInOverworld)
            return;

        _visibilityTick = state.TickCount;
        _visibilityHeroX = state.Hero.X;
        _visibilityHeroY = state.Hero.Y;
        _visibilitySafeRoom = state.IsInSafeRoom;
        _visibilityOverworld = state.IsInOverworld;
        _fogEnabled = enabled;
        _visibleFloors.Clear();
        if (!enabled) return;

        float range = state.VisionRange;
        int minX = Math.Max(0, (int)MathF.Floor(state.Hero.X - range));
        int maxX = Math.Min(maze.Width - 1, (int)MathF.Ceiling(state.Hero.X + range));
        int minY = Math.Max(0, (int)MathF.Floor(state.Hero.Y - range));
        int maxY = Math.Min(maze.Height - 1, (int)MathF.Ceiling(state.Hero.Y + range));
        float rangeSquared = range * range;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (maze.Walls[x, y]) continue;
                float dx = x - state.Hero.X;
                float dy = y - state.Hero.Y;
                if (dx * dx + dy * dy > rangeSquared || !state.CheckLOS(state.Hero.X, state.Hero.Y, x, y))
                    continue;
                _visibleFloors.Add((x, y));
                _seenFloors.Add((x, y));
            }
        }

        // Walked tiles (Maze.Explored) used to be swept into _seenFloors here — a full-grid pass on
        // every hero step. IsCellSeenCached reads Explored directly instead, which is the same
        // answer without the scan.
    }

    /// <summary>
    /// The inclusive cell range currently on screen, in this node's own cell coordinates. Derived
    /// from the live camera rather than the hero, so it stays correct while the camera lerps toward
    /// them, during screen shake, and at any zoom.
    /// </summary>
    /// <summary>Diagnostic accessor for the scale probe — how much of the grid culling keeps.</summary>
    public (int minX, int minY, int maxX, int maxY) DebugVisibleCellBounds() =>
        State == null ? (0, 0, 0, 0) : VisibleCellBounds(State.CurrentMaze);

    private (int minX, int minY, int maxX, int maxY) VisibleCellBounds(Maze maze)
    {
        // Node-local -> viewport, inverted. GetCanvasTransform (not GetViewportTransform) is the
        // right one: the project stretches a 1280x720 base viewport to the window, and
        // GetViewportRect reports the *base* size, whereas GetViewportTransform maps all the way
        // out to window space. Mixing those two spaces undercounts the visible area by exactly the
        // stretch factor, which culls away the right and bottom of the screen.
        Transform2D localToViewport = GetCanvasTransform() * GetGlobalTransform();
        // A zero-scale (non-invertible) transform can't be inverted; fall back to the whole grid
        // rather than silently drawing nothing.
        if (MathF.Abs(localToViewport.Determinant()) < 0.000001f)
            return (0, 0, maze.Width - 1, maze.Height - 1);
        Transform2D screenToLocal = localToViewport.AffineInverse();

        Rect2 screen = GetViewportRect();
        Vector2 a = screenToLocal * screen.Position;
        Vector2 b = screenToLocal * new Vector2(screen.End.X, screen.Position.Y);
        Vector2 c = screenToLocal * new Vector2(screen.Position.X, screen.End.Y);
        Vector2 d = screenToLocal * screen.End;

        float left = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
        float right = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
        float top = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
        float bottom = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));

        // One cell of margin so a partially-scrolled cell at the edge still draws.
        int minX = Math.Clamp((int)MathF.Floor(left / CellSize) - 1, 0, maze.Width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(right / CellSize) + 1, 0, maze.Width - 1);
        int minY = Math.Clamp((int)MathF.Floor(top / CellSize) - 1, 0, maze.Height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(bottom / CellSize) + 1, 0, maze.Height - 1);
        return (minX, minY, maxX, maxY);
    }

    private bool IsCellVisibleCached(int x, int y) => !_fogEnabled || _visibleFloors.Contains((x, y));

    /// <summary>Seen = remembered by this renderer's own LOS memory, or walked (Maze.Explored).
    /// Reading Explored on demand avoids a full-grid scan every time the hero moves.</summary>
    private bool IsCellSeenCached(int x, int y) =>
        !_fogEnabled || _seenFloors.Contains((x, y)) || IsExplored(x, y);

    private bool IsExplored(int x, int y) =>
        _visibilityMaze != null &&
        x >= 0 && y >= 0 && x < _visibilityMaze.Width && y < _visibilityMaze.Height &&
        _visibilityMaze.Explored[x, y] && !_visibilityMaze.Walls[x, y];
    private bool IsTileVisibleCached(Maze maze, int x, int y) =>
        maze.Walls[x, y] ? WallVisibility(maze, x, y).visible : IsCellVisibleCached(x, y);
    private bool IsTileSeenCached(Maze maze, int x, int y) =>
        maze.Walls[x, y] ? WallVisibility(maze, x, y).seen : IsCellSeenCached(x, y);

    private (bool visible, bool seen) WallVisibility(Maze maze, int x, int y)
    {
        if (!_fogEnabled) return (true, true);
        bool seen = false;
        for (int nx = x - 1; nx <= x + 1; nx++)
        {
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                if (!IsWalkable(maze, nx, ny)) continue;
                if (_visibleFloors.Contains((nx, ny))) return (true, true);
                // Explored included, matching IsCellSeenCached — walls light up from any adjacent
                // floor the hero has either seen or walked.
                if (_seenFloors.Contains((nx, ny)) || IsExplored(nx, ny)) seen = true;
            }
        }
        return (false, seen);
    }

    private static Color FogColor(Color color, bool dimmed) => dimmed
        ? new Color(color.R, color.G, color.B, color.A * FogMemoryAlpha)
        : color;

    private void DrawCell(Maze maze, int x, int y, Palette palette, bool dimmed)
    {
        Rect2 cell = new(x * CellSize, y * CellSize, CellSize, CellSize);

        // Shut doors block, so they'd otherwise fall into the wall branch and read as solid stone.
        // Drawn before that check so a closed door looks like something you can open.
        if (maze.TileAt(x, y).IsDoor())
        {
            DrawDoor(maze, x, y, cell, palette, dimmed);
            return;
        }

        if (maze.Walls[x, y])
        {
            DrawRect(cell, FogColor(palette.Wall, dimmed));
            DrawRect(cell.Grow(-1), FogColor(palette.WallInset, dimmed), false, 1f);
            DrawExposedWallEdges(maze, x, y, cell, FogColor(palette.WallEdge, dimmed));
            return;
        }

        DungeonTileType type = maze.Dungeon?.Tiles[x, y] ?? DungeonTileType.RoomFloor;
        Color floorColor = type switch
        {
            DungeonTileType.CorridorFloor => palette.Corridor,
            DungeonTileType.Doorway => palette.Doorway,
            _ => ((x * 31 + y * 17) & 3) == 0 ? palette.FloorVariant : palette.Floor
        };

        DrawRect(cell, FogColor(floorColor, dimmed));
        DrawLine(cell.Position, cell.Position + Vector2.Right * CellSize, FogColor(palette.Grout, dimmed), 1f);
        DrawLine(cell.Position, cell.Position + Vector2.Down * CellSize, FogColor(palette.Grout, dimmed), 1f);

        if (type == DungeonTileType.Doorway && maze.Dungeon != null)
        {
            bool eastWest = maze.Dungeon.DoorwayOrientationAt(x, y) == DungeonPassageOrientation.EastWest;
            Vector2 center = cell.GetCenter();
            Vector2 half = eastWest ? new Vector2(0, 15) : new Vector2(15, 0);
            DrawLine(center - half, center + half, FogColor(palette.DoorwayEdge, dimmed), 3f);
        }
    }

    /// <summary>
    /// A door reads at a glance: shut ones fill their frame so you can see they're in the way, open
    /// ones swing aside leaving the threshold clear, locked ones carry a keyhole mark.
    /// </summary>
    private void DrawDoor(Maze maze, int x, int y, Rect2 cell, Palette palette, bool dimmed)
    {
        TileType tile = maze.TileAt(x, y);
        bool eastWest = maze.Dungeon?.DoorwayOrientationAt(x, y) == DungeonPassageOrientation.EastWest;

        // Floor underneath, so an open doorway reads as passable ground.
        DrawRect(cell, FogColor(palette.Doorway, dimmed));

        Color timber = tile == TileType.DoorLocked
            ? new Color(0.42f, 0.29f, 0.16f)
            : new Color(0.53f, 0.37f, 0.20f);
        Color frame = new(0.24f, 0.17f, 0.10f);
        Vector2 center = cell.GetCenter();

        if (tile == TileType.DoorOpen)
        {
            // Swung aside: a leaf folded back against the jamb, threshold left clear.
            Vector2 leafSize = eastWest ? new Vector2(CellSize * 0.72f, 7) : new Vector2(7, CellSize * 0.72f);
            Vector2 leafAt = eastWest
                ? new Vector2(cell.Position.X + CellSize * 0.14f, cell.Position.Y + 2)
                : new Vector2(cell.Position.X + 2, cell.Position.Y + CellSize * 0.14f);
            DrawRect(new Rect2(leafAt, leafSize), FogColor(timber, dimmed));
            return;
        }

        // Shut: fills the opening, with a visible frame and plank seams.
        Rect2 slab = eastWest
            ? new Rect2(cell.Position.X, cell.Position.Y + CellSize * 0.18f, CellSize, CellSize * 0.64f)
            : new Rect2(cell.Position.X + CellSize * 0.18f, cell.Position.Y, CellSize * 0.64f, CellSize);
        DrawRect(slab, FogColor(timber, dimmed));
        DrawRect(slab, FogColor(frame, dimmed), false, 2f);

        for (int i = 1; i <= 2; i++)
        {
            float t = i / 3f;
            Vector2 from = eastWest
                ? new Vector2(slab.Position.X + slab.Size.X * t, slab.Position.Y)
                : new Vector2(slab.Position.X, slab.Position.Y + slab.Size.Y * t);
            Vector2 to = eastWest
                ? new Vector2(slab.Position.X + slab.Size.X * t, slab.End.Y)
                : new Vector2(slab.End.X, slab.Position.Y + slab.Size.Y * t);
            DrawLine(from, to, FogColor(frame, dimmed), 1f);
        }

        DrawCircle(center, 3f, FogColor(tile == TileType.DoorLocked
            ? new Color(0.85f, 0.72f, 0.30f)   // brass keyhole: this one needs a key
            : new Color(0.20f, 0.15f, 0.09f), dimmed));
    }

    private void DrawExposedWallEdges(Maze maze, int x, int y, Rect2 cell, Color color)
    {
        Vector2 topLeft = cell.Position;
        Vector2 topRight = cell.Position + Vector2.Right * CellSize;
        Vector2 bottomLeft = cell.Position + Vector2.Down * CellSize;
        Vector2 bottomRight = cell.End;

        if (IsWalkable(maze, x, y - 1)) DrawLine(topLeft, topRight, color, 3f);
        if (IsWalkable(maze, x + 1, y)) DrawLine(topRight, bottomRight, color, 3f);
        if (IsWalkable(maze, x, y + 1)) DrawLine(bottomLeft, bottomRight, color, 3f);
        if (IsWalkable(maze, x - 1, y)) DrawLine(topLeft, bottomLeft, color, 3f);
    }

    private void DrawDungeonDetails(Maze maze, Palette palette)
    {
        if (maze.Dungeon == null)
            return;

        foreach (DungeonDecoration decoration in maze.Dungeon.Decorations)
        {
            if (!IsCellSeenCached(decoration.X, decoration.Y)) continue;
            bool dimmed = !IsCellVisibleCached(decoration.X, decoration.Y);
            Vector2 center = WorldToPixel(decoration.X, decoration.Y);
            float size = 5f + decoration.Variant % 3;
            Color color = decoration.Type switch
            {
                DungeonDecorationType.Brazier or DungeonDecorationType.Campfire => palette.Fire,
                DungeonDecorationType.Mushrooms or DungeonDecorationType.Rune => palette.Arcane,
                DungeonDecorationType.Bones => palette.Bone,
                _ => palette.Prop
            };
            DrawRect(new Rect2(center - Vector2.One * size, Vector2.One * size * 2f),
                FogColor(color, dimmed));
        }

        foreach (DungeonThemeFeature feature in maze.Dungeon.ThemeFeatures)
        {
            if (!IsCellSeenCached(feature.X, feature.Y)) continue;
            bool dimmed = !IsCellVisibleCached(feature.X, feature.Y);
            Color color = FogColor(palette.Hazard, dimmed);
            Vector2 center = WorldToPixel(feature.X, feature.Y);
            DrawCircle(center, 10f, color, false, 3f);
            DrawLine(center + new Vector2(-6, 0), center + new Vector2(6, 0), color, 2f);
            DrawLine(center + new Vector2(0, -6), center + new Vector2(0, 6), color, 2f);
        }
    }

    private void DrawTacticalMovementRange(GameState state)
    {
        TacticalTurnState turn = state.TacticalTurn;
        if (state.SimulationMode != SimulationMode.TurnBased || !turn.IsPlayerTurn ||
            turn.MovementRemaining <= 0 || TacticalAttackPreview != null)
            return;

        int startX = state.Hero.GridX;
        int startY = state.Hero.GridY;
        var distances = new Dictionary<(int x, int y), int> { [(startX, startY)] = 0 };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        (int dx, int dy)[] directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int distance = distances[current];
            if (distance >= turn.MovementRemaining) continue;

            foreach (var (dx, dy) in directions)
            {
                var next = (x: current.x + dx, y: current.y + dy);
                if (distances.ContainsKey(next) || !state.CurrentMaze.IsWalkable(next.x, next.y) ||
                    !IsCellSeenCached(next.x, next.y))
                    continue;
                bool occupied = state.Enemies.Exists(enemy => enemy.IsAlive &&
                    (int)System.MathF.Round(enemy.X) == next.x && (int)System.MathF.Round(enemy.Y) == next.y);
                if (occupied) continue;
                distances[next] = distance + 1;
                queue.Enqueue(next);
            }
        }

        Color fill = new(0.12f, 0.55f, 0.74f, 0.16f);
        Color edge = new(0.32f, 0.76f, 0.88f, 0.42f);
        foreach (var cell in distances.Keys)
        {
            if (cell == (startX, startY)) continue;
            Rect2 rect = new(cell.x * CellSize + 2, cell.y * CellSize + 2, CellSize - 4, CellSize - 4);
            DrawRect(rect, fill);
            DrawRect(rect, edge, false, 1f);
        }
    }

    private void DrawTacticalEnemyIntents(GameState state)
    {
        TacticalTurnState turn = state.TacticalTurn;
        if (state.SimulationMode != SimulationMode.TurnBased || !turn.IsPlayerTurn)
            return;

        foreach (TacticalEnemyIntent intent in turn.EnemyIntents)
        {
            int actorX = (int)MathF.Round(intent.FromX);
            int actorY = (int)MathF.Round(intent.FromY);
            if (!IsCellVisibleCached(actorX, actorY)) continue;
            Color color = intent.Kind switch
            {
                TacticalIntentKind.Attack => new Color("#E46962"),
                TacticalIntentKind.Advance => new Color("#79B8D1"),
                TacticalIntentKind.Reposition => new Color("#E8C861"),
                _ => new Color("#929CA2")
            };
            Vector2 actorCenter = WorldToPixel(intent.FromX, intent.FromY);

            if (intent.TargetX.HasValue && intent.TargetY.HasValue)
            {
                int targetX = (int)MathF.Round(intent.TargetX.Value);
                int targetY = (int)MathF.Round(intent.TargetY.Value);
                if (IsCellSeenCached(targetX, targetY))
                {
                    Vector2 targetCenter = WorldToPixel(intent.TargetX.Value, intent.TargetY.Value);
                    DrawLine(actorCenter, targetCenter, color with { A = 0.72f }, 2f);
                    Rect2 target = new(intent.TargetX.Value * CellSize + 5,
                        intent.TargetY.Value * CellSize + 5, CellSize - 10, CellSize - 10);
                    DrawRect(target, color with { A = 0.12f });
                    DrawRect(target, color with { A = 0.86f }, false, 2f);
                }
            }

            Vector2 badgeCenter = actorCenter + new Vector2(-16, -16);
            DrawCircle(badgeCenter, 10f, color);
            DrawString(ThemeDB.FallbackFont, badgeCenter + new Vector2(-8, 5),
                intent.Order.ToString(), HorizontalAlignment.Center, 16f, 12, Colors.White);
        }
    }

    private void DrawTacticalPathPreview(GameState state)
    {
        if (state.SimulationMode != SimulationMode.TurnBased ||
            !state.TacticalTurn.IsPlayerTurn || TacticalPathPreview.Count == 0)
            return;

        Color pathColor = new("#71B895");
        Vector2 previous = WorldToPixel(state.Hero.X, state.Hero.Y);
        foreach (var cell in TacticalPathPreview)
        {
            if (!IsCellSeenCached(cell.x, cell.y)) break;
            Vector2 center = WorldToPixel(cell.x, cell.y);
            DrawLine(previous, center, pathColor with { A = 0.9f }, 3f);
            Rect2 tile = new(cell.x * CellSize + 7, cell.y * CellSize + 7,
                CellSize - 14, CellSize - 14);
            DrawRect(tile, pathColor with { A = 0.18f });
            DrawRect(tile, pathColor with { A = 0.82f }, false, 2f);
            DrawCircle(center, 3f, pathColor);
            previous = center;
        }

        var destination = TacticalPathPreview[^1];
        Vector2 destinationCenter = WorldToPixel(destination.x, destination.y);
        Rect2 costBack = new(destinationCenter + new Vector2(-24, 9), new Vector2(48, 16));
        DrawRect(costBack, new Color(0.04f, 0.06f, 0.07f, 0.92f));
        DrawString(ThemeDB.FallbackFont, costBack.Position + new Vector2(0, 12),
            $"{TacticalPathPreview.Count} MOVE", HorizontalAlignment.Center, costBack.Size.X,
            10, pathColor);
    }

    private void DrawTacticalAttackPreview(GameState state)
    {
        TacticalAttackPreview? preview = TacticalAttackPreview;
        if (state.SimulationMode != SimulationMode.TurnBased ||
            !state.TacticalTurn.IsPlayerTurn || preview == null ||
            preview.TargetX < 0 || preview.TargetY < 0 ||
            preview.TargetX >= state.CurrentMaze.Width || preview.TargetY >= state.CurrentMaze.Height ||
            !IsTileVisibleCached(state.CurrentMaze, preview.TargetX, preview.TargetY))
            return;

        Color rangeColor = new("#79B8D1");
        Color areaColor = new("#C89BE5");
        Color targetColor = preview.CanCommit ? new Color("#71B895") : new Color("#E46962");
        foreach (var cell in preview.RangeCells)
        {
            if (!IsCellSeenCached(cell.x, cell.y)) continue;
            Rect2 tile = new(cell.x * CellSize + 4, cell.y * CellSize + 4,
                CellSize - 8, CellSize - 8);
            DrawRect(tile, rangeColor with { A = 0.1f });
            DrawRect(tile, rangeColor with { A = 0.38f }, false, 1f);
        }

        foreach (var cell in preview.AffectedCells)
        {
            if (!IsCellSeenCached(cell.x, cell.y)) continue;
            Rect2 tile = new(cell.x * CellSize + 3, cell.y * CellSize + 3,
                CellSize - 6, CellSize - 6);
            DrawRect(tile, areaColor with { A = preview.CanCommit ? 0.2f : 0.1f });
            DrawRect(tile, areaColor with { A = preview.CanCommit ? 0.78f : 0.36f }, false, 2f);
        }

        Vector2 heroCenter = WorldToPixel(state.Hero.X, state.Hero.Y);
        Vector2 impactCenter = WorldToPixel(preview.ImpactX, preview.ImpactY);
        DrawLine(heroCenter, impactCenter, targetColor with { A = 0.9f }, 3f);
        DrawCircle(impactCenter, 6f, targetColor, false, 2f);

        bool targetInBounds = preview.TargetX >= 0 && preview.TargetY >= 0 &&
            preview.TargetX < state.CurrentMaze.Width && preview.TargetY < state.CurrentMaze.Height;
        if (targetInBounds)
        {
            Rect2 target = new(preview.TargetX * CellSize + 2, preview.TargetY * CellSize + 2,
                CellSize - 4, CellSize - 4);
            DrawRect(target, targetColor with { A = 0.12f });
            DrawRect(target, targetColor with { A = 0.92f }, false, 3f);
        }

        string primaryText = $"{preview.AttackName}  {preview.Status}  RANGE {preview.Range:0.#}";
        int visibleAffectedEnemies = preview.AreaRadius > 0f
            ? state.Enemies.Count(enemy => enemy.IsAlive &&
                IsCellVisibleCached((int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y)) &&
                new Vector2(enemy.X, enemy.Y).DistanceTo(new Vector2(preview.TargetX, preview.TargetY)) <=
                    preview.AreaRadius + enemy.Radius)
            : 0;
        string? areaText = preview.AreaRadius > 0f
            ? $"AOE {preview.AreaRadius:0.#}  {visibleAffectedEnemies} " +
              (visibleAffectedEnemies == 1 ? "TARGET" : "TARGETS")
            : null;
        int longestLine = Math.Max(primaryText.Length, areaText?.Length ?? 0);
        float labelWidth = Math.Clamp(longestLine * 6f + 12f, 120f, 280f);
        float labelHeight = areaText == null ? 18f : 33f;
        Rect2 labelBack = new(impactCenter + new Vector2(-labelWidth / 2f, 14),
            new Vector2(labelWidth, labelHeight));
        DrawRect(labelBack, new Color(0.04f, 0.06f, 0.07f, 0.94f));
        DrawString(ThemeDB.FallbackFont, labelBack.Position + new Vector2(6, 13), primaryText,
            HorizontalAlignment.Left, labelBack.Size.X - 12, 11, targetColor);
        if (areaText != null)
            DrawString(ThemeDB.FallbackFont, labelBack.Position + new Vector2(6, 28), areaText,
                HorizontalAlignment.Left, labelBack.Size.X - 12, 11, areaColor);
    }

    private void DrawMazeFeatures(Maze maze, Palette palette)
    {
        foreach (MazeFeature feature in maze.Features)
        {
            int cellX = (int)MathF.Round(feature.X);
            int cellY = (int)MathF.Round(feature.Y);
            if (feature.IsUsed || !IsCellSeenCached(cellX, cellY) ||
                feature.Hidden && !feature.Perceived) continue;

            bool dimmed = !IsCellVisibleCached(cellX, cellY);
            Vector2 center = WorldToPixel(feature.X, feature.Y);
            switch (feature.Type)
            {
                case MazeFeatureType.Stairs:
                    for (int i = 0; i < 4; i++)
                    {
                        float width = 24f - i * 4f;
                        DrawLine(center + new Vector2(-width / 2f, -9f + i * 6f),
                            center + new Vector2(width / 2f, -9f + i * 6f),
                            FogColor(palette.Exit, dimmed), 3f);
                    }
                    break;
                case MazeFeatureType.Chest:
                    if (ReferenceEquals(feature, HoveredFeature))
                    {
                        Rect2 glowBounds = feature.IsOpened
                            ? new Rect2(center + new Vector2(-14, -19), new Vector2(28, 30))
                            : new Rect2(center + new Vector2(-14, -12), new Vector2(28, 24));
                        DrawInteractionGlow(glowBounds);
                    }
                    DrawRect(new Rect2(center + new Vector2(-11, -8), new Vector2(22, 16)),
                        FogColor(palette.Chest, dimmed));
                    float lidY = feature.IsOpened ? -13f : -1f;
                    DrawRect(new Rect2(center + new Vector2(-11, lidY - 3), new Vector2(22, 6)),
                        FogColor(palette.ChestEdge, dimmed), false, 2f);
                    if (feature.IsLocked && !feature.IsOpened)
                    {
                        DrawCircle(center + new Vector2(0, 2), 3f,
                            FogColor(palette.Exit, dimmed), false, 2f);
                        DrawRect(new Rect2(center + new Vector2(-2, 3), new Vector2(4, 5)),
                            FogColor(palette.Exit, dimmed));
                    }
                    break;
                case MazeFeatureType.Trap:
                    if (ReferenceEquals(feature, HoveredFeature))
                        DrawInteractionGlow(center, 10f);
                    DrawCircle(center, 8f, FogColor(palette.Hazard, dimmed), false, 2f);
                    break;

                case MazeFeatureType.DungeonEntrance:
                    // A dark arch set into stone — the maw the whole town exists around.
                    DrawRect(new Rect2(center + new Vector2(-14, -14), new Vector2(28, 28)),
                        new Color(0.22f, 0.23f, 0.26f));
                    DrawCircle(center + new Vector2(0, 3), 9f, new Color(0.05f, 0.05f, 0.08f));
                    DrawRect(new Rect2(center + new Vector2(-9, 3), new Vector2(18, 11)),
                        new Color(0.05f, 0.05f, 0.08f));
                    DrawPoiLabel(center, "DUNGEON", palette.Exit);
                    break;

                case MazeFeatureType.MineEntrance:
                    // A timber-framed adit in a rock mound.
                    DrawCircle(center + new Vector2(0, 2), 11f, new Color(0.33f, 0.29f, 0.25f));
                    DrawRect(new Rect2(center + new Vector2(-6, -2), new Vector2(12, 13)),
                        new Color(0.08f, 0.06f, 0.05f));
                    DrawLine(center + new Vector2(-7, -3), center + new Vector2(7, -3),
                        new Color(0.45f, 0.34f, 0.2f), 3f);
                    DrawPoiLabel(center, "MINE", new Color(0.72f, 0.6f, 0.45f));
                    break;

                case MazeFeatureType.Smithy:
                    // Anvil silhouette with an ember glow.
                    DrawRect(new Rect2(center + new Vector2(-10, -2), new Vector2(20, 6)),
                        new Color(0.28f, 0.28f, 0.31f));
                    DrawRect(new Rect2(center + new Vector2(-4, 4), new Vector2(8, 6)),
                        new Color(0.24f, 0.24f, 0.27f));
                    DrawCircle(center + new Vector2(8, -8), 3f, new Color(0.95f, 0.45f, 0.15f));
                    DrawPoiLabel(center, "SMITHY", new Color(0.93f, 0.55f, 0.25f));
                    break;

                case MazeFeatureType.Stall:
                    // Striped awning over a counter.
                    for (int i = 0; i < 4; i++)
                        DrawRect(new Rect2(center + new Vector2(-12 + i * 6, -10), new Vector2(6, 5)),
                            i % 2 == 0 ? new Color(0.82f, 0.68f, 0.3f) : new Color(0.55f, 0.2f, 0.18f));
                    DrawRect(new Rect2(center + new Vector2(-10, -4), new Vector2(20, 8)),
                        new Color(0.42f, 0.3f, 0.18f));
                    DrawPoiLabel(center, "STALL", palette.Chest);
                    break;

                case MazeFeatureType.Lamp:
                    // Iron post with a warm lantern head (the actual light pool is TownLighting's job).
                    DrawLine(center + new Vector2(0, 11), center + new Vector2(0, -9),
                        new Color(0.23f, 0.23f, 0.25f), 2.5f);
                    DrawCircle(center + new Vector2(0, -12), 3.5f, new Color(1f, 0.82f, 0.48f));
                    DrawLine(center + new Vector2(-4, -15), center + new Vector2(4, -15),
                        new Color(0.23f, 0.23f, 0.25f), 1.6f);
                    break;

                default:
                    DrawCircle(center, 10f, FogColor(palette.Arcane, dimmed), false, 3f);
                    break;
            }
        }
    }

    private void DrawEnemies(GameState state, Palette palette)
    {
        foreach (Enemy enemy in state.Enemies)
        {
            if (!IsCellVisibleCached((int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y)))
                continue;
            Vector2 center = WorldToPixel(enemy.X + enemy.AnimationOffsetX, enemy.Y + enemy.AnimationOffsetY);
            Color body = enemy.IsAlive ? palette.Enemy : palette.Corpse;
            float radius = enemy.IsBoss ? 17f : enemy.IsElite ? 14f : 12f;
            if (!enemy.IsAlive && ReferenceEquals(enemy, HoveredCorpse))
                DrawInteractionGlow(center, radius + 2f);
            DrawCircle(center, radius, body);
            DrawCircle(center, radius, enemy.InCombat ? palette.Hazard : palette.EnemyEdge, false, 2f);

            if (enemy.IsAlive && enemy.Hp < enemy.MaxHp)
            {
                float fraction = Mathf.Clamp(enemy.Hp / (float)enemy.MaxHp, 0f, 1f);
                Rect2 back = new(center + new Vector2(-15, -21), new Vector2(30, 3));
                DrawRect(back, palette.HealthBack);
                DrawRect(new Rect2(back.Position, new Vector2(back.Size.X * fraction, back.Size.Y)), palette.Health);
            }
        }
    }

    // Ambient critters (Core-simulated when GameState.EnableAmbience is on): dungeon rats dart
    // and flee, bats drift and bob; the dog/cat cases wait for the overworld scene. Same
    // visibility rule as enemies — only currently-visible cells. Colors match the Avalonia
    // renderer; sizes scaled for the 48px cell.
    private void DrawCritters(GameState state)
    {
        foreach (Critter critter in state.Critters)
        {
            if (!IsCellVisibleCached((int)MathF.Round(critter.X), (int)MathF.Round(critter.Y)))
                continue;
            Vector2 center = WorldToPixel(critter.X, critter.Y);
            switch (critter.Kind)
            {
                case CritterKind.Rat:
                    var ratBody = new Color(0.33f, 0.29f, 0.26f);
                    DrawCircle(center + new Vector2(0f, 3f), 3.5f, ratBody);
                    DrawLine(center + new Vector2(-3.5f, 3f), center + new Vector2(-8f, 4.5f),
                        new Color(0.47f, 0.4f, 0.36f), 1f);
                    break;
                case CritterKind.Bat:
                    float flap = MathF.Sin((critter.X + critter.Y) * 9f) * 2.2f;
                    Vector2 batCenter = center + new Vector2(0f, -9f + MathF.Sin((critter.X - critter.Y) * 6f) * 2.2f);
                    var wing = new Color(0.23f, 0.2f, 0.27f);
                    DrawLine(batCenter + new Vector2(-4.5f, -flap), batCenter, wing, 1.6f);
                    DrawLine(batCenter, batCenter + new Vector2(4.5f, -flap), wing, 1.6f);
                    break;
                case CritterKind.Dog:
                    var dogBody = new Color(0.55f, 0.35f, 0.17f);
                    DrawRect(new Rect2(center + new Vector2(-6f, -1.5f), new Vector2(11f, 6f)), dogBody);
                    DrawCircle(center + new Vector2(6.5f, -0.5f), 3f, dogBody);
                    DrawLine(center + new Vector2(-6f, 0f), center + new Vector2(-9f, -4f), dogBody, 1.5f);
                    break;
                case CritterKind.Cat:
                    var catBody = new Color(0.43f, 0.43f, 0.46f);
                    DrawRect(new Rect2(center + new Vector2(-4.5f, 0f), new Vector2(8f, 4.5f)), catBody);
                    DrawCircle(center + new Vector2(4.5f, 1f), 2.2f, catBody);
                    DrawLine(center + new Vector2(-4.5f, 1.5f), center + new Vector2(-7.5f, -2f), catBody, 1.2f);
                    break;
            }
        }
    }

    // Rising, fading combat feedback ("14", "Dodge!", "LEVEL UP!") — Core spawns these at every
    // damage/dodge/level-up beat; colors match the Avalonia renderer.
    private void DrawFloatingTexts(GameState state)
    {
        foreach (FloatingText text in state.FloatingTexts)
        {
            if (!IsCellVisibleCached((int)MathF.Round(text.X), (int)MathF.Round(text.Y)))
                continue;
            float life = text.LifeT; // 1 → 0 over lifetime
            float alpha = life < 0.35f ? life / 0.35f : 1f;
            (Color color, int size) = text.Kind switch
            {
                FloatingTextKind.HeroDamage => (new Color(1f, 0.33f, 0.33f), 14),
                FloatingTextKind.Dodge => (new Color(0.4f, 0.8f, 1f), 11),
                FloatingTextKind.LevelUp => (new Color(0.4f, 0.87f, 0.4f), 15),
                FloatingTextKind.Heal => (new Color(0.45f, 0.9f, 0.5f), 13),
                _ => (new Color(1f, 0.93f, 0.8f), 12) // EnemyDamage
            };
            Vector2 anchor = WorldToPixel(text.X, text.Y) +
                new Vector2(-60f, -(1f - life) * 20f);
            DrawString(ThemeDB.FallbackFont, anchor + new Vector2(1f, 1f), text.Text,
                HorizontalAlignment.Center, 120f, size, new Color(0f, 0f, 0f, alpha * 0.8f));
            DrawString(ThemeDB.FallbackFont, anchor, text.Text,
                HorizontalAlignment.Center, 120f, size, color with { A = alpha });
        }
    }

    private void DrawProjectiles(GameState state, Palette palette)
    {
        foreach (Projectile projectile in state.Projectiles)
        {
            if (!projectile.IsActive ||
                !IsCellVisibleCached((int)MathF.Round(projectile.CurrentX), (int)MathF.Round(projectile.CurrentY)))
                continue;
            DrawCircle(WorldToPixel(projectile.CurrentX, projectile.CurrentY), 5f, palette.Projectile);
        }
    }

    private void DrawHero(GameState state, Palette palette)
    {
        Vector2 center = WorldToPixel(
            state.Hero.X + state.Hero.AnimationOffsetX,
            state.Hero.Y + state.Hero.AnimationOffsetY);
        DrawCircle(center, 14f, palette.Hero);
        DrawCircle(center, 14f, state.Hero.InCombat ? palette.Hazard : palette.HeroEdge, false, 3f);
        DrawRect(new Rect2(center + new Vector2(-4, -5), new Vector2(8, 10)), palette.HeroMark);
    }

    private void DrawPoiLabel(Vector2 center, string text, Color color)
    {
        DrawString(ThemeDB.FallbackFont, center + new Vector2(-39, 27), text,
            HorizontalAlignment.Center, 80, 10, new Color(0f, 0f, 0f, 0.75f));
        DrawString(ThemeDB.FallbackFont, center + new Vector2(-40, 26), text,
            HorizontalAlignment.Center, 80, 10, color);
    }

    private void DrawInteractionGlow(Rect2 bounds)
    {
        Color glow = InteractionGlowColor();
        DrawRect(bounds.Grow(8f), glow with { A = glow.A * 0.12f }, false, 4f);
        DrawRect(bounds.Grow(5f), glow with { A = glow.A * 0.28f }, false, 3f);
        DrawRect(bounds.Grow(2f), glow, false, 2.5f);
    }

    private void DrawInteractionGlow(Vector2 center, float radius)
    {
        Color glow = InteractionGlowColor();
        DrawCircle(center, radius + 8f, glow with { A = glow.A * 0.12f }, false, 4f);
        DrawCircle(center, radius + 5f, glow with { A = glow.A * 0.28f }, false, 3f);
        DrawCircle(center, radius + 2f, glow, false, 2.5f);
    }

    private static Color InteractionGlowColor()
    {
        float phase = (float)Time.GetTicksMsec() / 180f;
        float alpha = 0.72f + MathF.Sin(phase) * 0.16f;
        return new Color(0.58f, 0.91f, 1f, alpha);
    }

    private static bool IsWalkable(Maze maze, int x, int y) =>
        x >= 0 && y >= 0 && x < maze.Width && y < maze.Height && !maze.Walls[x, y];

    private readonly record struct Palette(
        Color Wall,
        Color WallInset,
        Color WallEdge,
        Color Floor,
        Color FloorVariant,
        Color Corridor,
        Color Doorway,
        Color DoorwayEdge,
        Color Grout,
        Color Prop,
        Color Bone,
        Color Fire,
        Color Arcane,
        Color Hazard,
        Color Exit,
        Color Chest,
        Color ChestEdge,
        Color Enemy,
        Color EnemyEdge,
        Color Corpse,
        Color Projectile,
        Color Hero,
        Color HeroEdge,
        Color HeroMark,
        Color HealthBack,
        Color Health)
    {
        /// <summary>The town: grass and packed-earth paths under open sky (night is TownLighting's job).</summary>
        public static Palette Town { get; } = Build(
            new Color(0.25f, 0.26f, 0.28f),   // walls: town stone
            new Color(0.20f, 0.29f, 0.16f),   // floor: grass
            new Color(0.30f, 0.27f, 0.18f),   // corridor (unused in town, earth tone)
            new Color(0.85f, 0.72f, 0.35f));  // accent: warm gold

        public static Palette For(DungeonTheme? theme) => theme switch
        {
            DungeonTheme.Sewer => Build(new Color(0.13f, 0.2f, 0.17f), new Color(0.29f, 0.39f, 0.29f), new Color(0.17f, 0.29f, 0.25f), new Color(0.45f, 0.62f, 0.35f)),
            DungeonTheme.Cemetery => Build(new Color(0.18f, 0.18f, 0.21f), new Color(0.34f, 0.35f, 0.39f), new Color(0.25f, 0.25f, 0.3f), new Color(0.48f, 0.58f, 0.62f)),
            DungeonTheme.Library => Build(new Color(0.2f, 0.14f, 0.16f), new Color(0.42f, 0.29f, 0.23f), new Color(0.29f, 0.2f, 0.22f), new Color(0.72f, 0.56f, 0.3f)),
            DungeonTheme.Forge => Build(new Color(0.2f, 0.16f, 0.14f), new Color(0.4f, 0.31f, 0.25f), new Color(0.3f, 0.23f, 0.2f), new Color(0.87f, 0.35f, 0.15f)),
            DungeonTheme.Hideout => Build(new Color(0.16f, 0.17f, 0.15f), new Color(0.36f, 0.34f, 0.25f), new Color(0.25f, 0.25f, 0.2f), new Color(0.66f, 0.53f, 0.28f)),
            _ => Build(new Color(0.14f, 0.16f, 0.19f), new Color(0.32f, 0.35f, 0.4f), new Color(0.22f, 0.25f, 0.3f), new Color(0.56f, 0.64f, 0.72f))
        };

        private static Palette Build(Color wall, Color floor, Color corridor, Color accent) => new(
            wall,
            wall.Lightened(0.08f),
            accent.Darkened(0.2f),
            floor,
            floor.Lightened(0.035f),
            corridor,
            floor.Lightened(0.1f),
            accent,
            floor.Darkened(0.16f),
            new Color(0.43f, 0.31f, 0.2f),
            new Color(0.72f, 0.69f, 0.58f),
            new Color(0.93f, 0.39f, 0.14f),
            new Color(0.38f, 0.72f, 0.78f),
            new Color(0.94f, 0.25f, 0.2f),
            accent.Lightened(0.25f),
            new Color(0.61f, 0.39f, 0.13f),
            new Color(0.93f, 0.72f, 0.25f),
            new Color(0.68f, 0.24f, 0.22f),
            new Color(0.87f, 0.52f, 0.32f),
            new Color(0.2f, 0.18f, 0.18f),
            new Color(0.91f, 0.82f, 0.31f),
            new Color(0.23f, 0.63f, 0.75f),
            new Color(0.66f, 0.88f, 0.91f),
            new Color(0.91f, 0.94f, 0.94f),
            new Color(0.15f, 0.05f, 0.05f),
            new Color(0.82f, 0.2f, 0.18f));
    }
}

using System;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// THE line-of-sight walk. GameState, CombatSystem, and MovementSystem each carried their own
/// byte-for-byte copy of this Bresenham loop — three implementations that could (and would)
/// drift. They all delegate here now.
///
/// Semantics preserved from those copies: a cell blocks the line when the blocking bitmap says an
/// actor can't stand there (walls, shut doors, water, trees, rock), and out-of-bounds cells do not
/// block. Note this is *physical* blocking, which matches projectile flight (Projectile.Update
/// collides on the same bitmap) — it is deliberately NOT TileType.BlocksSight, where water is
/// transparent; switching vision to sight semantics is note 04 open-terrain work, because it has
/// to move projectiles, fog, and perception together.
/// </summary>
public static class SightLine
{
    /// <summary>True when no blocking tile lies on the straight cell-line between the two
    /// positions. Integer coordinates are cell centers, consistent with entity GridX/GridY.
    ///
    /// Diagonal steps respect corners: when the walk moves on both axes at once it is squeezing
    /// between two cells, and if both are solid the line is pinched off. Without this, sight (and
    /// with it attacks and perception) leaked through the zero-width gap where two wall blocks
    /// meet corner-to-corner — most visibly at room corners a corridor passed close to.</summary>
    public static bool Clear(Maze maze, float x1, float y1, float x2, float y2)
    {
        int startX = (int)MathF.Round(x1);
        int startY = (int)MathF.Round(y1);
        int endX = (int)MathF.Round(x2);
        int endY = (int)MathF.Round(y2);

        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        int sx = startX < endX ? 1 : -1;
        int sy = startY < endY ? 1 : -1;
        int err = dx - dy;

        int currentX = startX;
        int currentY = startY;

        bool Blocked(int x, int y) =>
            x >= 0 && x < maze.Width && y >= 0 && y < maze.Height && maze.Walls[x, y];

        while (true)
        {
            if (Blocked(currentX, currentY)) return false;

            if (currentX == endX && currentY == endY) break;

            int err2 = 2 * err;
            bool stepX = err2 > -dy;
            bool stepY = err2 < dx;
            if (stepX && stepY &&
                Blocked(currentX + sx, currentY) && Blocked(currentX, currentY + sy))
            {
                return false;
            }
            if (stepX)
            {
                err -= dy;
                currentX += sx;
            }
            if (stepY)
            {
                err += dx;
                currentY += sy;
            }
        }

        return true;
    }
}

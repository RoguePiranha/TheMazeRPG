using System;
using System.Linq;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Simple Perlin noise implementation for smooth random values
/// </summary>
public class PerlinNoise
{
    private readonly int[] _permutation;
    
    public PerlinNoise(int seed)
    {
        var random = new Random(seed);
        _permutation = new int[512];
        var p = Enumerable.Range(0, 256).OrderBy(_ => random.Next()).ToArray();
        
        for (int i = 0; i < 512; i++)
        {
            _permutation[i] = p[i % 256];
        }
    }
    
    /// <summary>
    /// Get 2D Perlin noise value at (x, y)
    /// Returns value between -1 and 1
    /// </summary>
    public double Noise(double x, double y)
    {
        int xi = (int)Math.Floor(x) & 255;
        int yi = (int)Math.Floor(y) & 255;
        
        double xf = x - Math.Floor(x);
        double yf = y - Math.Floor(y);
        
        double u = Fade(xf);
        double v = Fade(yf);
        
        int aa = _permutation[_permutation[xi] + yi];
        int ab = _permutation[_permutation[xi] + yi + 1];
        int ba = _permutation[_permutation[xi + 1] + yi];
        int bb = _permutation[_permutation[xi + 1] + yi + 1];
        
        double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
        
        return Lerp(x1, x2, v);
    }
    
    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    
    private static double Lerp(double a, double b, double t) => a + t * (b - a);
    
    private static double Grad(int hash, double x, double y)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y;
        double v = h < 4 ? y : h == 12 || h == 14 ? x : 0;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

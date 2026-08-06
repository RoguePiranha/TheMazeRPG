using System;
using System.IO;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// Resolves content and writable save paths for whichever frontend hosts the game simulation.
/// The desktop frontend keeps its historical working-directory behavior; other hosts configure
/// absolute roots once, before constructing a GameState or loading static content services.
/// </summary>
public static class GamePaths
{
    private static string _contentRoot = Directory.GetCurrentDirectory();
    private static string _saveRoot = Directory.GetCurrentDirectory();

    public static string ContentRoot => _contentRoot;
    public static string SaveRoot => _saveRoot;

    public static void Configure(string contentRoot, string saveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);

        _contentRoot = Path.GetFullPath(contentRoot);
        _saveRoot = Path.GetFullPath(saveRoot);

        // Anything cached against the old roots is now wrong. Without this, a settings/config
        // touch that happened before Configure silently pinned defaults from the wrong
        // directory with no way to recover.
        GameSettings.Invalidate();
        WorldService.InvalidateCaches();
    }

    public static string Content(params string[] segments) => Combine(_contentRoot, segments);
    public static string Save(params string[] segments) => Combine(_saveRoot, segments);

    private static string Combine(string root, string[] segments)
    {
        string result = root;
        foreach (string segment in segments)
            result = Path.Combine(result, segment);
        return result;
    }
}

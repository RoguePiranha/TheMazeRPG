using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>One attack effect definition from Data/Sprites/attackfx.json. Art is authored at
/// 16 pixels per world tile (the Solaria scale); clients derive their own zoom from their cell
/// size, which is why no pixel scale is stored here.</summary>
public sealed class AttackFxDef
{
    /// <summary>"swing" (weapon sheet rotated to the aim, anchored on the attacker),
    /// "projectile" (sprite rotated to the heading, drawn at the current position).</summary>
    public string Kind { get; set; } = "";
    public string Asset { get; set; } = "";
    public int FrameSize { get; set; } = 16;
    public int Columns { get; set; } = 1;
    public int Row { get; set; }
    /// <summary>The direction the art faces at rest, degrees — 90 = screen-south, the Solaria
    /// authoring convention. Clients rotate by (aim - this).</summary>
    public float BaseAngleDeg { get; set; } = 90f;
    public string? ImpactAsset { get; set; }
    public int ImpactFrameSize { get; set; } = 48;
    public int ImpactColumns { get; set; } = 4;
}

/// <summary>
/// The client-agnostic half of the attack-effect pipeline (owner request 2026-08-06: attacks
/// should read as real effects, not a yellow dot): the VisualStyle → sheet mapping, loaded once
/// from Data/Sprites/attackfx.json. Each client renders these with its own texture machinery —
/// Avalonia's AttackFxService via Skia/avares, the Godot DungeonView via ImageTexture — so the
/// mapping is authored exactly once. Styles absent from the manifest keep their procedural
/// rendering; a missing or broken manifest can never take attacks off the screen.
/// </summary>
public static class AttackFxCatalog
{
    private sealed class Manifest
    {
        public Dictionary<string, AttackFxDef> Effects { get; set; } = new();
    }

    private static string ManifestPath
    {
        get
        {
            string configured = GamePaths.Content("Data", "Sprites", "attackfx.json");
            return File.Exists(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "Data", "Sprites", "attackfx.json");
        }
    }

    private static readonly Dictionary<VisualStyle, AttackFxDef> Defs = Load();

    private static Dictionary<VisualStyle, AttackFxDef> Load()
    {
        var map = new Dictionary<VisualStyle, AttackFxDef>();
        try
        {
            if (!File.Exists(ManifestPath))
            {
                GameLog.Debug($"AttackFxCatalog: no manifest at {ManifestPath}; attacks stay procedural.");
                return map;
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            foreach (var (styleName, def) in manifest?.Effects ?? new Dictionary<string, AttackFxDef>())
            {
                if (Enum.TryParse<VisualStyle>(styleName, ignoreCase: true, out var style))
                    map[style] = def;
                else
                    GameLog.Debug($"AttackFxCatalog: unknown VisualStyle '{styleName}' in manifest.");
            }
            GameLog.Debug($"AttackFxCatalog: loaded {map.Count} attack effect mapping(s).");
        }
        catch (Exception ex)
        {
            GameLog.Debug($"AttackFxCatalog: failed to read {ManifestPath} ({ex.Message}); attacks stay procedural.");
        }
        return map;
    }

    public static bool TryGet(VisualStyle style, out AttackFxDef fx) => Defs.TryGetValue(style, out fx!);

    /// <summary>Manifest inventory, for the TEST_SPRITES catalog check.</summary>
    public static IReadOnlyDictionary<VisualStyle, AttackFxDef> All => Defs;
}

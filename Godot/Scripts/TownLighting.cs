using System;
using System.Collections.Generic;
using Godot;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.GodotClient;

/// <summary>
/// Town night lighting via real Godot 2D lights (owner ruling: night should be night, lit in
/// pools). The Avalonia renderer's hand-punched Skia gradients are the behavioral spec — hero
/// light radius from Core (darkvision = full range, torch = pool, bare eyes = arm's reach),
/// street lamps and forge embers as fixtures, fire flickering out of phase per fixture, and the
/// Night Sight skill thinning the whole dark layer. CanvasModulate darkens the world canvas only
/// (the UI lives on a CanvasLayer); PointLight2D punches the pools back out.
/// </summary>
public partial class TownLighting : Node2D
{
    private static readonly Color NightColor = new(0.16f, 0.20f, 0.34f);

    private CanvasModulate _modulate = null!;
    private PointLight2D _heroLight = null!;
    private readonly List<PointLight2D> _fixtures = new();
    private readonly List<(float x, float y, bool isForge)> _fixtureSpecs = new();
    private Maze? _fixtureMaze;
    private static Texture2D? _lightTexture;

    public override void _Ready()
    {
        _modulate = new CanvasModulate { Color = Colors.White };
        AddChild(_modulate);
        _heroLight = MakeLight();
        AddChild(_heroLight);
    }

    /// <summary>Called every frame from GameHost with the live state.</summary>
    public void Sync(GameState state)
    {
        float darkness = state.IsInOverworld ? state.Clock.Darkness : 0f;
        // Night Sight thins the entire layer — dark becomes dusk everywhere (matches the
        // Avalonia 215 → 85 max-alpha rule, expressed as modulate strength).
        if (state.HasNightSight) darkness *= 0.4f;

        _modulate.Color = Colors.White.Lerp(NightColor, darkness);

        bool lightsOn = darkness > 0.05f;
        RebuildFixturesIfNeeded(state);

        _heroLight.Visible = lightsOn;
        if (lightsOn)
        {
            _heroLight.Position = DungeonView.WorldToPixel(state.Hero.X, state.Hero.Y);
            SetLightRadius(_heroLight, state.HeroLightRadius * DungeonView.CellSize);
            // A carried torch is open flame; darkvision and bare eyesight hold steady.
            bool fire = state.HasTorch && !state.Hero.HasDarkvision;
            _heroLight.Energy = darkness * (fire ? Flicker(0f, 0.08f) : 1f);
        }

        for (int i = 0; i < _fixtures.Count; i++)
        {
            var light = _fixtures[i];
            var (fx, fy, isForge) = _fixtureSpecs[i];
            light.Visible = lightsOn;
            if (!lightsOn) continue;
            float phase = fx * 7.31f + fy * 13.77f;
            // Glass-caged lamps sway gently; open forge embers dance harder.
            light.Energy = darkness * Flicker(phase, isForge ? 0.09f : 0.035f);
        }
    }

    private void RebuildFixturesIfNeeded(GameState state)
    {
        if (ReferenceEquals(_fixtureMaze, state.CurrentMaze)) return;
        _fixtureMaze = state.CurrentMaze;
        foreach (var light in _fixtures) light.QueueFree();
        _fixtures.Clear();
        _fixtureSpecs.Clear();
        if (!state.IsInOverworld) return;

        foreach (MazeFeature feature in state.CurrentMaze.Features)
        {
            bool lamp = feature.Type == MazeFeatureType.Lamp;
            bool forge = feature.Type == MazeFeatureType.Smithy;
            if (!lamp && !forge) continue;
            var light = MakeLight();
            light.Position = DungeonView.WorldToPixel(feature.X, feature.Y) +
                (lamp ? new Vector2(0f, -15f) : Vector2.Zero); // lamps glow from the lantern head
            light.Color = new Color(1f, 0.78f, 0.5f);          // warm firelight
            SetLightRadius(light, (lamp ? 3.8f : 2.4f) * DungeonView.CellSize);
            AddChild(light);
            _fixtures.Add(light);
            _fixtureSpecs.Add((feature.X, feature.Y, forge));
        }
    }

    /// <summary>Slow wander + faster dance, phase-offset per light so the town never pulses in
    /// sync — the same feel as the Avalonia flicker, driven by the render clock (keeps moving
    /// while the sim is paused; never touches sim state).</summary>
    private static float Flicker(float phase, float amount)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float fast = MathF.Sin(t * 9.3f + phase) * 0.6f + MathF.Sin(t * 23.7f + phase * 1.7f) * 0.25f;
        float slow = MathF.Sin(t * 1.6f + phase * 0.5f);
        return 1f + amount * (0.6f * fast + 0.4f * slow);
    }

    private static PointLight2D MakeLight() => new()
    {
        Texture = LightTexture(),
        Visible = false,
        BlendMode = Light2D.BlendModeEnum.Add,
        ShadowEnabled = false
    };

    private static void SetLightRadius(PointLight2D light, float radiusPixels)
    {
        float textureHalf = LightTexture().GetWidth() / 2f;
        light.TextureScale = radiusPixels / textureHalf;
    }

    /// <summary>Radial falloff texture matching the Avalonia gradient (bright core, long soft
    /// tail — stops at 0/0.3/0.5/0.68/0.84/1), built once in code so no imported asset is needed.</summary>
    private static Texture2D LightTexture()
    {
        if (_lightTexture != null) return _lightTexture;
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 1f, 1f, 1f));
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));
        gradient.AddPoint(0.3f, new Color(1f, 1f, 1f, 0.92f));
        gradient.AddPoint(0.5f, new Color(1f, 1f, 1f, 0.72f));
        gradient.AddPoint(0.68f, new Color(1f, 1f, 1f, 0.45f));
        gradient.AddPoint(0.84f, new Color(1f, 1f, 1f, 0.20f));
        _lightTexture = new GradientTexture2D
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f)
        };
        return _lightTexture;
    }
}

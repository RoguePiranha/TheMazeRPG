using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace TheMazeRPG.Views;

/// <summary>
/// Overlay panel that shows hero stats and inventory
/// </summary>
public class StatsOverlay : Control
{
    private bool _isVisible;
    private GameState? _gameState;
    private Point _lastMousePosition;
    private readonly Dictionary<string, (Rect bounds, string description)> _statHoverAreas = new();

    // Clickable "+" spend buttons for manual stat allocation, in panel-local coordinates, plus the
    // panel's screen origin so a pointer press (which arrives in control coordinates) can be mapped
    // into that local space. Populated during Render only while there are unspent points.
    private readonly List<(Rect bounds, string stat)> _statPlusAreas = new();
    private double _panelOriginX;
    private double _panelOriginY;

    // The game's pixel font (same file the XAML side references) for all overlay text.
    private static readonly FontFamily GameFontFamily =
        new("avares://TheMazeRPG/Assets/Fonts#Odderf Basic");

    // For the bestiary "discovered/total" denominator: every "{Race} {Class}" combo an enemy can
    // spawn as. Derived from the loaded content data so it stays correct as races/classes grow.
    private static readonly int TotalSpeciesCount = ComputeTotalSpeciesCount();
    private static int ComputeTotalSpeciesCount()
    {
        var cds = new CharacterDataService();
        int spawnableRaces = cds.Races.Count(kv => !kv.Value.Debug);
        return spawnableRaces * cds.Classes.Count;
    }
    
    public static readonly StyledProperty<string> HeroNameProperty =
        AvaloniaProperty.Register<StatsOverlay, string>(nameof(HeroName), "Hero");
    
    public static readonly StyledProperty<string> HeroClassProperty =
        AvaloniaProperty.Register<StatsOverlay, string>(nameof(HeroClass), "Wanderer");
    
    public static readonly StyledProperty<int> LevelProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Level), 1);
    
    public static readonly StyledProperty<int> StrengthProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Strength), 1);
    
    public static readonly StyledProperty<int> ConstitutionProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Constitution), 1);
    
    public static readonly StyledProperty<int> AgilityProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Agility), 1);
    
    public static readonly StyledProperty<int> DexterityProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Dexterity), 1);
    
    public static readonly StyledProperty<int> IntelligenceProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Intelligence), 1);
    
    public static readonly StyledProperty<int> WisdomProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Wisdom), 1);
    
    public static readonly StyledProperty<int> CharismaProperty =
        AvaloniaProperty.Register<StatsOverlay, int>(nameof(Charisma), 1);
    
    public string HeroName
    {
        get => GetValue(HeroNameProperty);
        set => SetValue(HeroNameProperty, value);
    }
    
    public string HeroClass
    {
        get => GetValue(HeroClassProperty);
        set => SetValue(HeroClassProperty, value);
    }
    
    public int Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }
    
    public int Strength
    {
        get => GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }
    
    public int Constitution
    {
        get => GetValue(ConstitutionProperty);
        set => SetValue(ConstitutionProperty, value);
    }
    
    public int Agility
    {
        get => GetValue(AgilityProperty);
        set => SetValue(AgilityProperty, value);
    }
    
    public int Dexterity
    {
        get => GetValue(DexterityProperty);
        set => SetValue(DexterityProperty, value);
    }
    
    public int Intelligence
    {
        get => GetValue(IntelligenceProperty);
        set => SetValue(IntelligenceProperty, value);
    }
    
    public int Wisdom
    {
        get => GetValue(WisdomProperty);
        set => SetValue(WisdomProperty, value);
    }
    
    public int Charisma
    {
        get => GetValue(CharismaProperty);
        set => SetValue(CharismaProperty, value);
    }
    
    public bool IsOverlayVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            IsHitTestVisible = value; // Only intercept input when visible
            if (value)
            {
                PointerMoved += OnPointerMoved;
                PointerPressed += OnPointerPressed;
            }
            else
            {
                PointerMoved -= OnPointerMoved;
                PointerPressed -= OnPointerPressed;
            }
            InvalidateVisual();
        }
    }
    
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        InvalidateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_gameState == null) return;
        // Map the press into the panel's local space (Render draws under a translate transform).
        var local = new Point(e.GetPosition(this).X - _panelOriginX, e.GetPosition(this).Y - _panelOriginY);
        foreach (var (bounds, stat) in _statPlusAreas)
        {
            if (bounds.Contains(local))
            {
                if (_gameState.SpendStatPoint(stat))
                {
                    UpdateStats();
                    InvalidateVisual();
                }
                e.Handled = true;
                return;
            }
        }
    }
    
    public void SetGameState(GameState gameState)
    {
        _gameState = gameState;
        // Start transparent to input so clicks reach the game canvas beneath (click-to-fire);
        // IsOverlayVisible flips this on only while the stats panel is actually shown.
        IsHitTestVisible = false;
        UpdateStats();
    }
    
    private void UpdateStats()
    {
        if (_gameState?.Hero == null) return;
        
        try
        {
            HeroName = _gameState.Hero.Name ?? "Hero";
            HeroClass = _gameState.Hero.Class ?? "Wanderer";
            Level = _gameState.Hero.Level;
            Strength = _gameState.Hero.Strength;
            Constitution = _gameState.Hero.Constitution;
            Agility = _gameState.Hero.Agility;
            Dexterity = _gameState.Hero.Dexterity;
            Intelligence = _gameState.Hero.Intelligence;
            Wisdom = _gameState.Hero.Wisdom;
            Charisma = _gameState.Hero.Charisma;
            
            InvalidateVisual();
        }
        catch
        {
            // Silently ignore update errors
        }
    }
    
    public override void Render(DrawingContext context)
    {
        if (!IsOverlayVisible) return;
        
        // Ensure we have valid bounds
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        
        // Update stats before rendering
        if (_gameState?.Hero != null)
        {
            UpdateStats();
        }
        
        try
        {
            _statHoverAreas.Clear();
            _statPlusAreas.Clear();

            // Dim the whole screen, then draw a centered, fixed-size modal panel (not full-screen).
            context.FillRectangle(
                new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                new Rect(0, 0, Bounds.Width, Bounds.Height));

            double panelW = System.Math.Min(760, System.Math.Max(320, Bounds.Width - 40));
            double panelH = System.Math.Min(600, System.Math.Max(240, Bounds.Height - 40));
            double originX = (Bounds.Width - panelW) / 2;
            double originY = (Bounds.Height - panelH) / 2;
            // Remember the origin so OnPointerPressed can map screen clicks into panel-local space.
            _panelOriginX = originX;
            _panelOriginY = originY;
            int unspent = _gameState?.Hero?.UnspentStatPoints ?? 0;
            // Mouse position relative to the panel, for hover hit-testing under the transform below.
            var localMouse = new Point(_lastMousePosition.X - originX, _lastMousePosition.Y - originY);

            // Draw everything below relative to the panel's top-left corner.
            using var _panelXform = context.PushTransform(Matrix.CreateTranslation(originX, originY));

            var panelRect = new Rect(0, 0, panelW, panelH);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), panelRect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)), 2), panelRect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x55, 0x00)), 1), panelRect.Deflate(3));

            // Keep content inside the panel so a long inventory/affinity list can't spill over the border.
            using var _panelClip = context.PushClip(panelRect.Deflate(6));

        var textBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        var headerBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00));
        var statLabelBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

        var headerFont = new Typeface(GameFontFamily, FontStyle.Normal, FontWeight.Bold);
        var normalFont = new Typeface(GameFontFamily);
        
        double x = 28;
        double y = 30;
        
        // Header: Name, Class, Level
        var headerText = new FormattedText(
            $"{HeroName} - Level {Level} {HeroClass}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            headerFont,
            24,
            headerBrush);
        
        context.DrawText(headerText, new Point(x, y));
        y += 50;
        
        // Stats Section (with the unspent-points prompt when there are points to assign)
        var sectionText = new FormattedText(
            "STATS",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            headerFont,
            18,
            headerBrush);
        context.DrawText(sectionText, new Point(x, y));
        if (unspent > 0)
        {
            var pointsText = new FormattedText(
                $"{unspent} point{(unspent == 1 ? "" : "s")} to spend",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                normalFont, 13, new SolidColorBrush(Color.FromRgb(120, 220, 130)));
            context.DrawText(pointsText, new Point(x + 70, y + 4));
        }
        y += 35;

        // Draw each stat (a "+" spend button appears next to each while points remain)
        bool canSpend = unspent > 0;
        DrawStat(context, "Strength", Strength, "Melee Damage, Carry Limits, Knockback", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Constitution", Constitution, "Health, Defense, Resistances", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Agility", Agility, "Movement Speed, Dodge Rate, Stealth", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Dexterity", Dexterity, "Attack Speed, Accuracy, Crit Rate", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Intelligence", Intelligence, "Magic Damage, Spell Cooldown, Mana", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Wisdom", Wisdom, "Magic Resist, Healing, Faith", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        DrawStat(context, "Charisma", Charisma, "NPC Interaction, Follower Count", x, ref y, statLabelBrush, textBrush, normalFont, canSpend);
        
        // Draw hover tooltip if mouse is over a stat
        foreach (var kvp in _statHoverAreas)
        {
            if (kvp.Value.bounds.Contains(localMouse))
            {
                DrawTooltip(context, kvp.Value.description, localMouse, panelW, panelH);
                break;
            }
        }
        
        y += 20;
        
        // Attacks & Equipment Section (right side)
        double invX = panelW / 2 + 12;
        double invY = 78;
        
        var attacksSectionText = new FormattedText(
            "ATTACKS",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            headerFont,
            18,
            headerBrush);
        context.DrawText(attacksSectionText, new Point(invX, invY));
        invY += 35;
        
        // Show current attacks
        if (_gameState?.Hero?.Attacks != null && _gameState.Hero.Attacks.Count > 0)
        {
            foreach (var attack in _gameState.Hero.Attacks)
            {
                var isCurrent = attack == _gameState.Hero.CurrentAttack;
                var attackColor = isCurrent ? textBrush : statLabelBrush;
                
                var attackText = new FormattedText(
                    $"{(isCurrent ? "► " : "  ")}{attack.Name}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    normalFont,
                    14,
                    attackColor);
                context.DrawText(attackText, new Point(invX, invY));
                invY += 22;
            }
        }
        else
        {
            var noAttacksText = new FormattedText(
                "No attacks",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                normalFont,
                14,
                statLabelBrush);
            context.DrawText(noAttacksText, new Point(invX, invY));
        }

        // Inventory (found gear not equipped), rarity color-coded
        if (_gameState?.Hero?.Inventory is { Count: > 0 } inventory)
        {
            invY += 18;
            var invHeader = new FormattedText(
                "INVENTORY",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                headerFont, 18, headerBrush);
            context.DrawText(invHeader, new Point(invX, invY));
            invY += 30;
            foreach (var item in inventory)
            {
                var itemText = new FormattedText(
                    $"  {item.Name} ({item.Rarity})",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    normalFont, 13, new SolidColorBrush(RarityColor(item.Rarity)));
                context.DrawText(itemText, new Point(invX, invY));
                invY += 20;
            }
        }

        // Codex summary: cross-run stats + bestiary discovery count (minimal for now —
        // a full per-species bestiary view can come later once there's more to show).
        {
            var codex = CodexService.Instance.Data;
            invY += 18;
            var codexHeader = new FormattedText(
                "CODEX",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                headerFont, 18, headerBrush);
            context.DrawText(codexHeader, new Point(invX, invY));
            invY += 26;

            int discovered = codex.Bestiary.Count;
            var lines = new[]
            {
                $"Kills: {codex.PlayStats.TotalKills}   Deaths: {codex.PlayStats.TotalDeaths}",
                $"Deepest Floor: {codex.PlayStats.DeepestFloor}",
                $"Bestiary: {discovered}/{TotalSpeciesCount} discovered"
            };
            foreach (var line in lines)
            {
                var lineText = new FormattedText(
                    line,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    normalFont, 13, statLabelBrush);
                context.DrawText(lineText, new Point(invX, invY));
                invY += 18;
            }
        }

        // Affinities: the character's non-neutral elemental leanings, element-colored, with the
        // learnable spell tier. Neutral (baseline) elements are omitted.
        if (_gameState?.Hero?.Affinities is { } affinities)
        {
            var shown = affinities.Values
                .Where(kv => System.Math.Abs(kv.Value - Affinities.Neutral) > 0.5f)
                .OrderByDescending(kv => kv.Value)
                .ToList();
            if (shown.Count > 0)
            {
                invY += 18;
                var affHeader = new FormattedText(
                    "AFFINITIES",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    headerFont, 18, headerBrush);
                context.DrawText(affHeader, new Point(invX, invY));
                invY += 26;

                foreach (var kv in shown)
                {
                    int tier = AffinityService.LearnableTier(kv.Value);
                    var line = new FormattedText(
                        $"{kv.Key,-10} {kv.Value,5:0}   (tier {tier})",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        normalFont, 13, new SolidColorBrush(ElementColor(kv.Key)));
                    context.DrawText(line, new Point(invX, invY));
                    invY += 18;
                }
            }
        }

        // Footer hint
        var hintText = new FormattedText(
            "Press [Tab] to close",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            normalFont,
            12,
            statLabelBrush);
        context.DrawText(hintText, new Point(panelW - 160, panelH - 28));
        }
        catch
        {
            // Silently ignore rendering errors
        }
    }
    
    private void DrawStat(DrawingContext context, string label, int value, string description,
        double x, ref double y, IBrush labelBrush, IBrush valueBrush, Typeface font, bool canSpend = false)
    {
        var labelText = new FormattedText(
            $"{label}:",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            font,
            16,
            labelBrush);
        
        var valueText = new FormattedText(
            $"{value}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(font.FontFamily, FontStyle.Normal, FontWeight.Bold),
            16,
            valueBrush);
        
        context.DrawText(labelText, new Point(x, y));
        context.DrawText(valueText, new Point(x + 120, y));

        // Racial effectiveness: show the effective value inline (color-coded), full detail on hover.
        var (mult, effective) = StatEffectiveness(label);
        string tooltip = description;
        if (System.Math.Abs(mult - 1.0f) > 0.001f)
        {
            var effBrush = new SolidColorBrush(mult > 1f
                ? Color.FromRgb(120, 220, 130)   // buff
                : Color.FromRgb(230, 140, 110));  // penalty
            var effText = new FormattedText(
                $"→ {effective:0.0}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                font, 14, effBrush);
            context.DrawText(effText, new Point(x + 145, y + 1));
            tooltip = $"{description}\nRacial effectiveness: {mult * 100:0}%  (effective {effective:0.0})";
        }

        // Store hover area for this stat
        var hoverRect = new Rect(x, y, 200, 25);
        _statHoverAreas[label] = (hoverRect, tooltip);

        // Clickable "+" spend button while there are unspent points (registered for hit-testing).
        if (canSpend)
        {
            var plusRect = new Rect(x + 215, y - 1, 20, 20);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), plusRect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(120, 220, 130)), 1), plusRect);
            var plusText = new FormattedText(
                "+",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(font.FontFamily, FontStyle.Normal, FontWeight.Bold),
                16, new SolidColorBrush(Color.FromRgb(120, 220, 130)));
            context.DrawText(plusText, new Point(plusRect.X + 5, plusRect.Y + 1));
            _statPlusAreas.Add((plusRect, label));
        }

        y += 28;
    }

    /// <summary>Racial effectiveness multiplier and effective value for a stat, from the live hero.</summary>
    private (float mult, float effective) StatEffectiveness(string label)
    {
        var h = _gameState?.Hero;
        if (h == null) return (1f, 0f);
        return label switch
        {
            "Strength" => (h.StrengthEffectiveness, h.EffectiveStrength),
            "Constitution" => (h.ConstitutionEffectiveness, h.EffectiveConstitution),
            "Agility" => (h.AgilityEffectiveness, h.EffectiveAgility),
            "Dexterity" => (h.DexterityEffectiveness, h.EffectiveDexterity),
            "Intelligence" => (h.IntelligenceEffectiveness, h.EffectiveIntelligence),
            "Wisdom" => (h.WisdomEffectiveness, h.EffectiveWisdom),
            "Charisma" => (h.CharismaEffectiveness, h.EffectiveCharisma),
            _ => (1f, 0f)
        };
    }

    // Item rarity colors (Game Idea.md): Common→Mythic.
    private static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Common => Color.FromRgb(220, 220, 220),
        Rarity.Uncommon => Color.FromRgb(120, 220, 130),
        Rarity.Rare => Color.FromRgb(90, 160, 255),
        Rarity.Epic => Color.FromRgb(190, 120, 255),
        Rarity.Legendary => Color.FromRgb(255, 165, 60),
        Rarity.Mythic => Color.FromRgb(255, 215, 90),
        _ => Colors.White
    };

    // Element display colors (the "glow" tone from the renderer's magic palette).
    private static Color ElementColor(MagicElement element) => element switch
    {
        MagicElement.Mana => Color.FromRgb(150, 190, 255),
        MagicElement.Arcane => Color.FromRgb(170, 100, 255),
        MagicElement.Fire => Color.FromRgb(255, 110, 40),
        MagicElement.Ice => Color.FromRgb(150, 220, 255),
        MagicElement.Poison => Color.FromRgb(120, 255, 60),
        MagicElement.Water => Color.FromRgb(60, 130, 255),
        MagicElement.Lightning => Color.FromRgb(255, 230, 60),
        MagicElement.Life => Color.FromRgb(89, 214, 111),
        MagicElement.Death => Color.FromRgb(150, 150, 150),
        MagicElement.Light => Color.FromRgb(255, 243, 166),
        MagicElement.Shadow => Color.FromRgb(150, 90, 200),
        MagicElement.Earth => Color.FromRgb(170, 120, 60),
        MagicElement.Air => Color.FromRgb(225, 225, 220),
        MagicElement.Sonic => Color.FromRgb(100, 200, 255),
        MagicElement.Void => Color.FromRgb(120, 105, 170),
        MagicElement.Holy => Color.FromRgb(255, 210, 90),
        _ => Colors.White
    };

    private void DrawTooltip(DrawingContext context, string text, Point mousePos, double areaW, double areaH)
    {
        var tooltipFont = new Typeface(GameFontFamily);
        var tooltipText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            tooltipFont,
            12,
            new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
        
        double padding = 8;
        double tooltipWidth = tooltipText.Width + padding * 2;
        double tooltipHeight = tooltipText.Height + padding * 2;
        
        // Position tooltip near mouse, but keep it on screen
        double tooltipX = mousePos.X + 10;
        double tooltipY = mousePos.Y + 10;
        
        if (tooltipX + tooltipWidth > areaW)
            tooltipX = mousePos.X - tooltipWidth - 10;
        if (tooltipY + tooltipHeight > areaH)
            tooltipY = mousePos.Y - tooltipHeight - 10;
        
        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        
        // Draw tooltip background
        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(245, 0x11, 0x11, 0x11)),
            tooltipRect);

        // Draw tooltip border
        context.DrawRectangle(
            new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)), 1),
            tooltipRect);
        
        // Draw tooltip text
        context.DrawText(tooltipText, new Point(tooltipX + padding, tooltipY + padding));
    }
}

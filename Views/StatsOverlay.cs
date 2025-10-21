using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TheMazeRPG.Core.Services;
using System.Collections.Generic;

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
            }
            else
            {
                PointerMoved -= OnPointerMoved;
            }
            InvalidateVisual();
        }
    }
    
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        InvalidateVisual();
    }
    
    public void SetGameState(GameState gameState)
    {
        _gameState = gameState;
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
            
            // Semi-transparent dark background
            context.FillRectangle(
                new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                new Rect(0, 0, Bounds.Width, Bounds.Height));
        
        var textBrush = new SolidColorBrush(Colors.White);
        var headerBrush = new SolidColorBrush(Color.FromRgb(100, 180, 255));
        var statLabelBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        
        var headerFont = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
        var normalFont = new Typeface("Arial");
        
        double x = 40;
        double y = 40;
        
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
        
        // Stats Section
        var sectionText = new FormattedText(
            "STATS",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            headerFont,
            18,
            headerBrush);
        context.DrawText(sectionText, new Point(x, y));
        y += 35;
        
        // Draw each stat
        DrawStat(context, "Strength", Strength, "Melee Damage, Carry Limits, Knockback", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Constitution", Constitution, "Health, Defense, Resistances", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Agility", Agility, "Movement Speed, Dodge Rate, Stealth", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Dexterity", Dexterity, "Attack Speed, Accuracy, Crit Rate", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Intelligence", Intelligence, "Magic Damage, Spell Cooldown, Mana", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Wisdom", Wisdom, "Magic Resist, Healing, Faith", x, ref y, statLabelBrush, textBrush, normalFont);
        DrawStat(context, "Charisma", Charisma, "NPC Interaction, Follower Count", x, ref y, statLabelBrush, textBrush, normalFont);
        
        // Draw hover tooltip if mouse is over a stat
        foreach (var kvp in _statHoverAreas)
        {
            if (kvp.Value.bounds.Contains(_lastMousePosition))
            {
                DrawTooltip(context, kvp.Value.description, _lastMousePosition);
                break;
            }
        }
        
        y += 20;
        
        // Attacks & Equipment Section (right side)
        double invX = Bounds.Width / 2 + 20;
        double invY = 90;
        
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
        
        // Footer hint
        var hintText = new FormattedText(
            "Press [Tab] to close",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            normalFont,
            12,
            statLabelBrush);
        context.DrawText(hintText, new Point(Bounds.Width - 150, Bounds.Height - 30));
        }
        catch
        {
            // Silently ignore rendering errors
        }
    }
    
    private void DrawStat(DrawingContext context, string label, int value, string description, 
        double x, ref double y, IBrush labelBrush, IBrush valueBrush, Typeface font)
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
        
        // Store hover area for this stat
        var hoverRect = new Rect(x, y, 160, 25);
        _statHoverAreas[label] = (hoverRect, description);
        
        y += 28;
    }
    
    private void DrawTooltip(DrawingContext context, string text, Point mousePos)
    {
        var tooltipFont = new Typeface("Arial");
        var tooltipText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            tooltipFont,
            12,
            new SolidColorBrush(Colors.White));
        
        double padding = 8;
        double tooltipWidth = tooltipText.Width + padding * 2;
        double tooltipHeight = tooltipText.Height + padding * 2;
        
        // Position tooltip near mouse, but keep it on screen
        double tooltipX = mousePos.X + 10;
        double tooltipY = mousePos.Y + 10;
        
        if (tooltipX + tooltipWidth > Bounds.Width)
            tooltipX = mousePos.X - tooltipWidth - 10;
        if (tooltipY + tooltipHeight > Bounds.Height)
            tooltipY = mousePos.Y - tooltipHeight - 10;
        
        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        
        // Draw tooltip background
        context.FillRectangle(
            new SolidColorBrush(Color.FromArgb(240, 40, 40, 40)),
            tooltipRect);
        
        // Draw tooltip border
        context.DrawRectangle(
            new Pen(new SolidColorBrush(Color.FromRgb(100, 180, 255)), 1),
            tooltipRect);
        
        // Draw tooltip text
        context.DrawText(tooltipText, new Point(tooltipX + padding, tooltipY + padding));
    }
}

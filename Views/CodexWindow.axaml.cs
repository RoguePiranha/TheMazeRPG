using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Views;

/// <summary>Which Codex panel a CodexWindow shows.</summary>
public enum CodexTab
{
    Bestiary,
    PlayStats
}

/// <summary>
/// Read-only Codex viewer opened from the title menu: the bestiary (discovered "{Race} {Class}"
/// archetypes) or cross-run play stats, both fed from CodexService. A modal popup over the shell,
/// same pattern as SavesWindow.
/// </summary>
public partial class CodexWindow : Window
{
    private static readonly Color GoldColor = Color.FromRgb(0xFF, 0xCC, 0x00);
    private static readonly Color TextColor = Color.FromRgb(0xCC, 0xCC, 0xCC);
    private static readonly Color DimColor = Color.FromRgb(0x88, 0x88, 0x88);

    // Parameterless ctor for the XAML designer; real use passes a tab.
    public CodexWindow() : this(CodexTab.Bestiary) { }

    public CodexWindow(CodexTab tab)
    {
        InitializeComponent();

        var header = this.FindControl<TextBlock>("HeaderText");
        if (header != null) header.Text = tab == CodexTab.Bestiary ? "Bestiary" : "Play Stats";
        Title = tab == CodexTab.Bestiary ? "Bestiary" : "Play Stats";

        if (tab == CodexTab.Bestiary) PopulateBestiary();
        else PopulatePlayStats();
    }

    private void PopulateBestiary()
    {
        var panel = this.FindControl<StackPanel>("ContentPanel");
        if (panel == null) return;

        var entries = CodexService.Instance.Data.Bestiary.Values
            .OrderBy(e => e.FirstFloor)
            .ThenBy(e => e.Name)
            .ToList();

        if (entries.Count == 0)
        {
            panel.Children.Add(Muted("No creatures encountered yet."));
            return;
        }

        foreach (var entry in entries)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(TextColor)
            });
            string floors = entry.FirstFloor == entry.LastFloor
                ? $"Floor {entry.FirstFloor}"
                : $"Floors {entry.FirstFloor}-{entry.LastFloor}";
            row.Children.Add(new TextBlock
            {
                Text = $"Seen: {entry.Seen}    Killed: {entry.Killed}    {floors}",
                FontSize = 12,
                Foreground = new SolidColorBrush(DimColor)
            });

            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(8, 6),
                Child = row
            });
        }
    }

    private void PopulatePlayStats()
    {
        var panel = this.FindControl<StackPanel>("ContentPanel");
        if (panel == null) return;

        var ps = CodexService.Instance.Data.PlayStats;
        int discovered = CodexService.Instance.Data.Bestiary.Count;

        AddStatRow(panel, "Total Kills", ps.TotalKills.ToString());
        AddStatRow(panel, "Total Deaths", ps.TotalDeaths.ToString());
        AddStatRow(panel, "Deepest Floor", ps.DeepestFloor.ToString());
        AddStatRow(panel, "Floors Cleared", ps.TotalFloorsCleared.ToString());
        AddStatRow(panel, "Dungeon Exits", ps.TotalDungeonExits.ToString());
        AddStatRow(panel, "Creatures Discovered", discovered.ToString());
    }

    private void AddStatRow(StackPanel panel, string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(4, 3)
        };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 15,
            Foreground = new SolidColorBrush(TextColor)
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(GoldColor),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        panel.Children.Add(grid);
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = new SolidColorBrush(DimColor),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Avalonia.Thickness(0, 20)
    };

    private void BackButton_Click(object? sender, RoutedEventArgs e) => Close();
}

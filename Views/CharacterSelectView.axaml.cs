using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Views;

/// <summary>
/// Character creation view, hosted by the MainWindow shell. Raises Confirmed with the chosen
/// (name, class, race), or Cancelled to go back to the title — no window opening/closing.
/// </summary>
public partial class CharacterSelectView : UserControl
{
    /// <summary>(characterName, className, raceName)</summary>
    public event Action<string, string, string>? Confirmed;
    public event Action? Cancelled;

    private readonly CharacterDataService _characterDataService;

    public CharacterSelectView()
    {
        InitializeComponent();
        _characterDataService = new CharacterDataService();
        PopulateSelections();
    }

    private void PopulateSelections()
    {
        // Populate Races
        var raceListBox = this.FindControl<ListBox>("RaceListBox");
        if (raceListBox != null)
        {
            var raceItems = new List<SelectionItem>();
            foreach (var race in _characterDataService.Races)
            {
                raceItems.Add(new SelectionItem
                {
                    Name = race.Key,
                    Description = race.Value.Description,
                    Color = Color.Parse(race.Value.Color)
                });
            }
            raceListBox.ItemsSource = raceItems;
            raceListBox.SelectedIndex = raceItems.FindIndex(r => r.Name == "Human");
        }

        // Populate Classes
        var classListBox = this.FindControl<ListBox>("ClassListBox");
        if (classListBox != null)
        {
            var classItems = new List<SelectionItem>();
            foreach (var charClass in _characterDataService.Classes)
            {
                classItems.Add(new SelectionItem
                {
                    Name = charClass.Key,
                    Description = charClass.Value.Description,
                    Color = Color.Parse(charClass.Value.Color)
                });
            }
            classListBox.ItemsSource = classItems;
            classListBox.SelectedIndex = classItems.FindIndex(c => c.Name == "Wanderer");
        }
    }

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        var nameTextBox = this.FindControl<TextBox>("NameTextBox");
        var raceListBox = this.FindControl<ListBox>("RaceListBox");
        var classListBox = this.FindControl<ListBox>("ClassListBox");

        string characterName = "Hero";
        string selectedRace = "Human";
        string selectedClass = "Wanderer";

        if (nameTextBox != null && !string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
            characterName = nameTextBox.Text;
        }

        if (raceListBox?.SelectedItem is SelectionItem race)
        {
            selectedRace = race.Name;
        }

        if (classListBox?.SelectedItem is SelectionItem charClass)
        {
            selectedClass = charClass.Name;
        }

        Confirmed?.Invoke(characterName, selectedClass, selectedRace);
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e) =>
        Cancelled?.Invoke();

    public class SelectionItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Color Color { get; set; }

        public override string ToString()
        {
            return $"{Name} - {Description}";
        }
    }
}

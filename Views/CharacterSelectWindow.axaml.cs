using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Views;

public partial class CharacterSelectWindow : Window
{
    private readonly CharacterDataService _characterDataService;
    
    public string SelectedRace { get; private set; } = "Human";
    public string SelectedClass { get; private set; } = "Wanderer";
    public string CharacterName { get; private set; } = "Hero";
    public bool WasConfirmed { get; private set; } = false;
    
    public CharacterSelectWindow()
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
        
        if (nameTextBox != null && !string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
            CharacterName = nameTextBox.Text;
        }
        
        if (raceListBox?.SelectedItem is SelectionItem selectedRace)
        {
            SelectedRace = selectedRace.Name;
        }
        
        if (classListBox?.SelectedItem is SelectionItem selectedClass)
        {
            SelectedClass = selectedClass.Name;
        }
        
        WasConfirmed = true;
        Close();
    }
    
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

using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Views;

public partial class SavesWindow : Window
{
    public string? SelectedSaveId { get; private set; }
    public bool WasConfirmed { get; private set; } = false;

    // Delete is a two-click confirm (click once to arm, click again on the same item to commit)
    // rather than a modal dialog — this codebase has no message-box abstraction yet, and this is
    // cheap enough to not need one just for "are you sure you want to delete this save."
    private SaveListItem? _pendingDeleteItem;

    public SavesWindow()
    {
        InitializeComponent();
        PopulateSaves();
    }

    private void PopulateSaves()
    {
        var listBox = this.FindControl<ListBox>("SavesListBox");
        if (listBox == null) return;

        var items = SaveService.ListSaves().Select(s => new SaveListItem(s)).ToList();
        listBox.ItemsSource = items;
        if (items.Count > 0) listBox.SelectedIndex = 0;
    }

    private void LoadButton_Click(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SavesListBox");
        if (listBox?.SelectedItem is SaveListItem item)
        {
            SelectedSaveId = item.Summary.SaveId;
            WasConfirmed = true;
            Close();
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        WasConfirmed = false;
        Close();
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SavesListBox");
        var deleteButton = this.FindControl<Button>("DeleteButton");
        if (listBox?.SelectedItem is not SaveListItem item || deleteButton == null) return;

        if (_pendingDeleteItem == item)
        {
            SaveService.Delete(item.Summary.SaveId);
            _pendingDeleteItem = null;
            deleteButton.Content = "Delete";
            PopulateSaves();
        }
        else
        {
            _pendingDeleteItem = item;
            deleteButton.Content = "Confirm Delete?";
        }
    }

    private void SavesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selecting a different item cancels any pending delete confirmation on the old one.
        _pendingDeleteItem = null;
        var deleteButton = this.FindControl<Button>("DeleteButton");
        if (deleteButton != null) deleteButton.Content = "Delete";
    }

    private class SaveListItem
    {
        public SaveSummary Summary { get; }
        public SaveListItem(SaveSummary summary) => Summary = summary;

        public override string ToString() =>
            $"{Summary.HeroName} — {Summary.RaceName} {Summary.ClassName}, Level {Summary.Level} ({Summary.PlaytimeDisplay} played)";
    }
}

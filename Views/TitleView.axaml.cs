using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.Views;

/// <summary>
/// The home screen view: game title, then "press any key" reveals Start a New Game / Continue.
/// Hosted by the single MainWindow shell — navigation happens by raising the events below, never
/// by closing/opening windows.
/// </summary>
public partial class TitleView : UserControl
{
    /// <summary>The player chose "Start a New Game".</summary>
    public event Action? NewGameRequested;

    /// <summary>The player picked a save via Continue (argument: the save id).</summary>
    public event Action<string>? ContinueRequested;

    private bool _menuShown;

    public TitleView()
    {
        InitializeComponent();
        PointerPressed += (s, e) => ShowMenu();
    }

    /// <summary>Called by the shell window's KeyDown routing — any key reveals the menu.</summary>
    public void HandleKeyPressed(KeyEventArgs e) => ShowMenu();

    private void ShowMenu()
    {
        if (_menuShown) return;
        _menuShown = true;

        var prompt = this.FindControl<TextBlock>("PressAnyKeyText");
        if (prompt != null) prompt.IsVisible = false;

        var menu = this.FindControl<StackPanel>("MenuPanel");
        if (menu != null) menu.IsVisible = true;

        var continueButton = this.FindControl<Button>("ContinueButton");
        if (continueButton != null) continueButton.IsEnabled = SaveService.HasAnySaves();

        // Earned visibility (SimpleRPG-style): Bestiary once something's been encountered, Play
        // Stats once any cross-run activity has been recorded.
        var codex = CodexService.Instance.Data;
        var bestiaryButton = this.FindControl<Button>("BestiaryButton");
        if (bestiaryButton != null) bestiaryButton.IsVisible = codex.Bestiary.Count > 0;

        var ps = codex.PlayStats;
        bool hasStats = ps.TotalKills > 0 || ps.TotalDeaths > 0 || ps.TotalFloorsCleared > 0
            || ps.DeepestFloor > 0 || ps.TotalDungeonExits > 0;
        var playStatsButton = this.FindControl<Button>("PlayStatsButton");
        if (playStatsButton != null) playStatsButton.IsVisible = hasStats;
    }

    private void NewGameButton_Click(object? sender, RoutedEventArgs e) =>
        NewGameRequested?.Invoke();

    private async void BestiaryButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new CodexWindow(CodexTab.Bestiary).ShowDialog(owner);
    }

    private async void PlayStatsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            await new CodexWindow(CodexTab.PlayStats).ShowDialog(owner);
    }

    private async void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var savesWindow = new SavesWindow();
        await savesWindow.ShowDialog(owner);

        // Deletions inside the saves window may have emptied the list.
        var continueButton = this.FindControl<Button>("ContinueButton");
        if (continueButton != null) continueButton.IsEnabled = SaveService.HasAnySaves();

        if (savesWindow.WasConfirmed && savesWindow.SelectedSaveId != null)
        {
            ContinueRequested?.Invoke(savesWindow.SelectedSaveId);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;
using TheMazeRPG.UI.Controls;
using TheMazeRPG.ViewModels;

namespace TheMazeRPG.Views;

/// <summary>
/// The in-game view (canvas, HUD overlays, ESC pause menu), hosted by the MainWindow shell.
/// Navigation out (quit to menu/desktop) happens by raising events — the shell decides what to
/// do; this view never opens or closes windows.
/// </summary>
public partial class GameView : UserControl
{
    public event Action? QuitToMenuRequested;
    public event Action? QuitToDesktopRequested;

    /// <summary>Re-raised from the death overlay's "New Hero" button — the shell swaps to
    /// character creation.</summary>
    public event Action? NewHeroRequested;

    private StatsOverlay? _statsOverlay;
    private DeathOverlay? _deathOverlay;
    private MainWindowViewModel? _viewModel;

    public GameView()
    {
        InitializeComponent();
    }

    /// <summary>Wire the running game into this view's canvas and overlays.</summary>
    public void SetViewModel(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;

        if (this.FindControl<GameCanvas>("GameCanvas") is GameCanvas canvas)
        {
            canvas.SetGameState(viewModel.GameState);
        }

        _statsOverlay = this.FindControl<StatsOverlay>("StatsOverlay");
        _statsOverlay?.SetGameState(viewModel.GameState);

        _deathOverlay = this.FindControl<DeathOverlay>("DeathOverlay");
        if (_deathOverlay != null)
        {
            _deathOverlay.SetGameState(viewModel.GameState);
            _deathOverlay.NewHeroRequested += () => NewHeroRequested?.Invoke();
        }
    }

    // Movement keys currently held down, for real-time Manual-mode movement.
    private readonly HashSet<Key> _heldMoveKeys = new();

    private static bool IsMoveKey(Key k) => k switch
    {
        Key.W or Key.A or Key.S or Key.D => true,
        Key.Up or Key.Down or Key.Left or Key.Right => true,
        _ => false
    };

    /// <summary>Called by the shell window's KeyDown routing.</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            e.Handled = true; // Prevent default Tab focus traversal
            if (_statsOverlay != null)
            {
                _statsOverlay.IsOverlayVisible = !_statsOverlay.IsOverlayVisible;
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // Escape backs out of the inventory first if it's open, else toggles the pause menu.
            if (IsInventoryOpen) CloseInventory();
            else TogglePauseMenu();
        }
        else if (e.Key == Key.I && !IsPauseMenuOpen)
        {
            e.Handled = true;
            ToggleInventory();
        }
        else if (_viewModel != null && !IsPauseMenuOpen && !IsInventoryOpen)
        {
            var gs = _viewModel.GameState;
            if (e.Key == Key.M)
            {
                // Explicit toggle between auto-run and manual control.
                gs.ToggleControlMode();
                _heldMoveKeys.Clear();
                UpdateMoveIntent();
                e.Handled = true;
            }
            else if (TryGetHotbarIndex(e.Key, out int slot))
            {
                // Number keys 1-9 select the hotbar attack.
                gs.SelectAttack(slot);
                e.Handled = true;
            }
            else if (IsMoveKey(e.Key))
            {
                // Pressing a movement key drops the hero straight into Manual control.
                if (gs.ControlMode == ControlMode.Auto) gs.SetControlMode(ControlMode.Manual);
                _heldMoveKeys.Add(e.Key);
                UpdateMoveIntent();
                e.Handled = true;
            }
        }
    }

    /// <summary>Map a number-row or numpad key (1-9) to a zero-based hotbar slot index.</summary>
    private static bool TryGetHotbarIndex(Key k, out int index)
    {
        index = k switch
        {
            >= Key.D1 and <= Key.D9 => k - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => k - Key.NumPad1,
            _ => -1
        };
        return index >= 0;
    }

    /// <summary>Called by the shell window's KeyUp routing.</summary>
    public void HandleKeyUp(KeyEventArgs e)
    {
        if (IsMoveKey(e.Key) && _heldMoveKeys.Remove(e.Key))
        {
            UpdateMoveIntent();
            e.Handled = true;
        }
    }

    /// <summary>Recompute the Manual-mode movement vector from the currently-held keys.</summary>
    private void UpdateMoveIntent()
    {
        if (_viewModel == null) return;
        float dx = 0, dy = 0;
        if (_heldMoveKeys.Contains(Key.A) || _heldMoveKeys.Contains(Key.Left)) dx -= 1;
        if (_heldMoveKeys.Contains(Key.D) || _heldMoveKeys.Contains(Key.Right)) dx += 1;
        if (_heldMoveKeys.Contains(Key.W) || _heldMoveKeys.Contains(Key.Up)) dy -= 1;
        if (_heldMoveKeys.Contains(Key.S) || _heldMoveKeys.Contains(Key.Down)) dy += 1;
        _viewModel.GameState.SetManualMoveIntent(dx, dy);
    }

    private void StatsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_statsOverlay != null)
        {
            _statsOverlay.IsOverlayVisible = !_statsOverlay.IsOverlayVisible;
        }
    }

    // --- ESC Pause Menu ---

    private bool IsPauseMenuOpen =>
        this.FindControl<Border>("PauseMenuOverlay")?.IsVisible == true;

    private void TogglePauseMenu()
    {
        if (IsPauseMenuOpen) ClosePauseMenu();
        else OpenPauseMenu();
    }

    private void OpenPauseMenu()
    {
        var overlay = this.FindControl<Border>("PauseMenuOverlay");
        if (overlay == null || _viewModel == null) return;

        // Pause the simulation while the menu is up. Release any held movement so the hero
        // doesn't keep drifting when we un-pause (key-ups may not arrive while paused).
        _viewModel.GameState.IsRunning = false;
        _heldMoveKeys.Clear();
        _viewModel.GameState.SetManualMoveIntent(0, 0);
        overlay.IsVisible = true;

        // Saving is only allowed in the Overworld or an unengaged safe room (GameState.CanSave).
        bool canSave = _viewModel.GameState.CanSave;
        var saveButton = this.FindControl<Button>("SaveButton");
        if (saveButton != null)
        {
            saveButton.IsEnabled = canSave;
            saveButton.Content = "Save";
        }
        var hint = this.FindControl<TextBlock>("SaveHintText");
        if (hint != null)
        {
            hint.Text = canSave ? "" : "Saving is only possible in town or a safe room.";
        }
    }

    private void ClosePauseMenu()
    {
        var overlay = this.FindControl<Border>("PauseMenuOverlay");
        if (overlay == null || _viewModel == null) return;

        overlay.IsVisible = false;
        _viewModel.GameState.IsRunning = true;
    }

    private void ResumeButton_Click(object? sender, RoutedEventArgs e) =>
        ClosePauseMenu();

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || !_viewModel.GameState.CanSave) return;
        SaveService.Save(_viewModel.GameState);

        var saveButton = this.FindControl<Button>("SaveButton");
        if (saveButton != null) saveButton.Content = "Saved!";
    }

    private void QuitToMenuButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveIfAllowed();
        QuitToMenuRequested?.Invoke();
    }

    private void QuitToDesktopButton_Click(object? sender, RoutedEventArgs e)
    {
        SaveIfAllowed();
        QuitToDesktopRequested?.Invoke();
    }

    /// <summary>Quitting auto-saves when saving is allowed; mid-dungeon it does nothing (the
    /// entry-time snapshot already on disk is the intended revert point).</summary>
    private void SaveIfAllowed()
    {
        if (_viewModel != null && _viewModel.GameState.CanSave)
        {
            SaveService.Save(_viewModel.GameState);
        }
    }

    // --- Inventory / Hotbar assignment ---

    private bool IsInventoryOpen =>
        this.FindControl<Border>("InventoryOverlay")?.IsVisible == true;

    private void ToggleInventory()
    {
        if (IsInventoryOpen) CloseInventory();
        else OpenInventory();
    }

    private void OpenInventory()
    {
        var overlay = this.FindControl<Border>("InventoryOverlay");
        if (overlay == null || _viewModel == null) return;

        // Pause + release movement while managing gear (mirrors the pause menu).
        _viewModel.GameState.IsRunning = false;
        _heldMoveKeys.Clear();
        _viewModel.GameState.SetManualMoveIntent(0, 0);

        PopulateInventory();
        overlay.IsVisible = true;
    }

    private void CloseInventory()
    {
        var overlay = this.FindControl<Border>("InventoryOverlay");
        if (overlay == null || _viewModel == null) return;

        overlay.IsVisible = false;
        _viewModel.GameState.IsRunning = true;
    }

    private void PopulateInventory()
    {
        if (_viewModel == null) return;
        var hero = _viewModel.GameState.Hero;

        var equippedPanel = this.FindControl<StackPanel>("EquippedPanel");
        if (equippedPanel != null)
        {
            equippedPanel.Children.Clear();
            var equipped = hero.Loadout.Where(c => c is Weapon || c is Spell).ToList();
            if (equipped.Count == 0)
                equippedPanel.Children.Add(MutedRow("Nothing equipped"));
            foreach (var item in equipped)
                equippedPanel.Children.Add(ItemButton(item, "▸ remove", () =>
                {
                    _viewModel.GameState.UnequipToInventory(item);
                    PopulateInventory();
                }));
        }

        var inventoryPanel = this.FindControl<StackPanel>("InventoryPanel");
        if (inventoryPanel != null)
        {
            inventoryPanel.Children.Clear();
            int equippedCount = hero.Loadout.Count(c => c is Weapon || c is Spell);
            bool hasRoom = equippedCount < hero.HotbarCapacity;

            var invAttackGear = hero.Inventory.Where(c => c is Weapon || c is Spell).ToList();
            if (invAttackGear.Count == 0)
                inventoryPanel.Children.Add(MutedRow("No unequipped attacks"));
            foreach (var item in invAttackGear)
            {
                var btn = ItemButton(item, hasRoom ? "equip ◂" : "(hotbar full)", () =>
                {
                    if (_viewModel.GameState.EquipFromInventory(item)) PopulateInventory();
                });
                btn.IsEnabled = hasRoom;
                inventoryPanel.Children.Add(btn);
            }
        }
    }

    private Button ItemButton(Combinable item, string action, Action onClick)
    {
        var name = new TextBlock
        {
            Text = item.Name,
            Foreground = new SolidColorBrush(RarityColor(item.Rarity)),
            FontSize = 13
        };
        var act = new TextBlock
        {
            Text = action,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(act, 1);
        row.Children.Add(name);
        row.Children.Add(act);

        var btn = new Button
        {
            Content = row,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(3),
            Padding = new Avalonia.Thickness(8, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static TextBlock MutedRow(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        FontSize = 12,
        Margin = new Avalonia.Thickness(2, 4)
    };

    private static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => Color.FromRgb(0x78, 0xDC, 0x82),
        Rarity.Rare => Color.FromRgb(0x5A, 0xA0, 0xFF),
        Rarity.Epic => Color.FromRgb(0xBE, 0x78, 0xFF),
        Rarity.Legendary => Color.FromRgb(0xFF, 0xA5, 0x3C),
        Rarity.Mythic => Color.FromRgb(0xFF, 0xD7, 0x5A),
        _ => Color.FromRgb(0xDC, 0xDC, 0xDC)
    };
}

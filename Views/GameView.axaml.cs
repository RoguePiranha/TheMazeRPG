using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
            canvas.InteractRequested += OnInteractRequested;
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
        // Debug console: the backtick (`/~) key toggles it. While open it owns the keyboard —
        // Enter runs the command, Esc/backtick close it, and every other key falls through to the
        // focused TextBox so typing works (we deliberately don't mark those Handled).
        if (e.Key == Key.OemTilde)
        {
            e.Handled = true;
            ToggleConsole();
            return;
        }
        if (IsConsoleOpen)
        {
            if (e.Key == Key.Enter) { RunConsoleCommand(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseConsole(); e.Handled = true; }
            // else: let the keystroke reach the console TextBox.
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true; // Prevent default Tab focus traversal
            // Don't stack the stats panel on top of another modal overlay.
            if (!IsPauseMenuOpen && !IsInventoryOpen && !IsLootOpen && !IsContextMenuOpen)
                ToggleStats();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // Escape backs out of overlays in order, else toggles the pause menu.
            if (IsContextMenuOpen) CloseContextMenu();
            else if (IsLootOpen) CloseLoot();
            else if (IsInventoryOpen) CloseInventory();
            else if (IsStatsOpen) ToggleStats();
            else TogglePauseMenu();
        }
        else if (e.Key == Key.I && !IsPauseMenuOpen)
        {
            e.Handled = true;
            ToggleInventory();
        }
        else if (e.Key == Key.Space && _viewModel != null && !IsPauseMenuOpen && !IsInventoryOpen && !IsLootOpen)
        {
            e.Handled = true;
            _viewModel.GameState.TryDash();
        }
        else if (_viewModel != null && !IsPauseMenuOpen && !IsInventoryOpen && !IsLootOpen)
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

    private void StatsButton_Click(object? sender, RoutedEventArgs e) => ToggleStats();

    private bool IsStatsOpen => _statsOverlay?.IsOverlayVisible == true;

    /// <summary>Show/hide the stats panel. Opening pauses the sim and releases held movement,
    /// exactly like the inventory and pause overlays; closing resumes.</summary>
    private void ToggleStats()
    {
        if (_statsOverlay == null || _viewModel == null) return;
        if (IsStatsOpen)
        {
            _statsOverlay.IsOverlayVisible = false;
            _viewModel.GameState.IsRunning = true;
        }
        else
        {
            _viewModel.GameState.IsRunning = false;
            _heldMoveKeys.Clear();
            _viewModel.GameState.SetManualMoveIntent(0, 0);
            _statsOverlay.IsOverlayVisible = true;
        }
    }

    // --- Debug console (backtick key) ---

    private bool IsConsoleOpen =>
        this.FindControl<Border>("DebugConsoleOverlay")?.IsVisible == true;

    private void ToggleConsole()
    {
        if (IsConsoleOpen) CloseConsole();
        else OpenConsole();
    }

    /// <summary>Open the debug console: pause the sim (so nothing moves/dies while typing), release
    /// held movement, show the bar, and focus the input.</summary>
    private void OpenConsole()
    {
        var overlay = this.FindControl<Border>("DebugConsoleOverlay");
        if (overlay == null || _viewModel == null) return;

        _viewModel.GameState.IsRunning = false;
        _heldMoveKeys.Clear();
        _viewModel.GameState.SetManualMoveIntent(0, 0);

        overlay.IsVisible = true;
        var input = this.FindControl<TextBox>("DebugConsoleInput");
        if (input != null)
        {
            input.Text = "";
            // Focus after layout so the caret lands in the box.
            Dispatcher.UIThread.Post(() => input.Focus());
        }
    }

    private void CloseConsole()
    {
        var overlay = this.FindControl<Border>("DebugConsoleOverlay");
        if (overlay == null || _viewModel == null) return;
        overlay.IsVisible = false;
        _viewModel.GameState.IsRunning = true;
    }

    /// <summary>Run whatever's in the console input, show the result on the output line, and clear
    /// the input for the next command (the console stays open).</summary>
    private void RunConsoleCommand()
    {
        if (_viewModel == null) return;
        var input = this.FindControl<TextBox>("DebugConsoleInput");
        var output = this.FindControl<TextBlock>("DebugConsoleOutput");
        string cmd = input?.Text ?? "";
        if (string.IsNullOrWhiteSpace(cmd)) return;

        string result = _viewModel.GameState.ExecuteDebugCommand(cmd);
        if (output != null) output.Text = string.IsNullOrEmpty(result) ? "(no output)" : result;
        if (input != null) input.Text = "";
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

    // --- Right-click context menu (Examine / Disarm / Loot) ---

    private bool IsContextMenuOpen =>
        this.FindControl<Border>("ContextMenuOverlay")?.IsVisible == true;

    private void OnInteractRequested(InteractTarget target)
    {
        var overlay = this.FindControl<Border>("ContextMenuOverlay");
        var box = this.FindControl<Border>("ContextMenuBox");
        var panel = this.FindControl<StackPanel>("ContextMenuPanel");
        if (overlay == null || box == null || panel == null || _viewModel == null) return;
        var gs = _viewModel.GameState;
        panel.Children.Clear();

        if (target.Feature is { } feature)
        {
            AddMenuItem(panel, "Examine", () => { gs.ExamineFeature(feature); CloseContextMenu(); });
            if (feature.Perceived && feature.Type == MazeFeatureType.Trap)
                AddMenuItem(panel, "Disarm", () => { gs.TryDisarm(feature); CloseContextMenu(); });
        }
        else if (target.Corpse is { } corpse)
        {
            AddMenuItem(panel, "Examine", () => { gs.ExamineCorpse(corpse); CloseContextMenu(); });
            bool hasLoot = corpse.Inventory.Count > 0 || corpse.Gold > 0;
            var loot = AddMenuItem(panel, hasLoot ? "Loot" : "Loot (empty)", () => { CloseContextMenu(); OpenLoot(corpse); });
            loot.IsEnabled = hasLoot;
        }

        box.Margin = new Avalonia.Thickness(target.ScreenPoint.X, target.ScreenPoint.Y, 0, 0);
        overlay.IsVisible = true;
    }

    private Button AddMenuItem(StackPanel panel, string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Padding = new Avalonia.Thickness(10, 4)
        };
        btn.Click += (_, _) => onClick();
        panel.Children.Add(btn);
        return btn;
    }

    private void CloseContextMenu()
    {
        var overlay = this.FindControl<Border>("ContextMenuOverlay");
        if (overlay != null) overlay.IsVisible = false;
    }

    private void ContextBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A click anywhere on the backdrop (i.e. not on the menu box) dismisses the menu.
        if (e.Source is Control c && (c.Name == "ContextMenuOverlay" || !IsInsideMenuBox(c)))
            CloseContextMenu();
    }

    private bool IsInsideMenuBox(Control c)
    {
        var box = this.FindControl<Border>("ContextMenuBox");
        for (Control? cur = c; cur != null; cur = cur.Parent as Control)
            if (ReferenceEquals(cur, box)) return true;
        return false;
    }

    // --- Loot window (body + player inventory) ---

    private Enemy? _lootCorpse;

    private bool IsLootOpen =>
        this.FindControl<Border>("LootOverlay")?.IsVisible == true;

    private void OpenLoot(Enemy corpse)
    {
        var overlay = this.FindControl<Border>("LootOverlay");
        if (overlay == null || _viewModel == null) return;

        _lootCorpse = corpse;
        _viewModel.GameState.IsRunning = false; // pause while looting
        _heldMoveKeys.Clear();
        _viewModel.GameState.SetManualMoveIntent(0, 0);

        var title = this.FindControl<TextBlock>("LootTitle");
        if (title != null) title.Text = $"Loot — {corpse.Race} {corpse.Class}";
        PopulateLoot();
        overlay.IsVisible = true;
    }

    private void CloseLoot()
    {
        var overlay = this.FindControl<Border>("LootOverlay");
        if (overlay == null || _viewModel == null) return;
        overlay.IsVisible = false;
        _lootCorpse = null;
        _viewModel.GameState.IsRunning = true;
    }

    private void LootCloseButton_Click(object? sender, RoutedEventArgs e) => CloseLoot();

    private void LootAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _lootCorpse == null) return;
        _viewModel.GameState.LootAll(_lootCorpse);
        PopulateLoot();
    }

    private void PopulateLoot()
    {
        if (_viewModel == null || _lootCorpse == null) return;
        var hero = _viewModel.GameState.Hero;

        var bodyPanel = this.FindControl<StackPanel>("BodyPanel");
        if (bodyPanel != null)
        {
            bodyPanel.Children.Clear();
            if (_lootCorpse.Gold > 0)
                bodyPanel.Children.Add(MutedRow($"{_lootCorpse.Gold} gold (Loot All)"));
            if (_lootCorpse.Inventory.Count == 0)
                bodyPanel.Children.Add(MutedRow("Empty"));
            foreach (var item in _lootCorpse.Inventory)
                bodyPanel.Children.Add(LootRow(item, fromBody: true));
        }

        var playerPanel = this.FindControl<StackPanel>("PlayerPanel");
        if (playerPanel != null)
        {
            playerPanel.Children.Clear();
            if (hero.Inventory.Count == 0)
                playerPanel.Children.Add(MutedRow("Empty"));
            foreach (var item in hero.Inventory)
                playerPanel.Children.Add(LootRow(item, fromBody: false));
        }
    }

    // A loot row: click the name to inspect; the arrow (or a double-click) transfers it. Body is the
    // left panel, player inventory the right — so body items pull right "▸", player items push left "◂".
    private Border LootRow(Combinable item, bool fromBody)
    {
        var row = ItemRow(item, fromBody ? "▸" : "◂", true,
            onArrow: () => TransferLoot(item, fromBody),
            onSelect: () => ShowDetails("LootDetailsText", item));
        row.DoubleTapped += (_, _) => TransferLoot(item, fromBody);
        return row;
    }

    private void TransferLoot(Combinable item, bool fromBody)
    {
        if (_viewModel == null || _lootCorpse == null) return;
        var gs = _viewModel.GameState;
        if (fromBody) gs.LootItem(_lootCorpse, item);
        else gs.DepositToCorpse(_lootCorpse, item);
        PopulateLoot();
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
                equippedPanel.Children.Add(ItemRow(item, "▸", true,
                    onArrow: () => { _viewModel.GameState.UnequipToInventory(item); PopulateInventory(); },
                    onSelect: () => ShowDetails("ItemDetailsText", item)));
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
                inventoryPanel.Children.Add(ItemRow(item, "◂", hasRoom,
                    onArrow: () => { if (_viewModel.GameState.EquipFromInventory(item)) PopulateInventory(); },
                    onSelect: () => ShowDetails("ItemDetailsText", item)));
        }
    }

    // Shared item row: a name button (click → inspect via onSelect) and a transfer-arrow button
    // (click → onArrow). Transfer only happens through the arrow, never a stray row click.
    private Border ItemRow(Combinable item, string arrowGlyph, bool arrowEnabled, Action onArrow, Action onSelect)
    {
        var name = new Button
        {
            Content = new TextBlock { Text = $"{item.Name}  ({item.Rarity})", Foreground = new SolidColorBrush(RarityColor(item.Rarity)), FontSize = 13 },
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(6, 4)
        };
        name.Click += (_, _) => onSelect();

        var arrow = new Button
        {
            Content = arrowGlyph,
            IsEnabled = arrowEnabled,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)),
            Padding = new Avalonia.Thickness(12, 4),
            Margin = new Avalonia.Thickness(4, 0, 0, 0)
        };
        arrow.Click += (_, _) => onArrow();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(name);
        grid.Children.Add(arrow);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(3),
            Child = grid
        };
    }

    private void ShowDetails(string textBlockName, Combinable item)
    {
        if (this.FindControl<TextBlock>(textBlockName) is { } tb) tb.Text = ItemStatsText(item);
    }

    private static string ItemStatsText(Combinable item)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{item.Name}  ·  {item.Rarity} {item.Kind}");
        if (!string.IsNullOrWhiteSpace(item.Description)) sb.Append('\n').Append(item.Description);
        switch (item)
        {
            case Weapon w:
                sb.Append($"\nDamage {w.BaseDamage}   Range {w.Range:0.#}   Cooldown {w.Cooldown}   Crit {w.CritChance:P0}");
                if (w.StaminaCost > 0) sb.Append($"   Stamina {w.StaminaCost}");
                break;
            case Spell s:
                sb.Append($"\nDamage {s.BaseDamage}   Range {s.Range:0.#}   Cooldown {s.Cooldown}   Crit {s.CritChance:P0}");
                if (s.ManaCost > 0) sb.Append($"   Mana {s.ManaCost}");
                if (s.FaithCost > 0) sb.Append($"   Faith {s.FaithCost}");
                break;
            case Ability a when a.Modifiers.Count > 0:
                sb.Append('\n').Append(string.Join("   ", a.Modifiers.Select(m => $"{m.Key} +{m.Value:0.##}")));
                break;
        }
        if (item.Attributes.Count > 0) sb.Append($"\nAttributes: {string.Join(", ", item.Attributes)}");
        if (item.Level > 0) sb.Append($"   ·  Lv {item.Level}");
        return sb.ToString();
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

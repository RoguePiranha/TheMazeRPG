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

        // Refresh the "Press E" interaction prompt from GameState.NearbyInteractable each frame
        // (mirrors GameCanvas's own render timer — the prompt is UI state, not drawn on the canvas).
        _promptTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
        _promptTimer.Tick -= PromptTimer_Tick;
        _promptTimer.Tick += PromptTimer_Tick;
        _promptTimer.Start();
    }

    private DispatcherTimer? _promptTimer;

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // A fresh GameView is created per game; stop the prompt timer so the discarded view (and the
        // stale GameState it captures) can be collected.
        base.OnDetachedFromVisualTree(e);
        _promptTimer?.Stop();
    }

    private void PromptTimer_Tick(object? sender, EventArgs e)
    {
        var overlay = this.FindControl<Border>("InteractPromptOverlay");
        var text = this.FindControl<TextBlock>("InteractPromptText");
        if (overlay == null || text == null || _viewModel == null) return;

        // Show only while a structure, townsperson, or workable rock face is in range and no
        // blocking overlay/menu is up.
        var near = _viewModel.GameState.NearbyInteractable;
        var npc = _viewModel.GameState.NearbyNpc;
        var rock = _viewModel.GameState.NearbyMinableRock;
        bool show = (near != null || npc != null || rock != null) && !AnyOverlayOpen;
        overlay.IsVisible = show;
        if (show)
        {
            // The rock line is a hint, not a button: mining is swinging the held pickaxe.
            text.Text = near != null
                ? $"Press [E] — {StructureName(near.Type)}"
                : npc != null
                    ? $"Press [E] — {npc.Name} ({npc.Occupation})"
                    : "Swing your pickaxe at the rock face to mine";
        }
    }

    /// <summary>Friendly label for a town structure.</summary>
    private string StructureName(MazeFeatureType t) => t switch
    {
        MazeFeatureType.Smithy => "Smithy",
        MazeFeatureType.Stall => "Stall",
        MazeFeatureType.DungeonEntrance => "Dungeon Entrance",
        MazeFeatureType.GuardianDoor => $"Challenge the Floor {(_viewModel?.GameState.CurrentFloor ?? 0) + 1} Guardian",
        MazeFeatureType.Shrine => "Return to Town",
        _ => "Structure"
    };

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
        // The death screen is modal: it offers its own buttons and nothing else. No key may open
        // an overlay over it — a pause menu on top would freeze the restart countdown behind it.
        if (_viewModel?.GameState.IsHeroDead == true)
        {
            e.Handled = true;
            return;
        }

        // Debug console: the backtick (`/~) key toggles it. While open it owns the keyboard —
        // Enter runs the command, Esc/backtick close it, and every other key falls through to the
        // focused TextBox so typing works (we deliberately don't mark those Handled). It only
        // OPENS from gameplay: over another overlay it would stack two pause claims.
        if (e.Key == Key.OemTilde)
        {
            e.Handled = true;
            if (IsConsoleOpen || !AnyOverlayOpen) ToggleConsole();
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
            // Toggle only from gameplay or from the stats panel itself — never stacked on
            // another overlay.
            if (IsStatsOpen || !AnyOverlayOpen) ToggleStats();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // Escape backs out of overlays in order, else toggles the pause menu.
            if (IsContextMenuOpen) CloseContextMenu();
            else if (IsSellOpen) CloseSell();
            else if (IsLootOpen) CloseLoot();
            else if (IsInventoryOpen) CloseInventory();
            else if (IsStatsOpen) ToggleStats();
            else TogglePauseMenu();
        }
        else if (e.Key == Key.I)
        {
            e.Handled = true;
            // Same rule as Tab: toggle from gameplay or from the inventory itself only.
            if (IsInventoryOpen || !AnyOverlayOpen) ToggleInventory();
        }
        else if (e.Key == Key.E && !AnyOverlayOpen && _viewModel?.GameState.NearbyNpc is { } villager)
        {
            // A townsperson in reach: talk, trade, train, or be healed (note 09 PR 9).
            e.Handled = true;
            OpenNpcMenu(villager);
        }
        else if (e.Key == Key.E && !AnyOverlayOpen && _viewModel?.GameState.NearbyInteractable is { } structure)
        {
            // Overworld: use the town structure the hero is standing next to.
            e.Handled = true;
            OpenStructureMenu(structure);
        }
        else if (e.Key == Key.Space && _viewModel != null && !AnyOverlayOpen)
        {
            e.Handled = true;
            _viewModel.GameState.TryDash();
        }
        else if (e.Key == Key.C && _viewModel != null && !AnyOverlayOpen)
        {
            // Sneak toggle (note 11 §3, owner-confirmed toggle). Manual only; the sim refuses
            // otherwise, so no mode checks needed here.
            e.Handled = true;
            _viewModel.GameState.ToggleSneak();
        }
        else if (_viewModel != null && !AnyOverlayOpen)
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
                // Hotbar keys: attack slots select; consumable slots use one copy.
                gs.ActivateHotbarSlot(slot);
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

    // Per-slot hotbar bindings from settings.json ("hotbarKeys"), parsed once. Unparseable
    // entries are skipped (their slot falls back to the numpad only).
    private static readonly Key[] HotbarBindings = ParseHotbarBindings();

    private static Key[] ParseHotbarBindings()
    {
        var names = Core.Services.GameSettings.Current.HotbarKeys;
        var keys = new Key[names.Length];
        for (int i = 0; i < names.Length; i++)
            keys[i] = Enum.TryParse<Key>(names[i], ignoreCase: true, out var k) ? k : Key.None;
        return keys;
    }

    /// <summary>Map a pressed key to a zero-based hotbar slot: the configurable per-slot
    /// bindings first (settings.json), then the numpad as an always-on fallback.</summary>
    private static bool TryGetHotbarIndex(Key k, out int index)
    {
        for (int i = 0; i < HotbarBindings.Length; i++)
        {
            if (HotbarBindings[i] != Key.None && HotbarBindings[i] == k)
            {
                index = i;
                return true;
            }
        }
        index = k is >= Key.NumPad1 and <= Key.NumPad9 ? k - Key.NumPad1 : -1;
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
        // The button floats above everything, so it needs the same rules as the Tab key: dead
        // heroes get the death screen only, and stats never stacks on another overlay.
        if (_viewModel?.GameState.IsHeroDead == true) return;
        if (IsStatsOpen || !AnyOverlayOpen) ToggleStats();
    }

    private bool IsStatsOpen => _statsOverlay?.IsOverlayVisible == true;

    /// <summary>Show/hide the stats panel. Opening pauses the sim and releases held movement,
    /// exactly like the inventory and pause overlays; closing resumes.</summary>
    private void ToggleStats()
    {
        if (_statsOverlay == null || _viewModel == null) return;
        if (IsStatsOpen)
        {
            _statsOverlay.IsOverlayVisible = false;
            SyncPauseToOverlays();
        }
        else
        {
            _statsOverlay.IsOverlayVisible = true;
            OnOverlayOpened();
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

        overlay.IsVisible = true;
        OnOverlayOpened();
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
        SyncPauseToOverlays();
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

        overlay.IsVisible = true;
        OnOverlayOpened();

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
        SyncPauseToOverlays();
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

        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Top;
        box.Margin = new Avalonia.Thickness(target.ScreenPoint.X, target.ScreenPoint.Y, 0, 0);
        overlay.IsVisible = true;
    }

    /// <summary>Overworld: open the action menu for a town structure (triggered by the E key while
    /// standing next to it). Reuses the right-click context-menu overlay, opened centered.</summary>
    private void OpenStructureMenu(MazeFeature feature)
    {
        var overlay = this.FindControl<Border>("ContextMenuOverlay");
        var box = this.FindControl<Border>("ContextMenuBox");
        var panel = this.FindControl<StackPanel>("ContextMenuPanel");
        if (overlay == null || box == null || panel == null || _viewModel == null) return;
        var gs = _viewModel.GameState;
        panel.Children.Clear();
        _heldMoveKeys.Clear();
        gs.SetManualMoveIntent(0, 0);

        switch (feature.Type)
        {
            case MazeFeatureType.Smithy:
                foreach (var recipe in RecipeDataService.Instance.Recipes.Values)
                {
                    var r = recipe;
                    bool can = gs.CanCraft(r);
                    var item = AddMenuItem(panel, can ? r.Name : $"{r.Name}  ({NeedText(r, gs)})",
                        () => { gs.Craft(r); CloseContextMenu(); });
                    item.IsEnabled = can;
                }
                break;
            case MazeFeatureType.Stall:
                AddMenuItem(panel, "Sell items…", () => { CloseContextMenu(); OpenSell(); });
                break;
            case MazeFeatureType.DungeonEntrance:
                AddMenuItem(panel, "Enter the Dungeon", () => { CloseContextMenu(); gs.EnterDungeon(); });
                break;
            case MazeFeatureType.GuardianDoor:
                AddMenuItem(panel, $"Enter Floor {gs.CurrentFloor + 1} and fight the Guardian",
                    () => { CloseContextMenu(); gs.EnterGuardianChamber(); });
                break;
            case MazeFeatureType.Shrine:
                AddMenuItem(panel, "Return to town (end this dive)",
                    () => { CloseContextMenu(); gs.UseSafeRoomShrine(); });
                break;
        }
        AddMenuItem(panel, "Cancel", CloseContextMenu);

        // Centered (opened via the E key, not at a click point).
        box.HorizontalAlignment = HorizontalAlignment.Center;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Margin = new Avalonia.Thickness(0);
        overlay.IsVisible = true;
    }

    /// <summary>
    /// The Press-E menu for a townsperson (note 09 PR 9): Talk for everyone; the trades add
    /// their services — the merchant's wares, the trainer's spell list (affinity-gated entries
    /// shown with their reason), the priest's healing rite.
    /// </summary>
    private void OpenNpcMenu(Npc npc)
    {
        var overlay = this.FindControl<Border>("ContextMenuOverlay");
        var box = this.FindControl<Border>("ContextMenuBox");
        var panel = this.FindControl<StackPanel>("ContextMenuPanel");
        if (overlay == null || box == null || panel == null || _viewModel == null) return;
        var gs = _viewModel.GameState;
        panel.Children.Clear();
        _heldMoveKeys.Clear();
        gs.SetManualMoveIntent(0, 0);

        int opinion = NpcInteractionService.GetOpinion(gs.Hero, npc);
        AddMenuItem(panel,
            $"{npc.Name} — {npc.Occupation} ({NpcInteractionService.OpinionTier(opinion)})", () => { });

        AddMenuItem(panel, "Talk", () => { NpcInteractionService.Talk(gs, npc); CloseContextMenu(); });

        switch (npc.Occupation)
        {
            case NpcOccupation.Merchant:
                foreach (var (item, price) in NpcInteractionService.MerchantStock(gs, npc))
                {
                    var id = item.Id;
                    bool affordable = gs.Hero.Gold >= price;
                    var entry = AddMenuItem(panel, $"Buy {item.Name} — {price}g",
                        () => { NpcInteractionService.Buy(gs, npc, id); CloseContextMenu(); });
                    entry.IsEnabled = affordable;
                }
                AddMenuItem(panel, "Sell items…", () => { CloseContextMenu(); OpenSell(); });
                break;

            case NpcOccupation.Trainer:
                foreach (var offer in NpcInteractionService.TrainingOffers(gs))
                {
                    var id = offer.Id;
                    string label = offer.Known
                        ? $"{offer.Name} — already known"
                        : offer.Gated
                            ? $"{offer.Name} — {offer.Requirement}"
                            : $"Learn {offer.Name} ({offer.Element}) — {offer.Price}g";
                    var entry = AddMenuItem(panel, label,
                        () => { NpcInteractionService.Train(gs, npc, id); CloseContextMenu(); });
                    entry.IsEnabled = !offer.Known && !offer.Gated && gs.Hero.Gold >= offer.Price;
                }
                break;

            case NpcOccupation.Priest:
                int cost = NpcInteractionService.HealCost(gs.Hero);
                var heal = AddMenuItem(panel,
                    $"Healing rite — {cost}g ({gs.Hero.CurrentHp}/{gs.Hero.MaxHp} HP)",
                    () => { NpcInteractionService.PriestHeal(gs, npc); CloseContextMenu(); });
                heal.IsEnabled = gs.Hero.CurrentHp < gs.Hero.MaxHp && gs.Hero.Gold >= cost;
                break;
        }

        AddMenuItem(panel, "Cancel", CloseContextMenu);
        box.HorizontalAlignment = HorizontalAlignment.Center;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Margin = new Avalonia.Thickness(0);
        overlay.IsVisible = true;
    }

    /// <summary>"need 2 iron-ore, 1 …" — the recipe inputs the hero is short on.</summary>
    private static string NeedText(RecipeDef r, GameState gs) =>
        "need " + string.Join(", ", r.Inputs
            .Where(kv => gs.Hero.Resources.GetValueOrDefault(kv.Key, 0) < kv.Value)
            .Select(kv => $"{kv.Value} {kv.Key}"));

    /// <summary>True while any blocking overlay/menu is up — used to suppress the Overworld E prompt
    /// and its action while the player is in another UI.</summary>
    private bool AnyOverlayOpen =>
        IsPauseMenuOpen || IsInventoryOpen || IsLootOpen || IsSellOpen ||
        IsStatsOpen || IsContextMenuOpen || IsConsoleOpen;

    /// <summary>The overlays that pause the sim while up (the right-click context menu
    /// deliberately does not pause).</summary>
    private bool AnyPausingOverlayOpen =>
        IsPauseMenuOpen || IsInventoryOpen || IsLootOpen || IsSellOpen ||
        IsStatsOpen || IsConsoleOpen;

    /// <summary>
    /// The single authority for the pause flag: derived from what's actually on screen, called
    /// after every overlay visibility change. The per-overlay `IsRunning = true` writes this
    /// replaces could resume the sim while another overlay was still up (e.g. closing the debug
    /// console under the pause menu let enemies attack behind the "PAUSED" screen).
    /// </summary>
    private void SyncPauseToOverlays()
    {
        if (_viewModel == null) return;
        _viewModel.GameState.IsRunning = !AnyPausingOverlayOpen;
    }

    /// <summary>Release held movement when an overlay takes the keyboard (key-ups may never
    /// arrive while it's up), then re-derive the pause flag.</summary>
    private void OnOverlayOpened()
    {
        _heldMoveKeys.Clear();
        _viewModel?.GameState.SetManualMoveIntent(0, 0);
        SyncPauseToOverlays();
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

    // --- Stall sell window ---

    private bool IsSellOpen =>
        this.FindControl<Border>("SellOverlay")?.IsVisible == true;

    /// <summary>Open the Stall sell list. Pauses + releases movement like the other overlays.</summary>
    private void OpenSell()
    {
        var overlay = this.FindControl<Border>("SellOverlay");
        if (overlay == null || _viewModel == null) return;

        PopulateSell();
        overlay.IsVisible = true;
        OnOverlayOpened();
    }

    private void CloseSell()
    {
        var overlay = this.FindControl<Border>("SellOverlay");
        if (overlay == null || _viewModel == null) return;
        overlay.IsVisible = false;
        SyncPauseToOverlays();
    }

    private void SellCloseButton_Click(object? sender, RoutedEventArgs e) => CloseSell();

    private void PopulateSell()
    {
        if (_viewModel == null) return;
        var gs = _viewModel.GameState;
        var hero = gs.Hero;

        if (this.FindControl<TextBlock>("SellGoldText") is { } goldText)
            goldText.Text = hero.Gold.ToString();
        if (this.FindControl<TextBlock>("SellDetailsText") is { } detailsText)
            detailsText.Text = "Select an item to inspect it.";

        var panel = this.FindControl<StackPanel>("SellPanel");
        if (panel == null) return;
        panel.Children.Clear();

        // SellItem works on inventory, action-bar loadout, AND worn Equipment slots.
        var items = hero.Inventory.Concat(hero.Loadout).Concat(hero.Equipment.Values).ToList();
        if (items.Count == 0)
        {
            panel.Children.Add(MutedRow("You have no items to sell."));
            return;
        }

        foreach (var item in items)
        {
            int price = gs.SellPrice(item);
            panel.Children.Add(ItemRow(item, $"{price}g", arrowEnabled: true,
                onArrow: () => { gs.SellItem(item); PopulateSell(); },
                onSelect: () =>
                {
                    ShowDetails("SellDetailsText", item);
                    if (this.FindControl<TextBlock>("SellDetailsText") is { } d)
                        d.Text += $"\nSell price: {price} gold";
                }));
        }
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

        var title = this.FindControl<TextBlock>("LootTitle");
        if (title != null) title.Text = $"Loot — {corpse.Race} {corpse.Class}";
        PopulateLoot();
        overlay.IsVisible = true;
        OnOverlayOpened();
    }

    private void CloseLoot()
    {
        var overlay = this.FindControl<Border>("LootOverlay");
        if (overlay == null || _viewModel == null) return;
        overlay.IsVisible = false;
        _lootCorpse = null;
        SyncPauseToOverlays();
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

        PopulateInventory();
        overlay.IsVisible = true;
        OnOverlayOpened();
    }

    private void CloseInventory()
    {
        var overlay = this.FindControl<Border>("InventoryOverlay");
        if (overlay == null || _viewModel == null) return;

        overlay.IsVisible = false;
        SyncPauseToOverlays();
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

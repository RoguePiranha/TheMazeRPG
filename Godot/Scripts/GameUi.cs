using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.GodotClient;

/// <summary>Godot-facing menus and dungeon overlays. All mutations still go through Core APIs.</summary>
public partial class GameUi : Control
{
    private static readonly Color Ink = new("#0B0E11");
    private static readonly Color Panel = new("#171C20");
    private static readonly Color PanelRaised = new("#20272C");
    private static readonly Color Border = new("#465159");
    private static readonly Color Gold = new("#E8C861");
    private static readonly Color Blue = new("#79B8D1");
    private static readonly Color Green = new("#71B895");
    private static readonly Color Red = new("#E46962");
    private static readonly Color Text = new("#E2E6E8");
    private static readonly Color Muted = new("#929CA2");

    private Control _frontEnd = null!;
    private ColorRect _frontShade = null!;
    private Control _hud = null!;
    private ColorRect _modal = null!;
    private CenterContainer _modalCenter = null!;
    private Action? _modalBackAction;

    private Label _floorLabel = null!;
    private Label _themeLabel = null!;
    private Label _modeLabel = null!;
    private Label _turnLabel = null!;
    private PanelContainer _intentBand = null!;
    private Label _intentLabel = null!;
    private Label _healthLabel = null!;
    private Label _resourceLabel = null!;
    private Label _messageLabel = null!;
    private Label _activityLabel = null!;
    private HBoxContainer _hotbar = null!;
    private string _hotbarSignature = "";

    private readonly List<string> _raceNames = new();
    private readonly List<string> _classNames = new();
    private readonly List<SaveSummary> _saveSummaries = new();
    private string? _pendingDeleteId;

    public bool IsModalOpen => _modal.Visible;
    public bool IsFrontEndOpen => _frontEnd.Visible;

    public event Action? NewGameRequested;
    public event Action? ContinueRequested;
    public event Action<string, string, string>? CharacterCreated;
    public event Action<string>? SaveLoadRequested;
    public event Action<string>? SaveDeleteRequested;
    public event Action? BackToTitleRequested;
    public event Action? SaveRequested;
    public event Action? QuitToTitleRequested;
    public event Action? QuitToDesktopRequested;
    public event Action? DiveAgainRequested;
    public event Action<ClientSettings>? SettingsChanged;
    public event Action? ModalOpened;
    public event Action? ModalClosed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        BuildLayers();
        BuildHud();
    }

    public void ShowTitle(bool hasSaves)
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);

        var center = FullRect(new CenterContainer());
        var content = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(420, 0)
        };
        content.AddThemeConstantOverride("separation", 12);
        content.AddChild(LabelOf("THE MAZE RPG", 50, Gold, HorizontalAlignment.Center));
        content.AddChild(LabelOf("DESCEND. ADAPT. SURVIVE.", 14, Muted, HorizontalAlignment.Center));
        content.AddChild(Spacer(26));

        var newGame = CommandButton("NEW CHARACTER", Gold, 330);
        newGame.Pressed += () => NewGameRequested?.Invoke();
        content.AddChild(newGame);

        var continueButton = CommandButton("CONTINUE", Blue, 330);
        continueButton.Disabled = !hasSaves;
        continueButton.Pressed += () => ContinueRequested?.Invoke();
        content.AddChild(continueButton);

        var codex = CommandButton("CODEX", Green, 330);
        codex.Pressed += () => ShowCodex();
        content.AddChild(codex);

        var settings = CommandButton("SETTINGS", Blue, 330);
        settings.Pressed += () => ShowSettings(GetClientSettings());
        content.AddChild(settings);

        var exit = CommandButton("EXIT", Muted, 330);
        exit.Pressed += () => QuitToDesktopRequested?.Invoke();
        content.AddChild(exit);

        center.AddChild(content);
        _frontEnd.AddChild(center);
    }

    public void ShowCharacterCreation(CharacterDataService data)
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);

        _raceNames.Clear();
        _raceNames.AddRange(data.Races.Keys.OrderBy(name => name));
        _classNames.Clear();
        _classNames.AddRange(data.Classes.Keys.OrderBy(name => name));

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(800, 590) };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(LabelOf("CREATE CHARACTER", 28, Gold, HorizontalAlignment.Center));

        var name = new LineEdit
        {
            Text = "Wayfarer",
            PlaceholderText = "Character name",
            MaxLength = 24,
            CustomMinimumSize = new Vector2(0, 42)
        };
        name.AddThemeFontSizeOverride("font_size", 16);
        body.AddChild(Labeled("NAME", name));

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 16);
        var raceList = SelectionList(_raceNames, out VBoxContainer raceColumn, "RACE");
        var classList = SelectionList(_classNames, out VBoxContainer classColumn, "CLASS");
        var raceDetail = DetailLabel();
        var classDetail = DetailLabel();
        raceColumn.AddChild(raceDetail);
        classColumn.AddChild(classDetail);
        columns.AddChild(raceColumn);
        columns.AddChild(classColumn);
        body.AddChild(columns);

        int humanIndex = Math.Max(0, _raceNames.IndexOf("Human"));
        int wandererIndex = Math.Max(0, _classNames.IndexOf("Wanderer"));
        raceList.Select(humanIndex);
        classList.Select(wandererIndex);

        void UpdateRace(int index)
        {
            CharacterRace race = data.Races[_raceNames[index]];
            string effectiveness = string.Join("  ", race.Effectiveness
                .Where(pair => Math.Abs(pair.Value - 1f) >= 0.05f)
                .Select(pair => $"{Abbreviate(pair.Key)} {pair.Value:0.##}x"));
            raceDetail.Text = race.Description + (effectiveness.Length > 0 ? $"\n{effectiveness}" : "\nBalanced effectiveness");
        }

        void UpdateClass(int index)
        {
            CharacterClass characterClass = data.Classes[_classNames[index]];
            classDetail.Text = characterClass.Description + "\n" + string.Join("  ",
                characterClass.StartingStats.Select(pair => $"{Abbreviate(pair.Key)} {pair.Value}"));
        }

        raceList.ItemSelected += index => UpdateRace((int)index);
        classList.ItemSelected += index => UpdateClass((int)index);
        UpdateRace(humanIndex);
        UpdateClass(wandererIndex);

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 150);
        back.Pressed += () => BackToTitleRequested?.Invoke();
        var begin = CommandButton("BEGIN DIVE", Gold, 200);
        begin.Pressed += () =>
        {
            string heroName = string.IsNullOrWhiteSpace(name.Text) ? "Wayfarer" : name.Text.Trim();
            int raceIndex = SelectedIndex(raceList, humanIndex);
            int classIndex = SelectedIndex(classList, wandererIndex);
            CharacterCreated?.Invoke(heroName, _classNames[classIndex], _raceNames[raceIndex]);
        };
        actions.AddChild(back);
        actions.AddChild(begin);
        body.AddChild(actions);

        AddFrontPanel(body, new Vector2(860, 650));
    }

    public void ShowSaves(IReadOnlyList<SaveSummary> saves)
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);
        _pendingDeleteId = null;
        _saveSummaries.Clear();
        _saveSummaries.AddRange(saves);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(680, 460) };
        body.AddThemeConstantOverride("separation", 12);
        body.AddChild(LabelOf("CONTINUE", 28, Blue, HorizontalAlignment.Center));

        var list = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            CustomMinimumSize = new Vector2(0, 300)
        };
        list.AddThemeFontSizeOverride("font_size", 16);
        foreach (SaveSummary save in saves)
            list.AddItem($"{save.HeroName}     {save.RaceName} {save.ClassName}     LEVEL {save.Level}     {save.PlaytimeDisplay}");
        if (saves.Count > 0) list.Select(0);
        body.AddChild(list);

        var status = LabelOf(saves.Count == 0 ? "No characters found." : "", 13, Muted, HorizontalAlignment.Center);
        body.AddChild(status);
        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 130);
        back.Pressed += () => BackToTitleRequested?.Invoke();
        var delete = CommandButton("DELETE", Red, 150);
        delete.Disabled = saves.Count == 0;
        var load = CommandButton("LOAD", Blue, 160);
        load.Disabled = saves.Count == 0;

        load.Pressed += () =>
        {
            int selected = SelectedIndex(list, -1);
            if (selected >= 0) SaveLoadRequested?.Invoke(_saveSummaries[selected].SaveId);
        };
        delete.Pressed += () =>
        {
            int selected = SelectedIndex(list, -1);
            if (selected < 0) return;
            string id = _saveSummaries[selected].SaveId;
            if (_pendingDeleteId == id)
            {
                SaveDeleteRequested?.Invoke(id);
                return;
            }
            _pendingDeleteId = id;
            delete.Text = "CONFIRM DELETE";
            status.Text = $"Delete {_saveSummaries[selected].HeroName}? This cannot be undone.";
        };
        list.ItemSelected += _ =>
        {
            _pendingDeleteId = null;
            delete.Text = "DELETE";
            status.Text = "";
        };
        actions.AddChild(back);
        actions.AddChild(delete);
        actions.AddChild(load);
        body.AddChild(actions);
        AddFrontPanel(body, new Vector2(740, 540));
    }

    public void ShowGame(GameState state)
    {
        CloseModal(false);
        _frontEnd.Visible = false;
        _frontShade.Visible = false;
        _hud.Visible = true;
        _hotbarSignature = "";
        RefreshGame(state);
    }

    public void RefreshGame(GameState state)
    {
        if (!_hud.Visible) return;
        Hero hero = state.Hero;
        _floorLabel.Text = $"FLOOR {state.CurrentFloor:00}";
        _themeLabel.Text = state.CurrentMaze.Dungeon?.Theme.ToString().ToUpperInvariant() ?? "SANCTUARY";
        _healthLabel.Text = $"HP {hero.CurrentHp}/{hero.MaxHp}";
        _resourceLabel.Text = $"ST {hero.CurrentStamina}/{hero.MaxStamina}   MP {hero.CurrentMana}/{hero.MaxMana}";

        if (state.SimulationMode == SimulationMode.TurnBased)
        {
            TacticalTurnState turn = state.TacticalTurn;
            int potions = hero.Inventory.OfType<Item>().Count(item => item.UseEffect == ItemUseEffect.RestoreHealth);
            _turnLabel.Visible = true;
            if (turn.IsPlayerTurn)
            {
                _modeLabel.Text = $"TURN {turn.TurnNumber:00}";
                _turnLabel.Text = $"MOVE {turn.MovementRemaining}/{turn.MovementAllowance}   ACTION {Availability(turn.ActionAvailable)}   BONUS {Availability(turn.BonusActionAvailable)}   POTION {potions}";
            }
            else
            {
                _modeLabel.Text = turn.Phase switch
                {
                    TacticalPhase.PlayerEffects => "RESOLVING",
                    TacticalPhase.EnemyActions => "ENEMY PHASE",
                    _ => "WORLD PHASE"
                };
                int currentAction = Math.Clamp(turn.EnemyActionsTotal - turn.EnemyActionsRemaining,
                    1, Math.Max(1, turn.EnemyActionsTotal));
                _turnLabel.Text = turn.Phase == TacticalPhase.EnemyActions
                    ? $"ACT {currentAction}/{turn.EnemyActionsTotal}   {turn.LastEnemyAction.ToUpperInvariant()}"
                    : turn.LastEnemyAction.ToUpperInvariant();
            }
            _intentBand.Visible = turn.IsPlayerTurn && turn.EnemyIntents.Count > 0;
            if (_intentBand.Visible)
            {
                string order = string.Join("   ", turn.EnemyIntents.Take(3).Select(FormatIntent));
                int hidden = Math.Max(0, turn.EnemyIntents.Count - 3);
                _intentLabel.Text = $"ENEMY ORDER   {order}{(hidden > 0 ? $"   +{hidden}" : "")}";
            }
        }
        else
        {
            _modeLabel.Text = hero.InCombat ? "ENGAGED" : "REAL-TIME";
            _turnLabel.Visible = false;
            _intentBand.Visible = false;
        }

        _messageLabel.Text = string.Join("\n", state.Messages.Messages.TakeLast(4).Select(message => message.Text));
        _activityLabel.Visible = state.CurrentActivity != null;
        _activityLabel.Text = state.CurrentActivity == null
            ? ""
            : $"{state.CurrentActivity.Name.ToUpperInvariant()}   {state.CurrentActivity.TicksRemaining}";
        RefreshHotbar(state);
    }

    public void ShowPause(GameState state)
    {
        var body = ModalBody("PAUSED", Gold, new Vector2(330, 0));
        var resume = CommandButton("RESUME", Green, 270);
        resume.Pressed += () => CloseModal();
        body.AddChild(resume);

        var save = CommandButton("SAVE", Blue, 270);
        save.Disabled = !state.CanSave;
        save.Pressed += () => SaveRequested?.Invoke();
        body.AddChild(save);
        if (!state.CanSave)
            body.AddChild(LabelOf("Saving is available in safe rooms.", 12, Muted, HorizontalAlignment.Center));

        var settings = CommandButton("SETTINGS", Blue, 270);
        settings.Pressed += () => ShowSettings(GetClientSettings(), state);
        body.AddChild(settings);

        var title = CommandButton("QUIT TO TITLE", Muted, 270);
        title.Pressed += () => QuitToTitleRequested?.Invoke();
        body.AddChild(title);
        var desktop = CommandButton("EXIT GAME", Red, 270);
        desktop.Pressed += () => QuitToDesktopRequested?.Invoke();
        body.AddChild(desktop);
        ShowModal(body, new Vector2(390, 440));
    }

    public void ShowCharacterSheet(GameState state)
    {
        Hero hero = state.Hero;
        var body = ModalBody("CHARACTER", Gold, new Vector2(630, 520));
        body.AddChild(LabelOf($"{hero.Name.ToUpperInvariant()}     LEVEL {hero.Level}     {hero.Race.ToUpperInvariant()} {hero.Class.ToUpperInvariant()}",
            15, Text, HorizontalAlignment.Center));
        body.AddChild(LabelOf($"XP {hero.Experience}/{hero.ExperienceToNext}     POINTS {hero.UnspentStatPoints}",
            13, hero.UnspentStatPoints > 0 ? Gold : Muted, HorizontalAlignment.Center));
        body.AddChild(Spacer(4));

        var statGrid = new GridContainer { Columns = 4, SizeFlagsVertical = SizeFlags.ExpandFill };
        statGrid.AddThemeConstantOverride("h_separation", 18);
        statGrid.AddThemeConstantOverride("v_separation", 5);
        statGrid.AddChild(LabelOf("ATTRIBUTE", 12, Muted));
        statGrid.AddChild(LabelOf("BASE", 12, Muted, HorizontalAlignment.Right));
        statGrid.AddChild(LabelOf("EFFECTIVE", 12, Muted, HorizontalAlignment.Right));
        statGrid.AddChild(LabelOf("", 12, Muted));
        foreach (string stat in GameState.CoreStatNames)
        {
            statGrid.AddChild(LabelOf(stat.ToUpperInvariant(), 14, Text));
            statGrid.AddChild(LabelOf(BaseStat(hero, stat).ToString(), 14, Text, HorizontalAlignment.Right));
            statGrid.AddChild(LabelOf(EffectiveStat(hero, stat).ToString("0.##"), 14, Blue, HorizontalAlignment.Right));
            var add = SmallButton("+", Gold);
            add.CustomMinimumSize = new Vector2(38, 32);
            add.Disabled = hero.UnspentStatPoints <= 0;
            add.TooltipText = $"Spend one point on {stat}";
            string selectedStat = stat;
            add.Pressed += () =>
            {
                if (state.SpendStatPoint(selectedStat)) ShowCharacterSheet(state);
            };
            statGrid.AddChild(add);
        }
        body.AddChild(statGrid);
        body.AddChild(LabelOf(
            $"HP {hero.CurrentHp}/{hero.MaxHp}     STAMINA {hero.CurrentStamina}/{hero.MaxStamina}     MANA {hero.CurrentMana}/{hero.MaxMana}     FAITH {hero.CurrentFaith}/{hero.MaxFaith}",
            13, Muted, HorizontalAlignment.Center));
        body.AddChild(LabelOf($"TACTICAL MOVE {state.CalculateTacticalMovementAllowance()}     GOLD {hero.Gold}",
            13, Green, HorizontalAlignment.Center));
        var close = CommandButton("CLOSE", Green, 160);
        close.Pressed += () => CloseModal();
        body.AddChild(Centered(close));
        ShowModal(body, new Vector2(700, 650));
    }

    public void ShowCodex(bool playStats = false)
    {
        CodexData data = CodexService.Instance.Data;
        var body = ModalBody("CODEX", Green, new Vector2(680, 500));
        var tabs = ActionsRow();
        var bestiaryTab = CommandButton("BESTIARY", playStats ? Muted : Gold, 180);
        bestiaryTab.Pressed += () => ShowCodex(false);
        var statsTab = CommandButton("PLAY STATS", playStats ? Gold : Muted, 180);
        statsTab.Pressed += () => ShowCodex(true);
        tabs.AddChild(bestiaryTab);
        tabs.AddChild(statsTab);
        body.AddChild(tabs);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360)
        };
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 7);
        if (playStats)
        {
            var stats = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            stats.AddThemeConstantOverride("h_separation", 24);
            stats.AddThemeConstantOverride("v_separation", 10);
            AddCodexStat(stats, "TOTAL KILLS", data.PlayStats.TotalKills);
            AddCodexStat(stats, "TOTAL DEATHS", data.PlayStats.TotalDeaths);
            AddCodexStat(stats, "DEEPEST FLOOR", data.PlayStats.DeepestFloor);
            AddCodexStat(stats, "FLOORS CLEARED", data.PlayStats.TotalFloorsCleared);
            AddCodexStat(stats, "DUNGEON EXITS", data.PlayStats.TotalDungeonExits);
            AddCodexStat(stats, "CREATURES DISCOVERED", data.Bestiary.Count);
            content.AddChild(stats);
        }
        else
        {
            List<BestiaryEntry> entries = data.Bestiary.Values
                .OrderBy(entry => entry.FirstFloor)
                .ThenBy(entry => entry.Name)
                .ToList();
            if (entries.Count == 0)
            {
                content.AddChild(LabelOf("No creatures encountered yet.", 14, Muted, HorizontalAlignment.Center));
            }
            foreach (BestiaryEntry entry in entries)
            {
                string floors = entry.FirstFloor == entry.LastFloor
                    ? $"FLOOR {entry.FirstFloor}"
                    : $"FLOORS {entry.FirstFloor}-{entry.LastFloor}";
                var entryBody = new VBoxContainer();
                entryBody.AddThemeConstantOverride("separation", 2);
                entryBody.AddChild(LabelOf(entry.Name.ToUpperInvariant(), 15, Text));
                entryBody.AddChild(LabelOf($"SEEN {entry.Seen}     KILLED {entry.Killed}     {floors}", 12, Muted));
                var panel = new PanelContainer();
                panel.AddThemeStyleboxOverride("panel", Box(PanelRaised, Border, 3, 1));
                var margin = Margin(10, 7, 10, 7);
                margin.AddChild(entryBody);
                panel.AddChild(margin);
                content.AddChild(panel);
            }
        }
        scroll.AddChild(content);
        body.AddChild(scroll);
        var close = CommandButton("CLOSE", Green, 160);
        close.Pressed += () => CloseModal();
        body.AddChild(Centered(close));
        ShowModal(body, new Vector2(750, 640));
    }

    public void ShowCombination(GameState state, string resultMessage = "")
    {
        List<Combinable> owned = state.Hero.Loadout.Concat(state.Hero.Inventory).ToList();
        var body = ModalBody("PORTABLE SYNTHESIS", Gold, new Vector2(780, 500));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        VBoxContainer firstColumn = ListColumn("FIRST COMPONENT", out ItemList firstList);
        VBoxContainer secondColumn = ListColumn("SECOND COMPONENT", out ItemList secondList);
        foreach (Combinable item in owned)
        {
            firstList.AddItem(ItemLabel(item));
            secondList.AddItem(ItemLabel(item));
        }
        if (owned.Count > 0) firstList.Select(0);
        if (owned.Count > 1) secondList.Select(1);
        columns.AddChild(firstColumn);
        columns.AddChild(secondColumn);
        body.AddChild(columns);

        var preview = DetailLabel(105);
        preview.Text = resultMessage.Length > 0 ? resultMessage : "Select two components.";
        body.AddChild(preview);
        var actions = ActionsRow();
        var inventory = CommandButton("INVENTORY", Muted, 160);
        inventory.Pressed += () => ShowInventory(state);
        var combine = CommandButton("COMBINE", Gold, 180);
        combine.Disabled = true;
        var close = CommandButton("CLOSE", Green, 150);
        close.Pressed += () => CloseModal();
        actions.AddChild(inventory);
        actions.AddChild(combine);
        actions.AddChild(close);
        body.AddChild(actions);

        void UpdatePreview()
        {
            int firstIndex = SelectedIndex(firstList, -1);
            int secondIndex = SelectedIndex(secondList, -1);
            if (firstIndex < 0 || secondIndex < 0)
            {
                combine.Disabled = true;
                preview.Text = owned.Count < 2 ? "At least two components are required." : "Select two components.";
                return;
            }
            Combinable first = owned[firstIndex];
            Combinable second = owned[secondIndex];
            if (!CombinationEngine.CanCombine(first, second, CombineLocation.Anywhere, out string reason))
            {
                combine.Disabled = true;
                preview.Text = reason;
                return;
            }
            Combinable result = CombinationEngine.Combine(first, second);
            combine.Disabled = false;
            preview.Text = "RESULT\n" + ItemDetails(result);
        }

        firstList.ItemSelected += _ => UpdatePreview();
        secondList.ItemSelected += _ => UpdatePreview();
        combine.Pressed += () =>
        {
            int firstIndex = SelectedIndex(firstList, -1);
            int secondIndex = SelectedIndex(secondList, -1);
            if (firstIndex < 0 || secondIndex < 0) return;
            Combinable? result = state.CombineOwned(owned[firstIndex], owned[secondIndex],
                CombineLocation.Anywhere, out string reason);
            ShowCombination(state, result == null ? reason : $"CREATED\n{ItemDetails(result)}");
            RefreshGame(state);
        };
        UpdatePreview();
        ShowModal(body, new Vector2(850, 650), backAction: () => ShowInventory(state));
    }

    public void ShowSettings(ClientSettings settings, GameState? returnToPause = null)
    {
        var body = ModalBody("SETTINGS", Blue, new Vector2(480, 300));

        var fullscreen = SettingsToggle("FULLSCREEN", settings.Fullscreen);
        var smoothing = SettingsToggle("CAMERA SMOOTHING", settings.CameraSmoothing);
        var shake = SettingsToggle("SCREEN SHAKE", settings.ScreenShake);
        var vsync = SettingsToggle("V-SYNC", settings.VSync);
        body.AddChild(fullscreen);
        body.AddChild(smoothing);
        body.AddChild(shake);
        body.AddChild(vsync);

        var volumeRow = new HBoxContainer();
        var volumeText = LabelOf($"MASTER VOLUME     {Math.Round(settings.MasterVolume * 100):0}%", 14, Text);
        volumeText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var volume = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 5,
            Value = settings.MasterVolume * 100,
            CustomMinimumSize = new Vector2(220, 32)
        };
        volumeRow.AddChild(volumeText);
        volumeRow.AddChild(volume);
        body.AddChild(volumeRow);

        void Apply()
        {
            settings.Fullscreen = fullscreen.ButtonPressed;
            settings.CameraSmoothing = smoothing.ButtonPressed;
            settings.ScreenShake = shake.ButtonPressed;
            settings.VSync = vsync.ButtonPressed;
            settings.MasterVolume = (float)volume.Value / 100f;
            settings.Save();
            SettingsChanged?.Invoke(settings);
            volumeText.Text = $"MASTER VOLUME     {Math.Round(settings.MasterVolume * 100):0}%";
        }

        fullscreen.Toggled += _ => Apply();
        smoothing.Toggled += _ => Apply();
        shake.Toggled += _ => Apply();
        vsync.Toggled += _ => Apply();
        volume.ValueChanged += _ => Apply();

        var actions = ActionsRow();
        var defaults = CommandButton("DEFAULTS", Muted, 150);
        defaults.Pressed += () =>
        {
            settings.Fullscreen = false;
            settings.CameraSmoothing = true;
            settings.ScreenShake = true;
            settings.VSync = true;
            settings.MasterVolume = 0.8f;
            settings.Save();
            SettingsChanged?.Invoke(settings);
            ShowSettings(settings, returnToPause);
        };
        var close = CommandButton("CLOSE", Green, 150);
        close.Pressed += () =>
        {
            if (returnToPause != null) ShowPause(returnToPause);
            else CloseModal();
        };
        actions.AddChild(defaults);
        actions.AddChild(close);
        body.AddChild(actions);
        ShowModal(body, new Vector2(550, 420), backAction: returnToPause == null ? null : () => ShowPause(returnToPause));
    }

    public void ShowInventory(GameState state)
    {
        var body = ModalBody("INVENTORY & LOADOUT", Gold, new Vector2(810, 510));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);

        VBoxContainer loadoutColumn = ListColumn("EQUIPPED", out ItemList equipped);
        VBoxContainer inventoryColumn = ListColumn("BACKPACK", out ItemList inventory);
        var details = DetailLabel(86);
        var status = LabelOf("", 12, Muted, HorizontalAlignment.Center);

        void Populate()
        {
            equipped.Clear();
            inventory.Clear();
            foreach (Combinable item in state.Hero.Loadout)
                equipped.AddItem(ItemLabel(item));
            foreach (Combinable item in state.Hero.Inventory)
                inventory.AddItem(ItemLabel(item));
            details.Text = "Select an item to inspect it.";
            status.Text = $"HOTBAR {state.Hero.Loadout.Count}/{state.Hero.HotbarCapacity}     GOLD {state.Hero.Gold}";
            _hotbarSignature = "";
            RefreshGame(state);
        }

        equipped.ItemSelected += index => details.Text = ItemDetails(state.Hero.Loadout[(int)index]);
        inventory.ItemSelected += index => details.Text = ItemDetails(state.Hero.Inventory[(int)index]);

        var unequip = CommandButton("UNEQUIP", Muted, 140);
        unequip.Pressed += () =>
        {
            int index = SelectedIndex(equipped, -1);
            if (index >= 0 && state.UnequipToInventory(state.Hero.Loadout[index])) Populate();
        };
        var equip = CommandButton("EQUIP", Blue, 140);
        equip.Pressed += () =>
        {
            int index = SelectedIndex(inventory, -1);
            if (index < 0) return;
            Combinable item = state.Hero.Inventory[index];
            if (!state.EquipFromInventory(item))
                details.Text = item is Weapon or Spell ? "The hotbar is full." : "This item is not an attack.";
            else Populate();
        };
        loadoutColumn.AddChild(unequip);
        inventoryColumn.AddChild(equip);
        columns.AddChild(loadoutColumn);
        columns.AddChild(inventoryColumn);
        body.AddChild(columns);
        body.AddChild(status);
        body.AddChild(details);
        var actions = ActionsRow();
        var synthesize = CommandButton("SYNTHESIZE", Gold, 170);
        synthesize.Pressed += () => ShowCombination(state);
        var close = CommandButton("CLOSE", Green, 160);
        close.Pressed += () => CloseModal();
        actions.AddChild(synthesize);
        actions.AddChild(close);
        body.AddChild(actions);
        Populate();
        ShowModal(body, new Vector2(880, 650));
    }

    public void ShowLoot(GameState state, Enemy corpse)
    {
        var body = ModalBody($"LOOT: {corpse.Race.ToUpperInvariant()} {corpse.Class.ToUpperInvariant()}", Gold, new Vector2(810, 500));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        VBoxContainer corpseColumn = ListColumn("BODY", out ItemList corpseItems);
        VBoxContainer heroColumn = ListColumn("YOUR BACKPACK", out ItemList heroItems);
        var details = DetailLabel(80);
        var status = LabelOf("", 12, Muted, HorizontalAlignment.Center);

        void Populate()
        {
            corpseItems.Clear();
            heroItems.Clear();
            foreach (Combinable item in corpse.Inventory) corpseItems.AddItem(ItemLabel(item));
            foreach (Combinable item in state.Hero.Inventory) heroItems.AddItem(ItemLabel(item));
            status.Text = $"BODY GOLD {corpse.Gold}     YOUR GOLD {state.Hero.Gold}";
            details.Text = "Select an item to inspect it.";
        }

        corpseItems.ItemSelected += index => details.Text = ItemDetails(corpse.Inventory[(int)index]);
        heroItems.ItemSelected += index => details.Text = ItemDetails(state.Hero.Inventory[(int)index]);
        var take = CommandButton("TAKE", Blue, 130);
        take.Pressed += () =>
        {
            int index = SelectedIndex(corpseItems, -1);
            if (index >= 0 && state.LootItem(corpse, corpse.Inventory[index])) Populate();
        };
        var returnItem = CommandButton("RETURN", Muted, 130);
        returnItem.Pressed += () =>
        {
            int index = SelectedIndex(heroItems, -1);
            if (index >= 0 && state.DepositToCorpse(corpse, state.Hero.Inventory[index])) Populate();
        };
        corpseColumn.AddChild(take);
        heroColumn.AddChild(returnItem);
        columns.AddChild(corpseColumn);
        columns.AddChild(heroColumn);
        body.AddChild(columns);
        body.AddChild(status);
        body.AddChild(details);

        var actions = ActionsRow();
        var all = CommandButton("LOOT ALL", Gold, 170);
        all.Pressed += () => { state.LootAll(corpse); Populate(); RefreshGame(state); };
        var close = CommandButton("CLOSE", Green, 150);
        close.Pressed += () => CloseModal();
        actions.AddChild(all);
        actions.AddChild(close);
        body.AddChild(actions);
        Populate();
        ShowModal(body, new Vector2(880, 650));
    }

    public void ShowCorpseActions(GameState state, Enemy corpse)
    {
        var body = ModalBody("FALLEN ENEMY", Gold, new Vector2(330, 0));
        body.AddChild(LabelOf($"Level {corpse.Level} {corpse.Race} {corpse.Class}\n{corpse.Inventory.Count} item(s), {corpse.Gold} gold", 14, Text, HorizontalAlignment.Center));
        var examine = CommandButton("EXAMINE", Muted, 260);
        examine.Pressed += () => { state.ExamineCorpse(corpse); CloseModal(); };
        body.AddChild(examine);
        var loot = CommandButton("LOOT", Blue, 260);
        loot.Disabled = corpse.Inventory.Count == 0 && corpse.Gold == 0;
        loot.Pressed += () => ShowLoot(state, corpse);
        body.AddChild(loot);
        var close = CommandButton("CLOSE", Green, 260);
        close.Pressed += () => CloseModal();
        body.AddChild(close);
        ShowModal(body, new Vector2(390, 340));
    }

    public void ShowTrapActions(GameState state, MazeFeature trap)
    {
        var body = ModalBody("TRAP", Red, new Vector2(320, 0));
        var examine = CommandButton("EXAMINE", Muted, 250);
        examine.Pressed += () => { state.ExamineFeature(trap); CloseModal(); };
        body.AddChild(examine);
        var disarm = CommandButton("DISARM", Blue, 250);
        disarm.Disabled = !trap.Perceived;
        disarm.Pressed += () => { state.TryDisarm(trap); CloseModal(); };
        body.AddChild(disarm);
        var close = CommandButton("CLOSE", Green, 250);
        close.Pressed += () => CloseModal();
        body.AddChild(close);
        ShowModal(body, new Vector2(370, 300));
    }

    public void ShowDeath(GameState state)
    {
        var body = ModalBody("YOU HAVE FALLEN", Red, new Vector2(390, 0));
        body.AddChild(LabelOf($"{state.Hero.Name}\nFloor {state.CurrentFloor}\nThe character save has been erased.", 16, Text, HorizontalAlignment.Center));
        var newHero = CommandButton("NEW CHARACTER", Gold, 290);
        newHero.Pressed += () => NewGameRequested?.Invoke();
        body.AddChild(newHero);
        var title = CommandButton("TITLE", Muted, 290);
        title.Pressed += () => BackToTitleRequested?.Invoke();
        body.AddChild(title);
        ShowModal(body, new Vector2(450, 360), false);
    }

    public void ShowDungeonExit(GameState state)
    {
        var body = ModalBody("DIVE COMPLETE", Green, new Vector2(390, 0));
        body.AddChild(LabelOf($"{state.Hero.Name.ToUpperInvariant()} RETURNS\nLEVEL {state.Hero.Level}     GOLD {state.Hero.Gold}",
            16, Text, HorizontalAlignment.Center));
        var again = CommandButton("DESCEND AGAIN", Gold, 290);
        again.Pressed += () => DiveAgainRequested?.Invoke();
        body.AddChild(again);
        var title = CommandButton("RETURN TO TITLE", Muted, 290);
        title.Pressed += () => QuitToTitleRequested?.Invoke();
        body.AddChild(title);
        ShowModal(body, new Vector2(450, 340));
    }

    public void MarkSaved()
    {
        _messageLabel.Text = "Progress saved.";
    }

    public void CloseModal(bool notify = true)
    {
        if (!_modal.Visible) return;
        _modal.Visible = false;
        _modalBackAction = null;
        ClearChildren(_modalCenter);
        if (notify) ModalClosed?.Invoke();
    }

    public void HandleModalBack()
    {
        Action? back = _modalBackAction;
        if (back == null) CloseModal();
        else
        {
            _modalBackAction = null;
            back();
        }
    }

    private void BuildLayers()
    {
        _frontShade = FullRect(new ColorRect { Color = new Color(0.02f, 0.025f, 0.03f, 0.84f), MouseFilter = MouseFilterEnum.Ignore });
        AddChild(_frontShade);

        _frontEnd = FullRect(new Control { MouseFilter = MouseFilterEnum.Stop });
        AddChild(_frontEnd);

        _hud = FullRect(new Control { MouseFilter = MouseFilterEnum.Ignore });
        AddChild(_hud);

        _modal = FullRect(new ColorRect { Color = new Color(0f, 0f, 0f, 0.78f), MouseFilter = MouseFilterEnum.Stop });
        _modalCenter = FullRect(new CenterContainer());
        _modal.AddChild(_modalCenter);
        _modal.Visible = false;
        AddChild(_modal);
    }

    private void BuildHud()
    {
        var top = new PanelContainer
        {
            AnchorRight = 1,
            OffsetBottom = 78,
            MouseFilter = MouseFilterEnum.Pass
        };
        top.AddThemeStyleboxOverride("panel", Box(Ink with { A = 0.95f }, Border, 0, bottom: 1));
        var margin = Margin(18, 8, 18, 8);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        var primary = new HBoxContainer();
        primary.AddThemeConstantOverride("separation", 20);
        _floorLabel = LabelOf("FLOOR 01", 15, Gold);
        _themeLabel = LabelOf("CASTLE", 15, Muted);
        _themeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _modeLabel = LabelOf("REAL-TIME", 15, Green);
        _turnLabel = LabelOf("", 14, Blue);
        _turnLabel.Visible = false;
        _healthLabel = LabelOf("HP 100/100", 15, Red);
        _resourceLabel = LabelOf("ST 100/100   MP 100/100", 13, Muted);
        var inventory = SmallButton("INVENTORY", Blue);
        inventory.Pressed += () => { if (GetGameState() is { } state) ShowInventory(state); };
        var character = SmallButton("CHARACTER", Gold);
        character.Pressed += () => { if (GetGameState() is { } state) ShowCharacterSheet(state); };
        var codex = SmallButton("CODEX", Green);
        codex.Pressed += () => ShowCodex();
        var pause = SmallButton("PAUSE", Muted);
        pause.Pressed += () => { if (GetGameState() is { } state) ShowPause(state); };
        primary.AddChild(_floorLabel);
        primary.AddChild(_themeLabel);
        primary.AddChild(_modeLabel);
        primary.AddChild(_healthLabel);
        primary.AddChild(inventory);
        primary.AddChild(character);
        primary.AddChild(codex);
        primary.AddChild(pause);

        var secondary = new HBoxContainer();
        _turnLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        secondary.AddChild(_turnLabel);
        secondary.AddChild(_resourceLabel);
        stack.AddChild(primary);
        stack.AddChild(secondary);
        margin.AddChild(stack);
        top.AddChild(margin);
        _hud.AddChild(top);

        _intentBand = new PanelContainer
        {
            AnchorRight = 1,
            OffsetTop = 78,
            OffsetBottom = 106,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _intentBand.AddThemeStyleboxOverride("panel", Box(Panel with { A = 0.94f }, Border, 0, bottom: 1));
        var intentMargin = Margin(18, 4, 18, 3);
        _intentLabel = LabelOf("", 12, Blue);
        _intentLabel.ClipText = true;
        intentMargin.AddChild(_intentLabel);
        _intentBand.AddChild(intentMargin);
        _hud.AddChild(_intentBand);

        var bottom = new VBoxContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = -410,
            OffsetRight = 410,
            OffsetTop = -118,
            OffsetBottom = -14,
            Alignment = BoxContainer.AlignmentMode.End
        };
        bottom.AddThemeConstantOverride("separation", 5);
        _activityLabel = LabelOf("", 13, Blue, HorizontalAlignment.Center);
        _activityLabel.Visible = false;
        _hotbar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _hotbar.AddThemeConstantOverride("separation", 6);
        bottom.AddChild(_activityLabel);
        bottom.AddChild(_hotbar);
        _hud.AddChild(bottom);

        var messagePanel = new PanelContainer
        {
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = 16,
            OffsetRight = 390,
            OffsetTop = -118,
            OffsetBottom = -14,
            MouseFilter = MouseFilterEnum.Ignore
        };
        messagePanel.AddThemeStyleboxOverride("panel", Box(new Color(0.02f, 0.025f, 0.03f, 0.82f), Border, 3));
        var messageMargin = Margin(12, 8, 12, 8);
        _messageLabel = LabelOf("", 13, Text);
        _messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _messageLabel.VerticalAlignment = VerticalAlignment.Bottom;
        messageMargin.AddChild(_messageLabel);
        messagePanel.AddChild(messageMargin);
        _hud.AddChild(messagePanel);
        _hud.Visible = false;
    }

    private GameState? GetGameState() => GetParent()?.GetParent() is GameHost host ? host.State : null;
    private ClientSettings GetClientSettings() => GetParent()?.GetParent() is GameHost host
        ? host.Settings
        : new ClientSettings();

    private static void AddCodexStat(GridContainer grid, string name, int value)
    {
        var label = LabelOf(name, 15, Text);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddChild(label);
        grid.AddChild(LabelOf(value.ToString(), 16, Gold, HorizontalAlignment.Right));
    }

    private void RefreshHotbar(GameState state)
    {
        string signature = string.Join('|', state.Hero.Attacks.Select(attack => attack.Id)) + ":" + state.Hero.CurrentAttack?.Id;
        if (signature == _hotbarSignature) return;
        _hotbarSignature = signature;
        ClearChildren(_hotbar);
        for (int index = 0; index < state.Hero.Attacks.Count; index++)
        {
            int slot = index;
            Attack attack = state.Hero.Attacks[index];
            bool selected = ReferenceEquals(attack, state.Hero.CurrentAttack) || attack.Id == state.Hero.CurrentAttack?.Id;
            var button = SmallButton($"{index + 1}  {attack.Name.ToUpperInvariant()}", selected ? Gold : Muted);
            button.CustomMinimumSize = new Vector2(150, 38);
            button.TooltipText = attack.Description;
            button.Pressed += () => { state.SelectAttack(slot); _hotbarSignature = ""; RefreshGame(state); };
            _hotbar.AddChild(button);
        }
    }

    private void AddFrontPanel(Control content, Vector2 size)
    {
        var center = FullRect(new CenterContainer());
        var panel = new PanelContainer { CustomMinimumSize = size };
        panel.AddThemeStyleboxOverride("panel", Box(Panel with { A = 0.97f }, Border, 5, 1));
        var margin = Margin(28, 24, 28, 24);
        margin.AddChild(content);
        panel.AddChild(margin);
        center.AddChild(panel);
        _frontEnd.AddChild(center);
    }

    private void ShowModal(Control content, Vector2 size, bool notify = true, Action? backAction = null)
    {
        bool wasOpen = _modal.Visible;
        _modalBackAction = backAction;
        ClearChildren(_modalCenter);
        var panel = new PanelContainer { CustomMinimumSize = size };
        panel.AddThemeStyleboxOverride("panel", Box(Panel with { A = 0.99f }, Border, 5, 1));
        var margin = Margin(24, 22, 24, 22);
        margin.AddChild(content);
        panel.AddChild(margin);
        _modalCenter.AddChild(panel);
        _modal.Visible = true;
        if (notify && !wasOpen) ModalOpened?.Invoke();
    }

    private VBoxContainer ModalBody(string title, Color color, Vector2 minimum)
    {
        var body = new VBoxContainer { CustomMinimumSize = minimum };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(LabelOf(title, 24, color, HorizontalAlignment.Center));
        body.AddChild(Spacer(4));
        return body;
    }

    private static VBoxContainer ListColumn(string title, out ItemList list)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 7);
        column.AddChild(LabelOf(title, 14, Blue));
        list = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(360, 290),
            SelectMode = ItemList.SelectModeEnum.Single
        };
        list.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(list);
        return column;
    }

    private static ItemList SelectionList(List<string> names, out VBoxContainer column, string title)
    {
        column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(LabelOf(title, 14, Blue));
        var list = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(370, 250),
            SelectMode = ItemList.SelectModeEnum.Single
        };
        list.AddThemeFontSizeOverride("font_size", 15);
        foreach (string name in names) list.AddItem(name);
        column.AddChild(list);
        return list;
    }

    private static VBoxContainer Labeled(string label, Control control)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(LabelOf(label, 12, Muted));
        box.AddChild(control);
        return box;
    }

    private static HBoxContainer ActionsRow()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        return row;
    }

    private static CenterContainer Centered(Control control)
    {
        var center = new CenterContainer();
        center.AddChild(control);
        return center;
    }

    private static Label DetailLabel(float height = 92)
    {
        var label = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(0, height),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", Text);
        return label;
    }

    private static Label LabelOf(string value, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = value,
            HorizontalAlignment = align
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button CommandButton(string text, Color color, float width)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 42),
            FocusMode = FocusModeEnum.All
        };
        button.AddThemeFontSizeOverride("font_size", 15);
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeStyleboxOverride("normal", Box(PanelRaised, color with { A = 0.72f }, 4, 1));
        button.AddThemeStyleboxOverride("hover", Box(new Color("#2C353B"), color, 4, 1));
        button.AddThemeStyleboxOverride("pressed", Box(Ink, color, 4, 1));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Border with { A = 0.45f }, 4, 1));
        return button;
    }

    private static Button SmallButton(string text, Color color)
    {
        var button = CommandButton(text, color, 110);
        button.CustomMinimumSize = new Vector2(110, 32);
        button.AddThemeFontSizeOverride("font_size", 12);
        return button;
    }

    private static CheckButton SettingsToggle(string text, bool enabled)
    {
        var toggle = new CheckButton
        {
            Text = text,
            ButtonPressed = enabled,
            CustomMinimumSize = new Vector2(0, 38)
        };
        toggle.AddThemeFontSizeOverride("font_size", 14);
        toggle.AddThemeColorOverride("font_color", Text);
        toggle.AddThemeColorOverride("font_pressed_color", Green);
        return toggle;
    }

    private static MarginContainer Margin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static Control Spacer(float height) => new Control { CustomMinimumSize = new Vector2(1, height) };

    private static T FullRect<T>(T control) where T : Control
    {
        control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return control;
    }

    private static StyleBoxFlat Box(Color background, Color border, int radius, int width = 0, int bottom = -1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = bottom >= 0 ? bottom : width,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius
        };
        return box;
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static int SelectedIndex(ItemList list, int fallback)
    {
        int[] selected = list.GetSelectedItems();
        return selected.Length == 0 ? fallback : selected[0];
    }

    private static string ItemLabel(Combinable item) => $"{item.Name}   {item.Rarity} {item.Kind}";

    private static string ItemDetails(Combinable item)
    {
        string details = $"{item.Name}   {item.Rarity} {item.Kind}";
        if (!string.IsNullOrWhiteSpace(item.Description)) details += $"\n{item.Description}";
        if (item.Attributes.Count > 0) details += $"\n{string.Join(" / ", item.Attributes)}";
        return item switch
        {
            Weapon weapon => details + $"\nDamage {weapon.BaseDamage}   Range {weapon.Range:0.#}   Crit {weapon.CritChance:P0}   Stamina {weapon.StaminaCost}",
            Spell spell => details + $"\nDamage {spell.BaseDamage}   Range {spell.Range:0.#}   Crit {spell.CritChance:P0}   Mana {spell.ManaCost}",
            Item consumable when consumable.UseEffect == ItemUseEffect.RestoreHealth => details + $"\nRestores {consumable.EffectPower} health",
            _ => details
        };
    }

    private static string Abbreviate(string stat) => stat switch
    {
        "Strength" => "STR",
        "Constitution" => "CON",
        "Agility" => "AGI",
        "Dexterity" => "DEX",
        "Intelligence" => "INT",
        "Wisdom" => "WIS",
        "Charisma" => "CHA",
        _ => stat.ToUpperInvariant()
    };

    private static int BaseStat(Hero hero, string stat) => stat switch
    {
        "Strength" => hero.Strength,
        "Constitution" => hero.Constitution,
        "Agility" => hero.Agility,
        "Dexterity" => hero.Dexterity,
        "Intelligence" => hero.Intelligence,
        "Wisdom" => hero.Wisdom,
        "Charisma" => hero.Charisma,
        _ => 0
    };

    private static float EffectiveStat(Hero hero, string stat) => stat switch
    {
        "Strength" => hero.EffectiveStrength,
        "Constitution" => hero.EffectiveConstitution,
        "Agility" => hero.EffectiveAgility,
        "Dexterity" => hero.EffectiveDexterity,
        "Intelligence" => hero.EffectiveIntelligence,
        "Wisdom" => hero.EffectiveWisdom,
        "Charisma" => hero.EffectiveCharisma,
        _ => 0
    };

    private static string FormatIntent(TacticalEnemyIntent intent)
    {
        string action = intent.Kind switch
        {
            TacticalIntentKind.Attack => intent.Detail,
            TacticalIntentKind.Advance => "Advance",
            TacticalIntentKind.Reposition => "Retreat",
            _ => "Hold"
        };
        return $"{intent.Order} {intent.ActorName}: {action}".ToUpperInvariant();
    }

    private static string Availability(bool available) => available ? "READY" : "SPENT";
}

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
    private enum CharacterTab { Inventory, Stats, Progression, Hotbar }

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
    private VBoxContainer _topHud = null!;
    private PanelContainer _intentBand = null!;
    private Label _intentLabel = null!;
    private Label _healthLabel = null!;
    private Label _resourceLabel = null!;
    private Label _messageLabel = null!;
    private Label _activityLabel = null!;
    private PanelContainer _interactionToast = null!;
    private Label _interactionLabel = null!;
    private HBoxContainer _hotbar = null!;
    private PanelContainer _consolePanel = null!;
    private Label _consoleOutput = null!;
    private LineEdit _consoleInput = null!;
    private GameState? _consoleState;
    private VBoxContainer _bottomHud = null!;
    private PanelContainer _messagePanel = null!;
    private string _hotbarSignature = "";

    private readonly List<string> _raceNames = new();
    private readonly List<SaveSummary> _saveSummaries = new();
    private readonly List<WorldSummary> _worldSummaries = new();
    private string? _pendingDeleteId;

    public bool IsModalOpen => _modal.Visible;
    public bool IsFrontEndOpen => _frontEnd.Visible;
    public Vector2 PlayAreaInsets => new(GetTopHudInset(), GetBottomHudInset());

    public event Action? NewGameRequested;
    public event Action? ContinueRequested;
    public event Action<CharacterCreationSelection>? CharacterCreated;
    public event Action<string>? SaveLoadRequested;
    public event Action<string>? SaveDeleteRequested;
    public event Action? BackToTitleRequested;

    /// <summary>A world was chosen from the worlds screen — enter it and show its characters.</summary>
    public event Action<string>? WorldSelected;
    public event Action<WorldGenOptions, string>? WorldCreateRequested;
    public event Action<string>? WorldDeleteRequested;
    /// <summary>Back out of world creation to the worlds list.</summary>
    public event Action? BackToWorldsRequested;
    public event Action? SaveRequested;
    public event Action? QuitToTitleRequested;
    public event Action? QuitToDesktopRequested;
    public event Action? RestartRequested;
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

    public void ShowCharacterCreation(CharacterDataService data, bool startCustom = false)
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);

        _raceNames.Clear();
        _raceNames.AddRange(data.Races.Keys.OrderBy(name => name));
        StarterLoadoutService loadouts = StarterLoadoutService.Instance;

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(900, 600) };
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
        var raceDetail = DetailLabel();
        raceColumn.AddChild(raceDetail);
        raceColumn.CustomMinimumSize = new Vector2(330, 0);

        var loadoutColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        loadoutColumn.AddThemeConstantOverride("separation", 8);
        loadoutColumn.AddChild(LabelOf("STARTING LOADOUT", 13, Muted));
        var modes = ActionsRow();
        var kitsMode = CommandButton("KITS", Gold, 150);
        var customMode = CommandButton("CUSTOM", Muted, 150);
        modes.AddChild(kitsMode);
        modes.AddChild(customMode);
        loadoutColumn.AddChild(modes);
        var loadoutBody = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        loadoutBody.AddThemeConstantOverride("separation", 7);
        loadoutColumn.AddChild(loadoutBody);
        columns.AddChild(raceColumn);
        columns.AddChild(loadoutColumn);
        body.AddChild(columns);

        int humanIndex = Math.Max(0, _raceNames.IndexOf("Human"));
        raceList.Select(humanIndex);
        int selectedKitIndex = Math.Max(0, loadouts.Catalog.Kits.FindIndex(kit => kit.Id == "traveler"));
        bool customLoadout = false;
        var customSelections = loadouts.Catalog.CustomGroups.ToDictionary(
            group => group.Id,
            group => group.Options.FirstOrDefault()?.Id ?? "",
            StringComparer.OrdinalIgnoreCase);

        void UpdateRace(int index)
        {
            CharacterRace race = data.Races[_raceNames[index]];
            string effectiveness = string.Join("  ", race.Effectiveness
                .Where(pair => Math.Abs(pair.Value - 1f) >= 0.05f)
                .Select(pair => $"{Abbreviate(pair.Key)} {pair.Value:0.##}x"));
            raceDetail.Text = race.Description + (effectiveness.Length > 0 ? $"\n{effectiveness}" : "\nBalanced effectiveness");
        }

        void BuildKitMode()
        {
            ClearChildren(loadoutBody);
            var kitList = new ItemList
            {
                CustomMinimumSize = new Vector2(0, 225),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SelectMode = ItemList.SelectModeEnum.Single
            };
            foreach (StarterKitDefinition kit in loadouts.Catalog.Kits) kitList.AddItem(kit.Name);
            var detail = DetailLabel(82);
            void SelectKit(int index)
            {
                selectedKitIndex = Math.Clamp(index, 0, loadouts.Catalog.Kits.Count - 1);
                StarterKitDefinition kit = loadouts.Catalog.Kits[selectedKitIndex];
                detail.Text = kit.Description + "\n" + string.Join("  ", kit.ItemIds
                    .GroupBy(id => id)
                    .Select(group =>
                    {
                        string itemName = loadouts.CreateItem(group.Key)?.Name ?? group.Key;
                        return group.Count() > 1 ? $"{itemName} x{group.Count()}" : itemName;
                    }));
            }
            kitList.ItemSelected += index => SelectKit((int)index);
            kitList.Select(selectedKitIndex);
            SelectKit(selectedKitIndex);
            loadoutBody.AddChild(kitList);
            loadoutBody.AddChild(detail);
        }

        void BuildCustomMode()
        {
            ClearChildren(loadoutBody);
            var summary = DetailLabel(60);
            void UpdateSummary() => summary.Text = string.Join("  ", loadouts.Catalog.CustomGroups.Select(group =>
            {
                string selected = customSelections.GetValueOrDefault(group.Id, "");
                return group.Options.FirstOrDefault(option => option.Id == selected)?.Name ?? "None";
            }));

            foreach (StarterLoadoutGroup group in loadouts.Catalog.CustomGroups)
            {
                var options = new OptionButton { CustomMinimumSize = new Vector2(0, 36) };
                foreach (StarterLoadoutOption option in group.Options) options.AddItem(option.Name);
                int selected = Math.Max(0, group.Options.FindIndex(option =>
                    option.Id == customSelections.GetValueOrDefault(group.Id)));
                options.Select(selected);
                string groupId = group.Id;
                options.ItemSelected += index =>
                {
                    customSelections[groupId] = group.Options[(int)index].Id;
                    UpdateSummary();
                };
                loadoutBody.AddChild(Labeled(group.Name.ToUpperInvariant(), options));
            }
            UpdateSummary();
            loadoutBody.AddChild(summary);
        }

        kitsMode.Pressed += () =>
        {
            customLoadout = false;
            kitsMode.Disabled = true;
            customMode.Disabled = false;
            BuildKitMode();
        };
        customMode.Pressed += () =>
        {
            customLoadout = true;
            kitsMode.Disabled = false;
            customMode.Disabled = true;
            BuildCustomMode();
        };
        raceList.ItemSelected += index => UpdateRace((int)index);
        UpdateRace(humanIndex);
        customLoadout = startCustom;
        kitsMode.Disabled = !startCustom;
        customMode.Disabled = startCustom;
        if (startCustom) BuildCustomMode();
        else BuildKitMode();

        // Where they come into existence. Dungeon is the canonical opening (a newly-sentient being
        // waking in the maze); Town starts them as a resident, with the dungeon a choice they make.
        var origin = new OptionButton { CustomMinimumSize = new Vector2(0, 38) };
        origin.AddThemeFontSizeOverride("font_size", 15);
        origin.AddItem("The Dungeon — wake in the maze, no past", (int)StartLocation.Dungeon);
        origin.AddItem("The Town — begin as a resident", (int)StartLocation.Town);
        origin.Selected = 0;
        body.AddChild(Labeled("ORIGIN", origin));

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 150);
        back.Pressed += () => BackToWorldsRequested?.Invoke();
        var begin = CommandButton("BEGIN", Gold, 200);
        begin.Pressed += () =>
        {
            string heroName = string.IsNullOrWhiteSpace(name.Text) ? "Wayfarer" : name.Text.Trim();
            int raceIndex = SelectedIndex(raceList, humanIndex);
            string raceName = _raceNames[raceIndex];
            CharacterCreationSelection selection;
            if (customLoadout)
            {
                selection = new CharacterCreationSelection
                {
                    Name = heroName,
                    RaceName = raceName,
                    KitId = "custom",
                    IsCustom = true,
                    ItemIds = loadouts.Catalog.ResolveCustomItems(customSelections)
                };
            }
            else
                selection = loadouts.SelectionFromKit(
                    heroName, raceName, loadouts.Catalog.Kits[selectedKitIndex].Id);
            selection.StartLocation = (StartLocation)origin.GetItemId(Math.Max(0, origin.Selected));
            CharacterCreated?.Invoke(selection);
        };
        actions.AddChild(back);
        actions.AddChild(begin);
        body.AddChild(actions);

        AddFrontPanel(body, new Vector2(1000, 680));
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

    /// <summary>
    /// The worlds list — the step above characters. A world holds the generated region every
    /// character in it shares, and it survives their deaths, so picking one comes before picking a
    /// hero (owner rulings 2026-08-05).
    /// </summary>
    public void ShowWorlds(IReadOnlyList<WorldSummary> worlds)
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);
        _pendingDeleteId = null;
        _worldSummaries.Clear();
        _worldSummaries.AddRange(worlds);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(680, 460) };
        body.AddThemeConstantOverride("separation", 12);
        body.AddChild(LabelOf("WORLDS", 28, Gold, HorizontalAlignment.Center));
        body.AddChild(LabelOf("Characters live in a world. Worlds outlive them.", 13, Muted, HorizontalAlignment.Center));

        var list = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            CustomMinimumSize = new Vector2(0, 280)
        };
        list.AddThemeFontSizeOverride("font_size", 16);
        foreach (WorldSummary world in worlds)
            list.AddItem($"{world.Name}     {world.DescriptionLine}     {world.PopulationLine}");
        if (worlds.Count > 0) list.Select(0);
        body.AddChild(list);

        var status = LabelOf(worlds.Count == 0 ? "No worlds yet — create one to begin." : "",
            13, Muted, HorizontalAlignment.Center);
        body.AddChild(status);

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 130);
        back.Pressed += () => BackToTitleRequested?.Invoke();
        var create = CommandButton("NEW WORLD", Green, 175);
        create.Pressed += () => ShowWorldCreation();
        var delete = CommandButton("DELETE", Red, 150);
        delete.Disabled = worlds.Count == 0;
        var enter = CommandButton("ENTER", Blue, 150);
        enter.Disabled = worlds.Count == 0;

        enter.Pressed += () =>
        {
            int selected = SelectedIndex(list, -1);
            if (selected >= 0) WorldSelected?.Invoke(_worldSummaries[selected].WorldId);
        };
        // Two-click confirm, the same idiom as deleting a character — deleting a world takes every
        // character inside it, so it earns the extra beat.
        delete.Pressed += () =>
        {
            int selected = SelectedIndex(list, -1);
            if (selected < 0) return;
            string id = _worldSummaries[selected].WorldId;
            if (_pendingDeleteId == id)
            {
                WorldDeleteRequested?.Invoke(id);
                return;
            }
            _pendingDeleteId = id;
            delete.Text = "CONFIRM DELETE";
            WorldSummary world = _worldSummaries[selected];
            status.Text = $"Delete {world.Name} and its {world.LivingCharacters} character(s)? This cannot be undone.";
        };
        list.ItemSelected += _ =>
        {
            _pendingDeleteId = null;
            delete.Text = "DELETE";
            status.Text = "";
        };

        actions.AddChild(back);
        actions.AddChild(create);
        actions.AddChild(delete);
        actions.AddChild(enter);
        body.AddChild(actions);
        AddFrontPanel(body, new Vector2(760, 560));
    }

    /// <summary>
    /// World creation: the options a world is generated from (owner ruling 2026-08-05). The seed is
    /// shown and editable so worlds can be shared. Deliberately no resource-richness knob —
    /// abundance comes from the region's environment, not a slider.
    /// </summary>
    public void ShowWorldCreation()
    {
        CloseModal(false);
        _hud.Visible = false;
        _frontShade.Visible = true;
        _frontEnd.Visible = true;
        ClearChildren(_frontEnd);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(LabelOf("CREATE WORLD", 28, Gold, HorizontalAlignment.Center));

        var name = new LineEdit
        {
            Text = "Origins",
            PlaceholderText = "World name",
            MaxLength = 28,
            CustomMinimumSize = new Vector2(0, 42)
        };
        name.AddThemeFontSizeOverride("font_size", 16);
        body.AddChild(Labeled("NAME", name));

        var seed = new LineEdit
        {
            Text = new Random().Next().ToString(),
            PlaceholderText = "Seed",
            MaxLength = 12,
            CustomMinimumSize = new Vector2(0, 42)
        };
        seed.AddThemeFontSizeOverride("font_size", 16);
        body.AddChild(Labeled("SEED", seed));

        var sizeNames = Enum.GetNames<WorldSize>().ToList();
        var sizeList = SelectionList(sizeNames, out VBoxContainer sizeColumn, "WORLD SIZE");
        var sizeDetail = DetailLabel(76);
        sizeColumn.AddChild(sizeDetail);

        var hostilityNames = Enum.GetNames<Hostility>().ToList();
        var hostilityList = SelectionList(hostilityNames, out VBoxContainer hostilityColumn, "HOSTILITY");
        var hostilityDetail = DetailLabel(76);
        hostilityColumn.AddChild(hostilityDetail);

        // SelectionList sizes itself for a long roster (the race list); these hold three options
        // each, so stop them expanding or the detail text is flung to the bottom of the panel.
        foreach (ItemList list in new[] { sizeList, hostilityList })
        {
            list.SizeFlagsVertical = SizeFlags.Fill;
            list.CustomMinimumSize = new Vector2(340, 104);
        }

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 16);
        sizeColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hostilityColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(sizeColumn);
        columns.AddChild(hostilityColumn);
        body.AddChild(columns);
        body.AddChild(Spacer(8));

        void UpdateSize(int index)
        {
            var profile = WorldService.ResolveProfile(new WorldGenOptions { Size = Enum.Parse<WorldSize>(sizeNames[index]) });
            sizeDetail.Text =
                $"Region {profile.Size.RegionWidth}x{profile.Size.RegionHeight}, about {profile.Size.Population} residents.\n" +
                "Scales the region, not the number of towns.";
        }

        void UpdateHostility(int index)
        {
            var profile = WorldService.ResolveProfile(new WorldGenOptions { Hostility = Enum.Parse<Hostility>(hostilityNames[index]) });
            hostilityDetail.Text =
                $"Enemy density x{profile.EnemyDensity:0.0}, levels {profile.LevelOffset:+0;-0;+0}, " +
                $"elites {profile.EliteChance:P0}.";
        }

        sizeList.ItemSelected += index => UpdateSize((int)index);
        hostilityList.ItemSelected += index => UpdateHostility((int)index);
        int defaultSize = Math.Max(0, sizeNames.IndexOf(nameof(WorldSize.Medium)));
        int defaultHostility = Math.Max(0, hostilityNames.IndexOf(nameof(Hostility.Normal)));
        sizeList.Select(defaultSize);
        hostilityList.Select(defaultHostility);
        UpdateSize(defaultSize);
        UpdateHostility(defaultHostility);

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 130);
        back.Pressed += () => BackToWorldsRequested?.Invoke();
        var create = CommandButton("CREATE", Green, 175);
        create.Pressed += () =>
        {
            var options = new WorldGenOptions
            {
                // A non-numeric or empty seed field falls back to a random one rather than 0, so a
                // stray keystroke can't quietly funnel every world onto the same seed.
                Seed = int.TryParse(seed.Text.Trim(), out int parsed) ? parsed : new Random().Next(),
                Size = Enum.Parse<WorldSize>(sizeNames[Math.Max(0, SelectedIndex(sizeList, defaultSize))]),
                Hostility = Enum.Parse<Hostility>(hostilityNames[Math.Max(0, SelectedIndex(hostilityList, defaultHostility))])
            };
            WorldCreateRequested?.Invoke(options, name.Text.Trim());
        };
        actions.AddChild(back);
        actions.AddChild(create);
        body.AddChild(actions);

        AddFrontPanel(body, new Vector2(820, 520));
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
            // In town the mode line doubles as the world clock (dungeon keeps time to itself).
            _modeLabel.Text = state.IsInOverworld
                ? $"TOWN   {state.Clock.TimeDisplay.ToUpperInvariant()}"
                : hero.InCombat ? "ENGAGED" : "REAL-TIME";
            _turnLabel.Visible = false;
            _intentBand.Visible = false;
        }

        _messageLabel.Text = string.Join("\n", state.Messages.Messages.TakeLast(4).Select(message => message.Text));
        _activityLabel.Visible = state.CurrentActivity != null;
        _activityLabel.Text = state.CurrentActivity == null
            ? ""
            : $"{state.CurrentActivity.Name.ToUpperInvariant()}   {state.CurrentActivity.TicksRemaining}";
        MazeFeature? nearby = state.NearbyInteractable;
        _interactionToast.Visible = nearby != null;
        if (nearby != null)
        {
            string action = nearby.Type switch
            {
                MazeFeatureType.Chest => nearby.IsOpened ? "LOOT CHEST" : "CHEST",
                MazeFeatureType.GuardianDoor => $"CHALLENGE FLOOR {state.CurrentFloor + 1} GUARDIAN",
                MazeFeatureType.Shrine => "RETURN TO TOWN",
                MazeFeatureType.MineEntrance => "MINE",
                MazeFeatureType.Smithy => "SMITHY",
                MazeFeatureType.Stall => "MARKET STALL",
                MazeFeatureType.DungeonEntrance => "ENTER THE DUNGEON",
                _ => nearby.Type.ToString().ToUpperInvariant()
            };
            _interactionLabel.Text = $"PRESS E   {action}";
        }
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

    public void ShowSafeRoomChoice(GameState state, bool challengeGuardian)
    {
        string title = challengeGuardian ? "GUARDIAN DOOR" : "SAFE-ROOM SHRINE";
        var body = ModalBody(title, challengeGuardian ? Red : Blue, new Vector2(390, 0));
        body.AddChild(LabelOf(challengeGuardian
                ? $"Beyond this door is Floor {state.CurrentFloor + 1}.\nThe Guardian fight will begin there."
                : "This shrine ends the current dive\nand returns you safely to town.",
            15, Text, HorizontalAlignment.Center));

        var proceed = CommandButton(challengeGuardian
            ? $"ENTER FLOOR {state.CurrentFloor + 1}"
            : "RETURN TO TOWN", challengeGuardian ? Red : Blue, 290);
        proceed.Pressed += () =>
        {
            CloseModal();
            if (challengeGuardian) state.EnterGuardianChamber();
            else state.UseSafeRoomShrine();
        };
        body.AddChild(proceed);

        var cancel = CommandButton("STAY IN SAFE ROOM", Muted, 290);
        cancel.Pressed += () => CloseModal();
        body.AddChild(cancel);
        ShowModal(body, new Vector2(450, 300));
    }

    public void ShowCharacterSheet(GameState state)
    {
        ShowCharacterPanel(state, CharacterTab.Stats);
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
        ShowCharacterPanel(state, CharacterTab.Inventory);
    }

    public void ShowProgression(GameState state)
    {
        ShowCharacterPanel(state, CharacterTab.Progression);
    }

    private void ShowCharacterPanel(GameState state, CharacterTab selectedTab)
    {
        Hero hero = state.Hero;
        var body = ModalBody("CHARACTER", Gold, new Vector2(850, 520));
        body.AddThemeConstantOverride("separation", 6);
        body.AddChild(LabelOf(
            $"{hero.Name.ToUpperInvariant()}     LEVEL {hero.Progression.CharacterLevel}     {hero.Race.ToUpperInvariant()} {hero.Class.ToUpperInvariant()}",
            14, Text, HorizontalAlignment.Center));

        var tabs = ActionsRow();
        var inventoryTab = CommandButton("INVENTORY", selectedTab == CharacterTab.Inventory ? Gold : Muted, 190);
        inventoryTab.Pressed += () => ShowCharacterPanel(state, CharacterTab.Inventory);
        var stats = CommandButton("STATS", selectedTab == CharacterTab.Stats ? Gold : Muted, 190);
        stats.Pressed += () => ShowCharacterPanel(state, CharacterTab.Stats);
        var progression = CommandButton("PROGRESSION", selectedTab == CharacterTab.Progression ? Gold : Muted, 190);
        progression.Pressed += () => ShowCharacterPanel(state, CharacterTab.Progression);
        var hotbarTab = CommandButton("HOTBAR", selectedTab == CharacterTab.Hotbar ? Gold : Muted, 160);
        hotbarTab.Pressed += () => ShowCharacterPanel(state, CharacterTab.Hotbar);
        tabs.AddChild(inventoryTab);
        tabs.AddChild(stats);
        tabs.AddChild(progression);
        tabs.AddChild(hotbarTab);
        body.AddChild(tabs);

        if (selectedTab == CharacterTab.Hotbar)
        {
            BuildHotbarTab(state, body);
        }
        else if (selectedTab == CharacterTab.Stats)
        {
            BuildStatsTab(state, body);
            var close = CommandButton("CLOSE", Green, 160);
            close.Pressed += () => CloseModal();
            body.AddChild(Centered(close));
        }
        else if (selectedTab == CharacterTab.Progression)
        {
            BuildProgressionTab(state, body);
            var close = CommandButton("CLOSE", Green, 160);
            close.Pressed += () => CloseModal();
            body.AddChild(Centered(close));
        }
        else BuildInventoryTab(state, body);
        ShowModal(body, new Vector2(920, 650));
    }

    /// <summary>Hotbar management (owner request 2026-08-05): every usable action and backpack
    /// spell gets numbered ASSIGN buttons — "Assign to Slot [1-6]" — plus per-slot CLEAR.
    /// (Drag-and-drop can layer on later; direct slot buttons are the always-works path.)</summary>
    private void BuildHotbarTab(GameState state, VBoxContainer body)
    {
        Hero hero = state.Hero;
        body.AddChild(LabelOf("CLICK A NUMBER TO ASSIGN THAT ACTION TO A SLOT — CLEAR EMPTIES A SLOT",
            12, Muted, HorizontalAlignment.Center));

        // The six slots as they currently stand.
        var slotsRow = ActionsRow();
        for (int i = 0; i < hero.HotbarAssignments.Count; i++)
        {
            int slot = i;
            Attack? attack = state.HotbarAttackAt(i);
            bool selected = attack != null && ReferenceEquals(hero.CurrentAttack, attack);
            var cell = new VBoxContainer();
            cell.AddThemeConstantOverride("separation", 3);
            var slotButton = SmallButton($"{i + 1}\n{(attack == null ? "—" : AttackMonogram(attack.Name))}",
                selected ? Gold : attack != null ? Text : Muted);
            slotButton.CustomMinimumSize = new Vector2(66, 56);
            slotButton.TooltipText = attack == null ? "Empty slot" : $"{attack.Name} — click to select";
            slotButton.Pressed += () =>
            {
                state.SelectAttack(slot);
                ShowCharacterPanel(state, CharacterTab.Hotbar);
                RefreshGame(state);
            };
            cell.AddChild(slotButton);
            var clear = SmallButton("CLEAR", attack != null ? Red : Muted);
            clear.CustomMinimumSize = new Vector2(66, 26);
            clear.Disabled = attack == null;
            clear.Pressed += () =>
            {
                state.ClearHotbarSlot(slot);
                ShowCharacterPanel(state, CharacterTab.Hotbar);
                RefreshGame(state);
            };
            cell.AddChild(clear);
            slotsRow.AddChild(cell);
        }
        body.AddChild(slotsRow);

        // Everything assignable: usable actions first, then backpack spells (assigning one slots
        // it onto the action bar in the same motion).
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 250)
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);

        void AddAssignRow(string name, string tooltip, Action<int> assign)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 5);
            var label = LabelOf(name, 13, Text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.TooltipText = tooltip;
            row.AddChild(label);
            for (int n = 0; n < hero.HotbarAssignments.Count; n++)
            {
                int target = n;
                var assignButton = SmallButton((n + 1).ToString(), Blue);
                assignButton.CustomMinimumSize = new Vector2(34, 28);
                assignButton.TooltipText = $"Assign to slot {n + 1}";
                assignButton.Pressed += () =>
                {
                    assign(target);
                    ShowCharacterPanel(state, CharacterTab.Hotbar);
                    RefreshGame(state);
                };
                row.AddChild(assignButton);
            }
            list.AddChild(row);
        }

        foreach (Attack attack in hero.Attacks)
        {
            int atSlot = hero.HotbarAssignments.FindIndex(id =>
                string.Equals(id, attack.Id, StringComparison.OrdinalIgnoreCase));
            string suffix = atSlot >= 0 ? $"   [SLOT {atSlot + 1}]" : "   [UNASSIGNED]";
            string attackId = attack.Id;
            AddAssignRow(attack.Name.ToUpperInvariant() + suffix, attack.Description,
                target => state.AssignAttackToHotbar(attackId, target, out _));
        }
        foreach (Combinable spell in hero.Inventory.Where(item => item is Spell).ToList())
        {
            Combinable captured = spell;
            AddAssignRow(spell.Name.ToUpperInvariant() + "   [BACKPACK]", spell.Description,
                target => state.AssignSpellToHotbar(captured, target, out _));
        }
        foreach (var consumableGroup in hero.Inventory.OfType<Item>()
                     .Where(item => item.Consumable)
                     .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToList())
        {
            Item captured = consumableGroup.First();
            AddAssignRow($"{captured.Name.ToUpperInvariant()} x{consumableGroup.Count()}   [CONSUMABLE]",
                captured.Description,
                target => state.AssignConsumableToHotbar(captured, target, out _));
        }

        scroll.AddChild(list);
        body.AddChild(scroll);
        var close = CommandButton("CLOSE", Green, 160);
        close.Pressed += () => CloseModal();
        body.AddChild(Centered(close));
    }

    private void BuildStatsTab(GameState state, VBoxContainer body)
    {
        Hero hero = state.Hero;
        body.AddChild(LabelOf($"SHARED XP {hero.Progression.UnallocatedXp}     POINTS {hero.UnspentStatPoints}",
            13, hero.UnspentStatPoints > 0 ? Gold : Muted, HorizontalAlignment.Center));
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
                if (state.SpendStatPoint(selectedStat)) ShowCharacterPanel(state, CharacterTab.Stats);
            };
            statGrid.AddChild(add);
        }
        body.AddChild(statGrid);
        body.AddChild(LabelOf(
            $"HP {hero.CurrentHp}/{hero.MaxHp}     STAMINA {hero.CurrentStamina}/{hero.MaxStamina}     MANA {hero.CurrentMana}/{hero.MaxMana}     FAITH {hero.CurrentFaith}/{hero.MaxFaith}",
            13, Muted, HorizontalAlignment.Center));
        body.AddChild(LabelOf(
            $"DEFENSE {hero.Defense} + {hero.EquipmentDefenseBonus} GEAR     WEAPON DAMAGE +{hero.EquippedWeaponDamage}     TACTICAL MOVE {state.CalculateTacticalMovementAllowance()}     GOLD {hero.Gold}",
            13, Green, HorizontalAlignment.Center));
    }

    private void BuildProgressionTab(GameState state, VBoxContainer body)
    {
        Hero hero = state.Hero;
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 390)
        };
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        content.AddChild(LabelOf($"SHARED XP     {hero.Progression.UnallocatedXp}", 16,
            hero.Progression.UnallocatedXp > 0 ? Gold : Muted, HorizontalAlignment.Center));
        content.AddChild(LabelOf("CLASSES", 13, Blue));

        foreach (ProgressionSlot slot in hero.Progression.ClassSlots)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 42) };
            row.AddThemeConstantOverride("separation", 12);
            string description;
            int needed = 0;
            if (slot.State == ProgressionSlotState.Locked) description = "LOCKED";
            else if (slot.Instance == null) description = "EMPTY";
            else
            {
                ProgressionInstance instance = slot.Instance;
                needed = ProgressionService.Instance.XpNeededForNext(instance);
                string specialization = instance.Specialization == null
                    ? "" : $"     [{instance.Specialization.Name.ToUpperInvariant()}]";
                description = instance.Level >= instance.MaxLevel
                    ? $"{instance.Name.ToUpperInvariant()}{specialization}     LEVEL {instance.Level}     MASTERED"
                    : $"{instance.Name.ToUpperInvariant()}     LEVEL {instance.Level}     " +
                      $"{instance.CurrentXp}/{instance.CurrentXp + needed} XP{specialization}";
            }
            var label = LabelOf(description, 14, slot.Instance == null ? Muted : Text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);

            if (slot.Instance != null && slot.State == ProgressionSlotState.Active)
            {
                int allocation = needed;
                var allocate = CommandButton("ALLOCATE", Gold, 115);
                allocate.Disabled = hero.Progression.UnallocatedXp <= 0;
                allocate.TooltipText = $"Allocate up to {needed} shared XP to {slot.Instance.Name}";
                string selectedSlot = slot.SlotId;
                allocate.Pressed += () =>
                {
                    state.AllocateClassXp(selectedSlot, allocation);
                    ShowCharacterPanel(state, CharacterTab.Progression);
                };
                row.AddChild(allocate);
            }
            if (slot.Instance is { Level: >= 10, Specialization: null } &&
                state.GenerateSpecializationOffers(slot.SlotId).Count > 0)
            {
                var specialize = CommandButton("SPECIALIZE", Blue, 125);
                string selectedSlot = slot.SlotId;
                specialize.Pressed += () => ShowSpecializationOffers(state, selectedSlot);
                row.AddChild(specialize);
            }
            if (slot.State == ProgressionSlotState.Mastered &&
                state.GenerateAdvancementOffers(slot.SlotId).Count > 0)
            {
                var advance = CommandButton("ADVANCE", Green, 115);
                string selectedSlot = slot.SlotId;
                advance.Pressed += () => ShowAdvancementOffers(state, selectedSlot);
                row.AddChild(advance);
            }
            if (slot.State == ProgressionSlotState.Empty)
            {
                var offers = CommandButton("OFFERS", Blue, 130);
                string selectedSlot = slot.SlotId;
                offers.Pressed += () => ShowProgressionOffers(
                    state, ProgressionDomain.Class, selectedSlot);
                row.AddChild(offers);
            }
            content.AddChild(row);
        }

        content.AddChild(new HSeparator());
        content.AddChild(LabelOf("PROFESSIONS", 13, Green));
        foreach (ProgressionSlot slot in hero.Progression.ProfessionSlots)
        {
            string description;
            if (slot.State == ProgressionSlotState.Locked) description = "LOCKED";
            else if (slot.Instance == null) description = "EMPTY";
            else if (slot.State == ProgressionSlotState.Mastered)
                description = $"{slot.Instance.Name.ToUpperInvariant()}" +
                    (slot.Instance.Specialization == null ? "" :
                        $"     [{slot.Instance.Specialization.Name.ToUpperInvariant()}]") +
                    $"     LEVEL {slot.Instance.Level}     MASTERED";
            else
            {
                int needed = ProgressionService.Instance.XpNeededForNext(slot.Instance);
                description = $"{slot.Instance.Name.ToUpperInvariant()}     LEVEL {slot.Instance.Level}     " +
                    $"{slot.Instance.CurrentXp}/{slot.Instance.CurrentXp + needed} XP" +
                    (slot.Instance.Specialization == null ? "" :
                        $"     [{slot.Instance.Specialization.Name.ToUpperInvariant()}]");
            }
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 38) };
            var label = LabelOf(description, 14, slot.Instance == null ? Muted : Text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            if (slot.State == ProgressionSlotState.Empty)
            {
                var offers = CommandButton("OFFERS", Blue, 130);
                string selectedSlot = slot.SlotId;
                offers.Pressed += () => ShowProgressionOffers(
                    state, ProgressionDomain.Profession, selectedSlot);
                row.AddChild(offers);
            }
            if (slot.Instance is { Level: >= 10, Specialization: null } &&
                state.GenerateSpecializationOffers(slot.SlotId).Count > 0)
            {
                var specialize = CommandButton("SPECIALIZE", Blue, 125);
                string selectedSlot = slot.SlotId;
                specialize.Pressed += () => ShowSpecializationOffers(state, selectedSlot);
                row.AddChild(specialize);
            }
            if (slot.State == ProgressionSlotState.Mastered &&
                state.GenerateAdvancementOffers(slot.SlotId).Count > 0)
            {
                var advance = CommandButton("ADVANCE", Green, 115);
                string selectedSlot = slot.SlotId;
                advance.Pressed += () => ShowAdvancementOffers(state, selectedSlot);
                row.AddChild(advance);
            }
            content.AddChild(row);
        }

        content.AddChild(new HSeparator());
        content.AddChild(LabelOf("SKILLS", 13, Blue));
        if (hero.Progression.Skills.Count == 0)
            content.AddChild(LabelOf("NONE", 14, Muted));
        else
            foreach (SkillProgress skill in hero.Progression.Skills.Values.OrderBy(skill => skill.Name))
                content.AddChild(LabelOf(
                    $"{skill.Name.ToUpperInvariant()}     LEVEL {skill.Level}     {skill.CurrentXp} XP", 14, Text));

        scroll.AddChild(content);
        body.AddChild(scroll);
    }

    public void ShowProgressionOffers(GameState state, ProgressionDomain domain,
        string slotId, string feedback = "")
    {
        IReadOnlyList<ProgressionOffer> offers = state.GenerateProgressionOffers(domain, slotId);
        var body = ModalBody($"{domain.ToString().ToUpperInvariant()} OFFERS", Blue, new Vector2(700, 470));
        var list = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 210),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        foreach (ProgressionOffer offer in offers)
            list.AddItem($"{offer.Name.ToUpperInvariant()}     LEVEL 1");
        body.AddChild(list);
        var details = DetailLabel(120);
        details.Text = offers.Count == 0 ? "No paths are currently revealed for this slot." : "Select an offer.";
        body.AddChild(details);
        if (!string.IsNullOrWhiteSpace(feedback))
            body.AddChild(LabelOf(feedback, 12, Red, HorizontalAlignment.Center));

        int selected = -1;
        list.ItemSelected += index =>
        {
            selected = (int)index;
            ProgressionOffer offer = offers[selected];
            string reasons = offer.Explanations.Count == 0
                ? "" : "\n\n" + string.Join("\n", offer.Explanations);
            details.Text = offer.Description + reasons;
        };

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 150);
        back.Pressed += () => ShowProgression(state);
        var accept = CommandButton("ACCEPT", Gold, 170);
        accept.Disabled = offers.Count == 0;
        accept.Pressed += () =>
        {
            if (selected < 0 && offers.Count > 0) selected = 0;
            if (selected < 0) return;
            if (state.AcceptProgressionOffer(slotId, offers[selected], out string error))
            {
                SaveService.Save(state);
                ShowProgression(state);
            }
            else
                ShowProgressionOffers(state, domain, slotId, error);
        };
        actions.AddChild(back);
        actions.AddChild(accept);
        body.AddChild(actions);
        if (offers.Count > 0)
        {
            list.Select(0);
            selected = 0;
            ProgressionOffer first = offers[0];
            details.Text = first.Description + (first.Explanations.Count == 0
                ? "" : "\n\n" + string.Join("\n", first.Explanations));
        }
        ShowModal(body, new Vector2(780, 590), backAction: () => ShowProgression(state));
    }

    public void ShowSpecializationOffers(GameState state, string slotId, string feedback = "")
    {
        IReadOnlyList<ProgressionSpecializationOffer> offers = state.GenerateSpecializationOffers(slotId);
        var body = ModalBody("SPECIALIZATIONS", Blue, new Vector2(700, 470));
        var list = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 210),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        foreach (ProgressionSpecializationOffer offer in offers) list.AddItem(offer.Name.ToUpperInvariant());
        body.AddChild(list);
        var details = DetailLabel(120);
        details.Text = offers.Count == 0 ? "No specializations are currently revealed." : "Select a specialization.";
        body.AddChild(details);
        if (!string.IsNullOrWhiteSpace(feedback))
            body.AddChild(LabelOf(feedback, 12, Red, HorizontalAlignment.Center));

        int selected = -1;
        void Select(int index)
        {
            selected = index;
            ProgressionSpecializationOffer offer = offers[index];
            details.Text = offer.Description + (offer.Explanations.Count == 0
                ? "" : "\n\n" + string.Join("\n", offer.Explanations));
        }
        list.ItemSelected += index => Select((int)index);

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 150);
        back.Pressed += () => ShowProgression(state);
        var accept = CommandButton("SPECIALIZE", Gold, 170);
        accept.Disabled = offers.Count == 0;
        accept.Pressed += () =>
        {
            if (selected < 0 && offers.Count > 0) Select(0);
            if (selected < 0) return;
            if (state.AcceptSpecializationOffer(slotId, offers[selected], out string error))
            {
                SaveService.Save(state);
                ShowProgression(state);
            }
            else ShowSpecializationOffers(state, slotId, error);
        };
        actions.AddChild(back);
        actions.AddChild(accept);
        body.AddChild(actions);
        if (offers.Count > 0)
        {
            list.Select(0);
            Select(0);
        }
        ShowModal(body, new Vector2(780, 590), backAction: () => ShowProgression(state));
    }

    public void ShowAdvancementOffers(GameState state, string slotId, string feedback = "")
    {
        IReadOnlyList<ProgressionAdvancementOffer> offers = state.GenerateAdvancementOffers(slotId);
        var body = ModalBody("ADVANCEMENTS", Green, new Vector2(700, 470));
        var list = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 210),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        foreach (ProgressionAdvancementOffer offer in offers)
        {
            string kind = offer.Kind == ProgressionAdvancementKind.SinglePath
                ? "SINGLE PATH" : "CONVERGENCE";
            list.AddItem($"{offer.Name.ToUpperInvariant()}     LEVEL 1     {kind}");
        }
        body.AddChild(list);
        var details = DetailLabel(120);
        details.Text = offers.Count == 0 ? "No advancements are currently revealed." : "Select an advancement.";
        body.AddChild(details);
        if (!string.IsNullOrWhiteSpace(feedback))
            body.AddChild(LabelOf(feedback, 12, Red, HorizontalAlignment.Center));

        int selected = -1;
        void Select(int index)
        {
            selected = index;
            ProgressionAdvancementOffer offer = offers[index];
            details.Text = offer.Description + (offer.Explanations.Count == 0
                ? "" : "\n\n" + string.Join("\n", offer.Explanations));
        }
        list.ItemSelected += index => Select((int)index);

        var actions = ActionsRow();
        var back = CommandButton("BACK", Muted, 150);
        back.Pressed += () => ShowProgression(state);
        var accept = CommandButton("ADVANCE", Gold, 170);
        accept.Disabled = offers.Count == 0;
        accept.Pressed += () =>
        {
            if (selected < 0 && offers.Count > 0) Select(0);
            if (selected < 0) return;
            if (state.AcceptAdvancementOffer(slotId, offers[selected], out string error))
            {
                SaveService.Save(state);
                ShowProgression(state);
            }
            else ShowAdvancementOffers(state, slotId, error);
        };
        actions.AddChild(back);
        actions.AddChild(accept);
        body.AddChild(actions);
        if (offers.Count > 0)
        {
            list.Select(0);
            Select(0);
        }
        ShowModal(body, new Vector2(780, 590), backAction: () => ShowProgression(state));
    }

    private void BuildInventoryTab(GameState state, VBoxContainer body)
    {
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        VBoxContainer equippedColumn = ListColumn("EQUIPPED", out ItemList equippedList);
        VBoxContainer backpackColumn = ListColumn("BACKPACK", out ItemList backpackList);
        equippedList.CustomMinimumSize = new Vector2(360, 205);
        backpackList.CustomMinimumSize = new Vector2(360, 205);
        var details = DetailLabel(62);
        var status = LabelOf("", 12, Muted, HorizontalAlignment.Center);
        var equippedEntries = new List<Combinable?>();
        var backpackEntries = new List<Combinable>();

        void Populate()
        {
            equippedList.Clear();
            backpackList.Clear();
            equippedEntries.Clear();
            backpackEntries.Clear();
            foreach (EquipmentSlot slot in EquipmentSlots.DisplayOrder)
            {
                Combinable? item = state.Hero.Equipment.GetValueOrDefault(slot);
                string value = item?.Name ?? "Empty";
                if (slot == EquipmentSlot.OffHand && state.IsOffHandBlocked)
                    value = "Reserved by two-handed weapon";
                equippedList.AddItem($"{EquipmentSlots.Label(slot).ToUpperInvariant(),-11}  {value}");
                equippedEntries.Add(item);
            }
            foreach (Combinable spell in state.Hero.Loadout)
            {
                equippedList.AddItem($"ACTION SPELL  {spell.Name}");
                equippedEntries.Add(spell);
            }
            backpackEntries.AddRange(state.Hero.Inventory
                .OrderBy(item => item.Kind)
                .ThenByDescending(item => item.Rarity)
                .ThenBy(item => item.Name));
            foreach (Combinable item in backpackEntries)
                backpackList.AddItem(ItemLabel(item));
            details.Text = "Select an item to inspect it.";
            status.Text = $"GEAR DEFENSE +{state.Hero.EquipmentDefenseBonus}     WEAPON DAMAGE +{state.Hero.EquippedWeaponDamage}     SPELLS {state.Hero.Loadout.Count}     GOLD {state.Hero.Gold}";
            _hotbarSignature = "";
            RefreshGame(state);
        }

        equippedList.ItemSelected += index =>
        {
            int selected = (int)index;
            Combinable? item = selected < equippedEntries.Count ? equippedEntries[selected] : null;
            details.Text = item == null ? "This slot is empty." : ItemDetails(item, state.Hero);
        };
        backpackList.ItemSelected += index =>
            details.Text = ItemDetails(backpackEntries[(int)index], state.Hero);

        var unequip = CommandButton("UNEQUIP", Muted, 150);
        unequip.Pressed += () =>
        {
            int index = SelectedIndex(equippedList, -1);
            if (index >= 0 && index < equippedEntries.Count && equippedEntries[index] is { } item &&
                state.UnequipToInventory(item)) Populate();
        };
        var equip = CommandButton("EQUIP", Blue, 150);
        equip.Pressed += () =>
        {
            int index = SelectedIndex(backpackList, -1);
            if (index < 0) return;
            Combinable item = backpackEntries[index];
            if (!state.EquipFromInventory(item, out string reason)) details.Text = reason;
            else Populate();
        };
        equippedColumn.AddChild(unequip);
        backpackColumn.AddChild(equip);
        columns.AddChild(equippedColumn);
        columns.AddChild(backpackColumn);
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
        var bodyEntries = new List<Combinable?>();

        void Populate()
        {
            corpseItems.Clear();
            heroItems.Clear();
            bodyEntries.Clear();
            if (corpse.Gold > 0)
            {
                corpseItems.AddItem($"Gold ({corpse.Gold})   Currency");
                bodyEntries.Add(null);
            }
            foreach (Combinable item in corpse.Inventory)
            {
                corpseItems.AddItem(ItemLabel(item));
                bodyEntries.Add(item);
            }
            foreach (Combinable item in state.Hero.Inventory) heroItems.AddItem(ItemLabel(item));
            status.Text = $"YOUR GOLD {state.Hero.Gold}";
            details.Text = "Select an item to inspect it.";
        }

        corpseItems.ItemSelected += index =>
        {
            Combinable? item = bodyEntries[(int)index];
            details.Text = item == null ? $"Gold\n{corpse.Gold} coins." : ItemDetails(item);
        };
        heroItems.ItemSelected += index => details.Text = ItemDetails(state.Hero.Inventory[(int)index]);
        var take = CommandButton("TAKE", Blue, 130);
        take.Pressed += () =>
        {
            int index = SelectedIndex(corpseItems, -1);
            if (index < 0 || index >= bodyEntries.Count) return;
            Combinable? item = bodyEntries[index];
            bool taken = item == null ? state.LootGold(corpse) : state.LootItem(corpse, item);
            if (taken) Populate();
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

    public void ShowChestActions(GameState state, MazeFeature chest, string feedback = "")
    {
        if (chest.IsOpened)
        {
            ShowChestLoot(state, chest);
            return;
        }

        var body = ModalBody("CHEST", Gold, new Vector2(340, 0));
        var open = CommandButton(chest.IsLocked ? "OPEN (LOCKED)" : "OPEN", chest.IsLocked ? Muted : Blue, 270);
        open.Disabled = chest.IsLocked;
        open.Pressed += () =>
        {
            if (state.OpenChest(chest)) ShowChestLoot(state, chest);
        };
        body.AddChild(open);

        var inspect = CommandButton(chest.TrapChecked ? "TRAP CHECKED" : "LOOK FOR TRAPS", Muted, 270);
        inspect.Disabled = chest.TrapChecked;
        inspect.Pressed += () =>
        {
            state.LookForChestTraps(chest);
            ShowChestActions(state, chest, state.Messages.Messages.LastOrDefault()?.Text ?? "");
            RefreshGame(state);
        };
        body.AddChild(inspect);

        if (feedback.Length > 0)
            body.AddChild(LabelOf(feedback, 12,
                chest.ChestTrapDetected && !chest.TrapDisarmed ? Red : Muted,
                HorizontalAlignment.Center));

        if (chest.ChestTrapDetected && !chest.TrapDisarmed)
        {
            var disarm = CommandButton("DISARM TRAP", Red, 270);
            disarm.Pressed += () =>
            {
                state.TryDisarmChestTrap(chest);
                ShowChestActions(state, chest, state.Messages.Messages.LastOrDefault()?.Text ?? "");
                RefreshGame(state);
            };
            body.AddChild(disarm);
        }

        if (chest.IsLocked)
        {
            var lockpick = CommandButton("LOCKPICK", Blue, 270);
            lockpick.Pressed += () =>
            {
                state.TryLockpickChest(chest);
                ShowChestActions(state, chest, state.Messages.Messages.LastOrDefault()?.Text ?? "");
                RefreshGame(state);
            };
            body.AddChild(lockpick);
            if (state.HasChestKey(chest))
            {
                var key = CommandButton("USE KEY", Gold, 270);
                key.Pressed += () =>
                {
                    state.UseChestKey(chest);
                    ShowChestActions(state, chest, state.Messages.Messages.LastOrDefault()?.Text ?? "");
                    RefreshGame(state);
                };
                body.AddChild(key);
            }
        }

        var close = CommandButton("CLOSE", Green, 270);
        close.Pressed += () => CloseModal();
        body.AddChild(close);
        ShowModal(body, new Vector2(400, 0));
    }

    public void ShowChestLoot(GameState state, MazeFeature chest)
    {
        var body = ModalBody("OPEN CHEST", Gold, new Vector2(810, 500));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        VBoxContainer chestColumn = ListColumn("CHEST", out ItemList chestItems);
        VBoxContainer heroColumn = ListColumn("YOUR BACKPACK", out ItemList heroItems);
        var details = DetailLabel(80);
        var status = LabelOf("", 12, Muted, HorizontalAlignment.Center);
        var entries = new List<Combinable?>();

        void Populate()
        {
            chestItems.Clear();
            heroItems.Clear();
            entries.Clear();
            if (chest.Gold > 0)
            {
                chestItems.AddItem($"Gold ({chest.Gold})   Currency");
                entries.Add(null);
            }
            foreach (Combinable item in chest.Inventory)
            {
                chestItems.AddItem(ItemLabel(item));
                entries.Add(item);
            }
            foreach (Combinable item in state.Hero.Inventory) heroItems.AddItem(ItemLabel(item));
            details.Text = entries.Count == 0 ? "The chest is empty." : "Select an item to inspect it.";
            status.Text = $"YOUR GOLD {state.Hero.Gold}";
        }

        chestItems.ItemSelected += index =>
        {
            Combinable? item = entries[(int)index];
            details.Text = item == null ? $"Gold\n{chest.Gold} coins." : ItemDetails(item);
        };
        heroItems.ItemSelected += index => details.Text = ItemDetails(state.Hero.Inventory[(int)index]);
        var take = CommandButton("TAKE", Blue, 130);
        take.Pressed += () =>
        {
            int index = SelectedIndex(chestItems, -1);
            if (index < 0 || index >= entries.Count) return;
            Combinable? item = entries[index];
            bool taken = item == null ? state.LootChestGold(chest) : state.LootChestItem(chest, item);
            if (taken) Populate();
            RefreshGame(state);
        };
        chestColumn.AddChild(take);
        columns.AddChild(chestColumn);
        columns.AddChild(heroColumn);
        body.AddChild(columns);
        body.AddChild(status);
        body.AddChild(details);
        var actions = ActionsRow();
        var all = CommandButton("LOOT ALL", Gold, 170);
        all.Pressed += () => { state.LootAll(chest); Populate(); RefreshGame(state); };
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
        body.AddChild(LabelOf(
            $"{state.Hero.Name.ToUpperInvariant()}\n{state.Hero.Race.ToUpperInvariant()} {state.Hero.Class.ToUpperInvariant()}\n" +
            $"FLOOR {state.CurrentFloor}\nThe fallen character save has been erased.",
            16, Text, HorizontalAlignment.Center));
        var restart = CommandButton("RESTART SAME CHARACTER", Gold, 290);
        restart.Pressed += () => RestartRequested?.Invoke();
        body.AddChild(restart);
        var newHero = CommandButton("NEW CHARACTER", Blue, 290);
        newHero.Pressed += () => NewGameRequested?.Invoke();
        body.AddChild(newHero);
        var title = CommandButton("TITLE", Muted, 290);
        title.Pressed += () => BackToTitleRequested?.Invoke();
        body.AddChild(title);
        ShowModal(body, new Vector2(470, 430), false);
    }

    // The old "DIVE COMPLETE" modal is gone — leaving the dungeon now lands the hero in the
    // playable town (the overworld loop is ported); re-diving is the Dungeon Entrance's menu.

    /// <summary>Raised when the debug console opens (true) or closes (false) — the host pauses
    /// and resumes the sim around it, same as the Avalonia client.</summary>
    public event Action<bool>? ConsoleToggled;

    public bool IsConsoleOpen => _consolePanel.Visible;

    public void ToggleConsole(GameState state)
    {
        if (_consolePanel.Visible)
        {
            CloseConsole();
            return;
        }
        _consoleState = state;
        _consolePanel.Visible = true;
        _consoleOutput.Text = "Debug console — type 'help' for commands.";
        _consoleInput.Clear();
        _consoleInput.GrabFocus();
        ConsoleToggled?.Invoke(true);
    }

    public void CloseConsole()
    {
        if (!_consolePanel.Visible) return;
        _consolePanel.Visible = false;
        _consoleInput.ReleaseFocus();
        ConsoleToggled?.Invoke(false);
    }

    /// <summary>Mine entrance menu: start a mining activity (the modal closes so the sim runs).</summary>
    public void ShowMineActions(GameState state)
    {
        var body = ModalBody("MINE", Gold, new Vector2(360, 0));
        int ore = state.Hero.Resources.GetValueOrDefault("iron-ore", 0);
        body.AddChild(LabelOf($"IRON ORE CARRIED: {ore}", 14, Text, HorizontalAlignment.Center));
        var mine = CommandButton("MINE IRON ORE", Blue, 280);
        mine.Pressed += () => { CloseModal(); state.MineOre(); };
        body.AddChild(mine);
        var leave = CommandButton("LEAVE", Muted, 280);
        leave.Pressed += () => CloseModal();
        body.AddChild(leave);
        ShowModal(body, new Vector2(430, 300));
    }

    /// <summary>Smithy menu: one entry per crafting recipe, disabled with its needs when inputs
    /// are short. (Forge-gated synthesis joins once the combination UI grows a location filter.)</summary>
    public void ShowSmithyActions(GameState state)
    {
        var body = ModalBody("SMITHY", Gold, new Vector2(410, 0));
        string carried = string.Join("   ", state.Hero.Resources.Where(kv => kv.Value > 0)
            .Select(kv => $"{kv.Key.Replace('-', ' ').ToUpperInvariant()} x{kv.Value}"));
        body.AddChild(LabelOf(carried.Length > 0 ? carried : "NO MATERIALS CARRIED",
            12, Muted, HorizontalAlignment.Center));

        foreach (RecipeDef recipe in RecipeDataService.Instance.Recipes.Values)
        {
            bool can = state.CanCraft(recipe);
            string needs = string.Join(", ", recipe.Inputs.Select(kv => $"{kv.Value}x {kv.Key.Replace('-', ' ')}"));
            var craft = CommandButton(recipe.Name.ToUpperInvariant() + (can ? "" : $"   (need {needs})"),
                can ? Blue : Muted, 340);
            craft.Disabled = !can;
            craft.TooltipText = $"Needs {needs}.";
            craft.Pressed += () => { CloseModal(); state.Craft(recipe); };
            body.AddChild(craft);
        }

        var leave = CommandButton("LEAVE", Muted, 340);
        leave.Pressed += () => CloseModal();
        body.AddChild(leave);
        ShowModal(body, new Vector2(480, 420));
    }

    /// <summary>Market stall: sell inventory items at their rarity price. Rebuilds itself after
    /// each sale (the same self-refresh idiom as the chest menu).</summary>
    public void ShowStallActions(GameState state)
    {
        var body = ModalBody("MARKET STALL", Gold, new Vector2(430, 0));
        body.AddChild(LabelOf($"GOLD: {state.Hero.Gold}", 15, Gold, HorizontalAlignment.Center));

        var sellable = state.Hero.Inventory.Take(8).ToList();
        if (sellable.Count == 0)
            body.AddChild(LabelOf("NOTHING TO SELL", 13, Muted, HorizontalAlignment.Center));
        foreach (Combinable item in sellable)
        {
            var sell = CommandButton($"SELL {item.Name.ToUpperInvariant()}   {state.SellPrice(item)}g", Blue, 360);
            sell.Pressed += () => { state.SellItem(item); ShowStallActions(state); };
            body.AddChild(sell);
        }
        if (state.Hero.Inventory.Count > sellable.Count)
            body.AddChild(LabelOf($"+{state.Hero.Inventory.Count - sellable.Count} more in your pack",
                11, Muted, HorizontalAlignment.Center));

        var leave = CommandButton("LEAVE", Muted, 360);
        leave.Pressed += () => CloseModal();
        body.AddChild(leave);
        ShowModal(body, new Vector2(500, 460));
    }

    /// <summary>Dungeon entrance: confirm a fresh dive (progress checkpoints at the entrance).</summary>
    public void ShowDungeonEntranceActions(GameState state)
    {
        var body = ModalBody("DUNGEON ENTRANCE", Red, new Vector2(390, 0));
        body.AddChild(LabelOf("A fresh dive begins at Floor 1.\nProgress is saved at the entrance.",
            14, Text, HorizontalAlignment.Center));
        var enter = CommandButton("ENTER THE DUNGEON", Red, 300);
        enter.Pressed += () => { CloseModal(); state.EnterDungeon(); };
        body.AddChild(enter);
        var stay = CommandButton("STAY IN TOWN", Muted, 300);
        stay.Pressed += () => CloseModal();
        body.AddChild(stay);
        ShowModal(body, new Vector2(450, 320));
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
        _topHud = new VBoxContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _topHud.AddThemeConstantOverride("separation", 0);

        var top = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
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
        _topHud.AddChild(top);

        _intentBand = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _intentBand.AddThemeStyleboxOverride("panel", Box(Panel with { A = 0.94f }, Border, 0, bottom: 1));
        var intentMargin = Margin(18, 4, 18, 3);
        _intentLabel = LabelOf("", 12, Blue);
        _intentLabel.ClipText = true;
        intentMargin.AddChild(_intentLabel);
        _intentBand.AddChild(intentMargin);
        _topHud.AddChild(_intentBand);
        _hud.AddChild(_topHud);

        _interactionToast = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = -150,
            OffsetRight = 150,
            OffsetTop = -190,
            OffsetBottom = -144,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _interactionToast.AddThemeStyleboxOverride("panel", Box(Ink with { A = 0.94f }, Gold, 3, 1));
        var interactionMargin = Margin(14, 8, 14, 8);
        _interactionLabel = LabelOf("PRESS E   CHEST", 15, Gold, HorizontalAlignment.Center);
        interactionMargin.AddChild(_interactionLabel);
        _interactionToast.AddChild(interactionMargin);
        _hud.AddChild(_interactionToast);

        _bottomHud = new VBoxContainer
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
        _bottomHud.AddThemeConstantOverride("separation", 5);
        _activityLabel = LabelOf("", 13, Blue, HorizontalAlignment.Center);
        _activityLabel.Visible = false;
        _hotbar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _hotbar.AddThemeConstantOverride("separation", 6);
        _bottomHud.AddChild(_activityLabel);
        _bottomHud.AddChild(_hotbar);
        _hud.AddChild(_bottomHud);

        _messagePanel = new PanelContainer
        {
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = 16,
            OffsetRight = 390,
            OffsetTop = -118,
            OffsetBottom = -14,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _messagePanel.AddThemeStyleboxOverride("panel", Box(new Color(0.02f, 0.025f, 0.03f, 0.82f), Border, 3));
        var messageMargin = Margin(12, 8, 12, 8);
        _messageLabel = LabelOf("", 13, Text);
        _messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _messageLabel.VerticalAlignment = VerticalAlignment.Bottom;
        messageMargin.AddChild(_messageLabel);
        _messagePanel.AddChild(messageMargin);
        _hud.AddChild(_messagePanel);

        // Debug console (backtick): a bottom text box pushed into Core's shared command
        // interpreter — same commands as the Avalonia client (help/addgold/settime/moveplayer/...).
        _consolePanel = new PanelContainer
        {
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = 16,
            OffsetRight = -16,
            OffsetTop = -186,
            OffsetBottom = -124,
            Visible = false
        };
        _consolePanel.AddThemeStyleboxOverride("panel", Box(new Color(0.02f, 0.03f, 0.04f, 0.94f), Gold, 3));
        var consoleRows = new VBoxContainer();
        consoleRows.AddThemeConstantOverride("separation", 2);
        _consoleOutput = LabelOf("", 12, Muted);
        _consoleInput = new LineEdit { PlaceholderText = "command…  ('help' lists them)" };
        _consoleInput.TextSubmitted += text =>
        {
            if (_consoleState == null || string.IsNullOrWhiteSpace(text)) return;
            string result = _consoleState.ExecuteDebugCommand(text);
            _consoleOutput.Text = result;
            GD.Print($"CONSOLE> {text}");
            GD.Print(result);
            _consoleInput.Clear();
            RefreshGame(_consoleState);
        };
        _consoleInput.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventKey { Pressed: true } key &&
                (key.PhysicalKeycode == Key.Quoteleft || key.PhysicalKeycode == Key.Escape))
            {
                _consoleInput.AcceptEvent();
                CloseConsole();
            }
        };
        consoleRows.AddChild(_consoleOutput);
        consoleRows.AddChild(_consoleInput);
        var consoleMargin = Margin(10, 6, 10, 6);
        consoleMargin.AddChild(consoleRows);
        _consolePanel.AddChild(consoleMargin);
        _hud.AddChild(_consolePanel);
        _hud.Visible = false;
    }

    private float GetTopHudInset() => _hud.Visible
        ? _topHud.GetCombinedMinimumSize().Y
        : 0f;

    private float GetBottomHudInset()
    {
        if (!_hud.Visible) return 0f;

        float occupiedTop = Mathf.Min(_bottomHud.Position.Y, _messagePanel.Position.Y);
        return Mathf.Max(0f, _hud.Size.Y - occupiedTop);
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
        string equipment = string.Join('|', state.Hero.Equipment.Values.OfType<Weapon>()
            .Select(weapon => $"{weapon.Id}:{weapon.WeaponType}"));
        string training = string.Join('|', state.Hero.WeaponTraining.OrderBy(type => type));
        string counts = string.Join('|', Enumerable.Range(0, state.Hero.HotbarAssignments.Count)
            .Select(state.HotbarConsumableCount));
        string signature = string.Join('|', state.Hero.HotbarAssignments.Select(id => id ?? "·")) + ":" +
            state.Hero.CurrentAttack?.Id + ":" + equipment + ":" + training + ":" +
            state.Hero.HotbarCapacity + ":" + counts;
        if (signature == _hotbarSignature) return;
        _hotbarSignature = signature;
        ClearChildren(_hotbar);

        // Fixed bar: always HotbarCapacity uniform square slots rendered from the positional
        // assignments (managed on the character menu's Hotbar tab) — empty slots render as empty
        // boxes. Each shows its bound key (settings.json hotbarKeys, shared with the Avalonia
        // client) and a compact monogram; details live in the tooltip.
        string[] keyLabels = GameSettings.Current.HotbarKeyLabels;
        int slots = Math.Max(1, state.Hero.HotbarAssignments.Count);
        for (int index = 0; index < slots; index++)
        {
            int slot = index;
            string keyLabel = index < keyLabels.Length ? keyLabels[index] : (index + 1).ToString();
            Attack? attack = state.HotbarAttackAt(index);

            if (attack == null)
            {
                // Consumable quick-slot: monogram + carried count; pressing uses one copy.
                Item? consumable = state.HotbarConsumableAt(index);
                if (consumable != null)
                {
                    int count = state.HotbarConsumableCount(index);
                    var useButton = SmallButton($"{keyLabel}\n{AttackMonogram(consumable.Name)} x{count}", Green);
                    useButton.CustomMinimumSize = new Vector2(52, 52);
                    useButton.FocusMode = FocusModeEnum.None;
                    useButton.TooltipText = $"{consumable.Name} — press to use ({count} carried)";
                    useButton.Pressed += () => { state.ActivateHotbarSlot(slot); _hotbarSignature = ""; RefreshGame(state); };
                    _hotbar.AddChild(useButton);
                    continue;
                }

                var empty = SmallButton(keyLabel, Muted with { A = 0.45f });
                empty.CustomMinimumSize = new Vector2(52, 52);
                empty.Disabled = true;
                empty.FocusMode = FocusModeEnum.None;
                _hotbar.AddChild(empty);
                continue;
            }

            bool selected = ReferenceEquals(attack, state.Hero.CurrentAttack) || attack.Id == state.Hero.CurrentAttack?.Id;
            var button = SmallButton($"{keyLabel}\n{AttackMonogram(attack.Name)}", selected ? Gold : Muted);
            button.CustomMinimumSize = new Vector2(52, 52);
            // HUD buttons never take keyboard focus — a focused Control eats Tab (focus-next)
            // before the game can open the character sheet with it.
            button.FocusMode = FocusModeEnum.None;
            WeaponUseProfile weaponUse = WeaponProficiencyService.Evaluate(state.Hero, attack);
            string cost = attack.IsHeavyAttack
                ? attack.StaminaCost > 0 ? $"\nCost: {attack.StaminaCost} stamina"
                    : attack.ManaCost > 0 ? $"\nCost: {attack.ManaCost} mana"
                    : $"\nCost: {attack.FaithCost} faith"
                : "";
            button.TooltipText = attack.Name + "\n" + attack.Description + cost +
                (weaponUse.UsesWeapon && !weaponUse.IsTrained
                    ? $"\nUntrained {weaponUse.WeaponNames}: " +
                      $"{(1f - weaponUse.DamageMultiplier):P0} damage, " +
                      $"{(1f - weaponUse.AccuracyMultiplier):P0} accuracy penalty."
                    : "");
            button.Pressed += () => { state.SelectAttack(slot); _hotbarSignature = ""; RefreshGame(state); };
            _hotbar.AddChild(button);
        }
    }

    /// <summary>Compact slot label: initials of the first two words, or the first three letters
    /// of a single-word name ("Quick Slash" → QS, "Bow" → BOW) — same rule as the Avalonia bar.</summary>
    private static string AttackMonogram(string name)
    {
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        return name.Length <= 3 ? name.ToUpperInvariant() : name[..3].ToUpperInvariant();
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
        _interactionToast.Visible = false;
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

    private static string ItemDetails(Combinable item, Hero? hero = null)
    {
        string details = $"{item.Name}   {item.Rarity} {item.Kind}";
        if (!string.IsNullOrWhiteSpace(item.Description)) details += $"\n{item.Description}";
        if (item.Attributes.Count > 0) details += $"\n{string.Join(" / ", item.Attributes)}";
        return item switch
        {
            Weapon weapon => details +
                $"\n{WeaponProficiencyService.ResolveType(weapon)}   Damage +{weapon.BaseDamage}   " +
                $"{weapon.HandsRequired}-hand   Range {weapon.Range:0.#}" +
                (hero == null ? "" : $"\n{WeaponProficiencyService.TrainingLabel(hero, weapon)}"),
            Armor armor => details + $"\n{EquipmentSlots.Label(armor.Slot)}   Defense +{armor.DefenseBonus}",
            Spell spell => details + $"\nDamage {spell.BaseDamage}   Range {spell.Range:0.#}   Crit {spell.CritChance:P0}   Mana {spell.ManaCost}",
            Item consumable when consumable.UseEffect == ItemUseEffect.RestoreHealth => details + $"\nRestores {consumable.EffectPower} health",
            Item key when key.KeyId.Length > 0 => details + "\nKey item",
            Item accessory when accessory.EquipSlot.HasValue => details + $"\n{EquipmentSlots.Label(accessory.EquipSlot.Value)}   Defense +{accessory.DefenseBonus}",
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

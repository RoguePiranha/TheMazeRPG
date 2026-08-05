using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.GodotClient;

/// <summary>Owns client navigation and translates Godot input into shared Core operations.</summary>
public partial class GameHost : Node
{
    private DungeonView _dungeonView = null!;
    private Camera2D _camera = null!;
    private GameUi _ui = null!;
    private GameState _gameState = null!;
    private ClientSettings _clientSettings = null!;
    private double _simulationAccumulator;
    private double _secondsPerTick;
    private string? _capturePath;
    private int _captureCountdown;
    private bool _startTurnBased;
    private bool _startEnemyPhase;
    private bool _startIntentPreview;
    private bool _startPathPreview;
    private bool _startAttackPreview;
    private bool _startFogPreview;
    private bool _startInventory;
    private bool _startCharacter;
    private bool _startCharacterCustom;
    private bool _startSaves;
    private bool _startCharacterSheet;
    private bool _startProgression;
    private bool _startOffers;
    private bool _startSpecialization;
    private bool _startAdvancement;
    private bool _startLoot;
    private bool _startCodex;
    private bool _startCombination;
    private bool _startSettings;
    private bool _startChest;
    private bool _startChestPrompt;
    private bool _startDeath;
    private bool _inGame;
    private bool _deathShown;
    private bool _dungeonExitShown;
    private bool _wasRunningBeforeModal;
    private Vector2I _lastTacticalDirection = Vector2I.Right;
    private readonly Queue<Vector2I> _queuedTacticalPath = new();
    private double _tacticalPathStepAccumulator;
    private const double TacticalPathStepSeconds = 0.07;

    public GameState State => _gameState;
    public ClientSettings Settings => _clientSettings;

    public override void _Ready()
    {
        ConfigureInput();
        ConfigurePaths();
        _clientSettings = ClientSettings.Load();
        ConfigureDiagnosticCapture();

        _dungeonView = GetNode<DungeonView>("World/DungeonView");
        _camera = GetNode<Camera2D>("World/Camera2D");
        _ui = GetNode<GameUi>("Interface/GameUi");
        WireUi();
        ApplyClientSettings();

        SetPreviewState();
        if (_startCodex || _startSettings)
        {
            ShowTitle();
            if (_startCodex) _ui.ShowCodex();
            if (_startSettings) _ui.ShowSettings(_clientSettings);
        }
        else if (_startSaves)
        {
            ShowSaves();
        }
        else if (_startCharacter || _startCharacterCustom)
        {
            _ui.ShowCharacterCreation(new CharacterDataService(), _startCharacterCustom);
        }
        else if (_startTurnBased || _startEnemyPhase || _startIntentPreview || _startPathPreview || _startAttackPreview || _startFogPreview || _startInventory ||
                 _startCharacterSheet || _startProgression || _startOffers || _startSpecialization || _startAdvancement ||
                 _startLoot || _startCombination || _startChest || _startChestPrompt || _startDeath)
        {
            string characterClass = _startCombination || _startAttackPreview ? "Mage Apprentice"
                : _startInventory || _startProgression || _startSpecialization ? "Warrior"
                : _startAdvancement ? "Mage Apprentice" : "Wanderer";
            if (_startOffers)
                StartNewGame(StarterLoadoutService.Instance.SelectionFromKit(
                    "Wayfarer", "Human", "soldier"), persist: false);
            else
                StartNewGame("Wayfarer", characterClass, "Human", persist: false);
            if (_startTurnBased || _startEnemyPhase || _startIntentPreview || _startPathPreview || _startAttackPreview)
                _gameState.SetSimulationMode(SimulationMode.TurnBased);
            if (_startEnemyPhase)
                StartDiagnosticEnemyPhase();
            else if (_startIntentPreview)
                StartDiagnosticIntentPreview();
            else if (_startPathPreview)
                StartDiagnosticPathPreview();
            else if (_startAttackPreview)
                StartDiagnosticAttackPreview();
            else if (_startFogPreview)
                StartDiagnosticFogPreview();
            if (_startInventory)
            {
                _gameState.Hero.Inventory.Add(CombinableCatalog.Bow());
                _gameState.Hero.Inventory.Add(CombinableCatalog.LeatherCoat());
                _gameState.Hero.Inventory.Add(CombinableCatalog.LeatherGloves());
                _gameState.Hero.Inventory.Add(CombinableCatalog.WardRing());
                _ui.ShowInventory(_gameState);
            }
            if (_startCharacterSheet)
                _ui.ShowCharacterSheet(_gameState);
            if (_startProgression)
            {
                ProgressionService.Instance.GrantSharedXp(_gameState.Hero, 250);
                _gameState.TryActivateProfession("miner", out _);
                _gameState.RecordProfessionAction("miner");
                _gameState.RecordProfessionAction("miner");
                _ui.ShowProgression(_gameState);
            }
            if (_startOffers)
            {
                ProgressionSlot slot = _gameState.Hero.Progression.ClassSlots[0];
                _ui.ShowProgressionOffers(_gameState, ProgressionDomain.Class, slot.SlotId);
            }
            if (_startSpecialization)
            {
                ProgressionSlot slot = _gameState.Hero.Progression.ClassSlots[0];
                slot.Instance!.Level = 10;
                _ui.ShowSpecializationOffers(_gameState, slot.SlotId);
            }
            if (_startAdvancement)
            {
                ProgressionSlot slot = _gameState.Hero.Progression.ClassSlots[0];
                slot.Instance!.Level = 25;
                _gameState.RecordProgressionFact("knowledge.independent-spell-construction");
                _ui.ShowAdvancementOffers(_gameState, slot.SlotId);
            }
            if (_startLoot && _gameState.Enemies.FirstOrDefault() is { } corpse)
            {
                corpse.Hp = 0;
                corpse.Gold = 17;
                corpse.Inventory.Add(CombinableCatalog.HealthPotion());
                _ui.ShowLoot(_gameState, corpse);
            }
            if (_startCombination)
                _ui.ShowCombination(_gameState);
            if (_startChest || _startChestPrompt)
                StartDiagnosticChest(openMenu: _startChest);
            if (_startDeath)
                _gameState.Hero.CurrentHp = 0;
        }
        else
        {
            ShowTitle();
        }

        GD.Print($"GODOT_SHELL_READY saves={SaveService.ListSaves().Count}");
    }

    public override void _Process(double delta)
    {
        if (_capturePath == null || --_captureCountdown > 0)
            return;

        string? directory = Path.GetDirectoryName(_capturePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Texture2D? viewportTexture = GetViewport().GetTexture();
        Image? frame = viewportTexture?.GetImage();
        if (frame == null)
        {
            GD.PushError("GODOT_FRAME_CAPTURE failed: the active display driver has no viewport texture.");
            _capturePath = null;
            GetTree().Quit(1);
            return;
        }
        Error result = frame.SavePng(_capturePath);
        GD.Print($"GODOT_FRAME_CAPTURE result={result} path={_capturePath}");
        _capturePath = null;
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_inGame || !_gameState.IsRunning || _ui.IsModalOpen)
            return;
        if (_startEnemyPhase)
            return;

        if (_gameState.SimulationMode == SimulationMode.RealTime)
        {
            Vector2 movement = Input.GetVector("move_left", "move_right", "move_up", "move_down");
            _gameState.SetManualMoveIntent(movement.X, movement.Y);

            if (Input.IsActionJustPressed("dash"))
                _gameState.TryDash();

            AdvanceRealTime(delta);
        }
        else
        {
            _gameState.SetManualMoveIntent(0f, 0f);
            if (_gameState.TacticalTurn.IsPlayerTurn)
            {
                _simulationAccumulator = 0;
                AdvanceQueuedTacticalPath(delta);
            }
            else
            {
                _simulationAccumulator += delta;
                if (_simulationAccumulator >= _secondsPerTick)
                {
                    _gameState.AdvanceTacticalTurnTick();
                    _simulationAccumulator -= _secondsPerTick;
                    _simulationAccumulator = Math.Min(_simulationAccumulator, _secondsPerTick);
                }
            }
        }

        FollowHero();
        UpdateTacticalPathPreview();
        UpdateHoveredInteraction();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();

        if (_gameState.IsInOverworld && !_dungeonExitShown)
        {
            _dungeonExitShown = true;
            _ui.ShowDungeonExit(_gameState);
            return;
        }

        if (_gameState.IsHeroDead && !_deathShown)
        {
            _deathShown = true;
            _gameState.IsRunning = false;
            _ui.ShowDeath(_gameState);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_ui.IsModalOpen)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false } key &&
                (key.PhysicalKeycode == Key.Escape || key.PhysicalKeycode == Key.I) && !_deathShown)
            {
                _ui.HandleModalBack();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_ui.IsFrontEndOpen)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
            {
                ShowTitle();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!_inGame) return;

        if (@event.IsActionPressed("interact"))
        {
            CancelQueuedTacticalPath();
            _gameState.UpdateNearbyInteractable();
            if (_gameState.NearbyInteractable is { Type: MazeFeatureType.Chest } chest)
                _ui.ShowChestActions(_gameState, chest);
            else if (_gameState.NearbyInteractable is { Type: MazeFeatureType.GuardianDoor })
                _ui.ShowSafeRoomChoice(_gameState, challengeGuardian: true);
            else if (_gameState.NearbyInteractable is { Type: MazeFeatureType.Shrine })
                _ui.ShowSafeRoomChoice(_gameState, challengeGuardian: false);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } pressedKey)
        {
            if (pressedKey.PhysicalKeycode == Key.Escape)
            {
                _ui.ShowPause(_gameState);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (pressedKey.PhysicalKeycode == Key.I)
            {
                _ui.ShowInventory(_gameState);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (pressedKey.PhysicalKeycode == Key.Tab)
            {
                _ui.ShowCharacterSheet(_gameState);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (TryGetHotbarIndex(pressedKey.PhysicalKeycode, out int slot))
            {
                _gameState.SelectAttack(slot);
                _ui.RefreshGame(_gameState);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event.IsActionPressed("toggle_turn_mode"))
        {
            CancelQueuedTacticalPath();
            _gameState.ToggleSimulationMode();
            _simulationAccumulator = 0;
            _ui.RefreshGame(_gameState);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_gameState.SimulationMode == SimulationMode.TurnBased &&
            @event is InputEventKey { Pressed: true, Echo: false })
        {
            if (TryHandleTacticalKey(@event))
            {
                _ui.RefreshGame(_gameState);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is not InputEventMouseButton mouse || !mouse.Pressed)
            return;

        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _gameState.CycleAttack(mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            _ui.RefreshGame(_gameState);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Right)
        {
            CancelQueuedTacticalPath();
            TryOpenInteraction();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mouse.ButtonIndex != MouseButton.Left) return;
        Vector2 mouseTile = MouseTile();
        float dirX = mouseTile.X - _gameState.Hero.X;
        float dirY = mouseTile.Y - _gameState.Hero.Y;
        if (_gameState.SimulationMode == SimulationMode.TurnBased)
        {
            CancelQueuedTacticalPath();
            int targetX = (int)MathF.Round(mouseTile.X);
            int targetY = (int)MathF.Round(mouseTile.Y);
            IReadOnlyList<(int x, int y)> path = _gameState.TacticalTurn.IsPlayerTurn &&
                _dungeonView.IsCellSeen(targetX, targetY) &&
                !mouse.ShiftPressed && !IsLivingEnemyAt(targetX, targetY)
                ? _gameState.GetTacticalPathTo(targetX, targetY)
                : Array.Empty<(int x, int y)>();
            if (path.Any(cell => !_dungeonView.IsCellSeen(cell.x, cell.y)))
                path = Array.Empty<(int x, int y)>();
            bool moved = path.Count > 0;
            if (moved) QueueTacticalPath(path);
            if (!moved && _dungeonView.IsCellVisible(targetX, targetY))
                _gameState.TryTacticalAttackAt(targetX, targetY);
            _dungeonView.TacticalAttackPreview = null;
        }
        else
            _gameState.FireManualAttack(dirX, dirY);
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (_gameState != null)
        {
            if (_inGame && !_gameState.IsHeroDead && _gameState.CanSave)
                SaveService.Save(_gameState);
            _gameState.IsRunning = false;
        }
    }

    private void WireUi()
    {
        _ui.NewGameRequested += () =>
        {
            _deathShown = false;
            _ui.ShowCharacterCreation(new CharacterDataService());
        };
        _ui.ContinueRequested += ShowSaves;
        _ui.CharacterCreated += selection => StartNewGame(selection, persist: true);
        _ui.SaveLoadRequested += LoadGame;
        _ui.SaveDeleteRequested += id => { SaveService.Delete(id); ShowSaves(); };
        _ui.BackToTitleRequested += ShowTitle;
        _ui.SaveRequested += () =>
        {
            if (_gameState.CanSave)
            {
                SaveService.Save(_gameState);
                _ui.MarkSaved();
            }
        };
        _ui.QuitToTitleRequested += QuitToTitle;
        _ui.QuitToDesktopRequested += () =>
        {
            if (_inGame && !_gameState.IsHeroDead && _gameState.CanSave)
                SaveService.Save(_gameState);
            _inGame = false;
            GetTree().Quit();
        };
        _ui.RestartRequested += () =>
        {
            _ui.CloseModal(false);
            _gameState.RestartGame();
            _gameState.SetControlMode(ControlMode.Manual);
            _gameState.Hero.Inventory.Add(CombinableCatalog.HealthPotion());
            SaveService.Save(_gameState);
            EnterGame(_gameState);
        };
        _ui.DiveAgainRequested += () =>
        {
            _ui.CloseModal(false);
            _gameState.IsRunning = true;
            _gameState.EnterDungeon();
            EnterGame(_gameState);
        };
        _ui.SettingsChanged += _ => ApplyClientSettings();
        _ui.ModalOpened += () =>
        {
            if (!_inGame) return;
            ClearHoveredInteraction();
            _wasRunningBeforeModal = _gameState.IsRunning;
            _gameState.IsRunning = false;
            _gameState.SetManualMoveIntent(0, 0);
        };
        _ui.ModalClosed += () =>
        {
            if (_inGame && !_gameState.IsHeroDead)
                _gameState.IsRunning = _wasRunningBeforeModal;
        };
    }

    private void ShowTitle()
    {
        _inGame = false;
        _deathShown = false;
        _dungeonExitShown = false;
        _gameState.IsRunning = false;
        _gameState.SetManualMoveIntent(0, 0);
        _ui.ShowTitle(SaveService.HasAnySaves());
    }

    private void ShowSaves() => _ui.ShowSaves(SaveService.ListSaves());

    private void StartNewGame(string name, string characterClass, string race, bool persist)
    {
        var state = new GameState(System.Environment.TickCount, name, characterClass, race)
        {
            IsRunning = true
        };
        state.SetControlMode(ControlMode.Manual);
        state.Hero.Inventory.Add(CombinableCatalog.HealthPotion());
        if (persist) SaveService.Save(state);
        EnterGame(state);
    }

    private void StartNewGame(CharacterCreationSelection selection, bool persist)
    {
        var state = new GameState(System.Environment.TickCount, selection) { IsRunning = true };
        state.SetControlMode(ControlMode.Manual);
        if (persist) SaveService.Save(state);
        EnterGame(state);
    }

    private void LoadGame(string saveId)
    {
        SaveData? data = SaveService.Load(saveId);
        if (data == null)
        {
            ShowSaves();
            return;
        }

        var state = GameState.FromSave(System.Environment.TickCount, data);
        state.SetControlMode(ControlMode.Manual);
        state.IsRunning = true;

        // The Godot migration intentionally remains dungeon-only. Town-resume saves begin a new
        // dive from their preserved entrance snapshot until the overworld scene is ported.
        if (state.IsInOverworld)
            state.EnterDungeon();
        EnterGame(state);
    }

    private void EnterGame(GameState state)
    {
        if (_gameState != null && !ReferenceEquals(_gameState, state))
            _gameState.IsRunning = false;
        _gameState = state;
        // Live-game-only ambience (critters + flavor lines) — the same seam rule the Avalonia
        // client uses: headless demos stay silent, real play gets the alive layer.
        state.EnableAmbience = true;
        _secondsPerTick = 1.0 / Math.Max(1, GameSettings.Current.TickRate);
        _simulationAccumulator = 0;
        _inGame = true;
        _deathShown = false;
        _dungeonExitShown = false;
        _dungeonView.State = state;
        _dungeonView.TacticalPathPreview = Array.Empty<(int x, int y)>();
        _dungeonView.TacticalAttackPreview = null;
        ClearHoveredInteraction();
        CancelQueuedTacticalPath();
        _ui.ShowGame(state);
        SnapCameraToHero();
        _dungeonView.QueueRedraw();
        GD.Print($"GODOT_DUNGEON_READY hero={state.Hero.Name} floor={state.CurrentFloor} theme={state.CurrentMaze.Dungeon?.Theme}");
    }

    private void QuitToTitle()
    {
        if (_gameState.CanSave) SaveService.Save(_gameState);
        _ui.CloseModal(false);
        ShowTitle();
    }

    private void SetPreviewState()
    {
        _gameState = new GameState(1729, "Wayfarer", "Wanderer", "Human");
        _gameState.SetControlMode(ControlMode.Manual);
        _gameState.IsRunning = false;
        _secondsPerTick = 1.0 / Math.Max(1, GameSettings.Current.TickRate);
        _dungeonView.State = _gameState;
        SnapCameraToHero();
        _dungeonView.QueueRedraw();
    }

    private void TryOpenInteraction()
    {
        var (feature, corpse) = FindMouseInteraction();
        if (corpse != null) _ui.ShowCorpseActions(_gameState, corpse);
        else if (feature is { Type: MazeFeatureType.Trap }) _ui.ShowTrapActions(_gameState, feature);
    }

    private void UpdateHoveredInteraction()
    {
        if (_startChestPrompt) return;
        var (feature, corpse) = FindMouseInteraction();
        _dungeonView.HoveredFeature = feature;
        _dungeonView.HoveredCorpse = corpse;
    }

    private (MazeFeature? feature, Enemy? corpse) FindMouseInteraction()
    {
        Vector2 tile = MouseTile();
        const float pickRadius = 0.65f;
        float best = pickRadius;
        MazeFeature? selectedFeature = null;
        Enemy? selectedCorpse = null;

        foreach (MazeFeature feature in _gameState.CurrentMaze.Features)
        {
            bool eligibleTrap = feature.Type == MazeFeatureType.Trap &&
                (!feature.Hidden || feature.Perceived);
            bool eligibleChest = feature.Type == MazeFeatureType.Chest &&
                ReferenceEquals(feature, _gameState.NearbyInteractable);
            if (feature.IsUsed || (!eligibleTrap && !eligibleChest)) continue;
            if (!_dungeonView.IsCellVisible(feature.X, feature.Y)) continue;
            float distance = tile.DistanceTo(new Vector2(feature.X, feature.Y));
            if (distance > best) continue;
            best = distance;
            selectedFeature = feature;
            selectedCorpse = null;
        }

        foreach (Enemy enemy in _gameState.Enemies)
        {
            if (enemy.IsAlive || !_dungeonView.IsCellVisible(
                    (int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y))) continue;
            float distance = tile.DistanceTo(new Vector2(enemy.X, enemy.Y));
            if (distance > best) continue;
            best = distance;
            selectedFeature = null;
            selectedCorpse = enemy;
        }
        return (selectedFeature, selectedCorpse);
    }

    private void ClearHoveredInteraction()
    {
        if (_dungeonView == null) return;
        _dungeonView.HoveredFeature = null;
        _dungeonView.HoveredCorpse = null;
        _dungeonView.QueueRedraw();
    }

    private Vector2 MouseTile() =>
        _dungeonView.GetGlobalMousePosition() / DungeonView.CellSize - Vector2.One * 0.5f;

    private void UpdateTacticalPathPreview()
    {
        if (_startPathPreview || _startAttackPreview) return;
        if (_queuedTacticalPath.Count > 0)
        {
            _dungeonView.TacticalAttackPreview = null;
            _dungeonView.TacticalPathPreview = _queuedTacticalPath
                .Select(cell => (x: cell.X, y: cell.Y)).ToArray();
            return;
        }
        _dungeonView.TacticalPathPreview = Array.Empty<(int x, int y)>();
        _dungeonView.TacticalAttackPreview = null;
        if (_gameState.SimulationMode != SimulationMode.TurnBased ||
            !_gameState.TacticalTurn.IsPlayerTurn)
            return;

        Vector2 tile = MouseTile();
        int targetX = (int)MathF.Round(tile.X);
        int targetY = (int)MathF.Round(tile.Y);
        if (!_dungeonView.IsCellSeen(targetX, targetY)) return;
        bool attackTargeting = Input.IsKeyPressed(Key.Shift) || IsLivingEnemyAt(targetX, targetY);
        if (!attackTargeting)
        {
            _dungeonView.TacticalPathPreview = _gameState.GetTacticalPathTo(targetX, targetY);
            if (_dungeonView.TacticalPathPreview.Count > 0 &&
                _dungeonView.TacticalPathPreview.All(cell => _dungeonView.IsCellSeen(cell.x, cell.y)))
                return;
            _dungeonView.TacticalPathPreview = Array.Empty<(int x, int y)>();
        }
        if (!_dungeonView.IsCellVisible(targetX, targetY)) return;
        _dungeonView.TacticalAttackPreview = _gameState.GetTacticalAttackPreview(targetX, targetY);
    }

    private bool IsLivingEnemyAt(int x, int y) =>
        _dungeonView.IsCellVisible(x, y) && _gameState.Enemies.Any(enemy => enemy.IsAlive &&
            (int)MathF.Round(enemy.X) == x && (int)MathF.Round(enemy.Y) == y);

    private void QueueTacticalPath(IReadOnlyList<(int x, int y)> path)
    {
        CancelQueuedTacticalPath();
        foreach (var cell in path)
            _queuedTacticalPath.Enqueue(new Vector2I(cell.x, cell.y));
        _tacticalPathStepAccumulator = 0;
        _dungeonView.TacticalPathPreview = path;
        _dungeonView.TacticalAttackPreview = null;
    }

    private void AdvanceQueuedTacticalPath(double delta)
    {
        if (_queuedTacticalPath.Count == 0) return;
        if (_gameState.SimulationMode != SimulationMode.TurnBased ||
            !_gameState.TacticalTurn.IsPlayerTurn)
        {
            CancelQueuedTacticalPath();
            return;
        }

        _tacticalPathStepAccumulator += delta;
        if (_tacticalPathStepAccumulator < TacticalPathStepSeconds) return;
        _tacticalPathStepAccumulator = 0;
        Vector2I next = _queuedTacticalPath.Peek();
        int dx = next.X - _gameState.Hero.GridX;
        int dy = next.Y - _gameState.Hero.GridY;
        if (Math.Abs(dx) + Math.Abs(dy) != 1 || !_gameState.TryTacticalMove(dx, dy))
        {
            CancelQueuedTacticalPath();
            return;
        }

        _queuedTacticalPath.Dequeue();
        if (!_gameState.TacticalTurn.IsPlayerTurn)
            CancelQueuedTacticalPath();
    }

    private void CancelQueuedTacticalPath()
    {
        _queuedTacticalPath.Clear();
        _tacticalPathStepAccumulator = 0;
        if (_dungeonView != null)
        {
            _dungeonView.TacticalPathPreview = Array.Empty<(int x, int y)>();
            _dungeonView.TacticalAttackPreview = null;
        }
    }

    private void ConfigurePaths()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string localData = Path.Combine(projectRoot, "Data");
        string contentRoot = Directory.Exists(localData)
            ? projectRoot
            : Directory.GetParent(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
              ?? projectRoot;
        string saveRoot = ProjectSettings.GlobalizePath("user://");
        MigrateLegacyProgress(contentRoot, saveRoot);
        GamePaths.Configure(contentRoot, saveRoot);
    }

    private static void MigrateLegacyProgress(string contentRoot, string saveRoot)
    {
        try
        {
            string legacySaves = Path.Combine(contentRoot, "Saves");
            if (!Directory.Exists(legacySaves)) return;

            string targetSaves = Path.Combine(saveRoot, "Saves");
            Directory.CreateDirectory(targetSaves);
            string legacyCodex = Path.Combine(legacySaves, "codex.json");
            string targetCodex = Path.Combine(targetSaves, "codex.json");
            if (File.Exists(legacyCodex) && !File.Exists(targetCodex))
                File.Copy(legacyCodex, targetCodex);

            string legacyCharacters = Path.Combine(legacySaves, "Characters");
            if (!Directory.Exists(legacyCharacters)) return;
            string targetCharacters = Path.Combine(targetSaves, "Characters");
            Directory.CreateDirectory(targetCharacters);
            foreach (string source in Directory.EnumerateFiles(legacyCharacters, "*.json"))
            {
                string target = Path.Combine(targetCharacters, Path.GetFileName(source));
                if (!File.Exists(target)) File.Copy(source, target);
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Legacy progress migration skipped: {exception.Message}");
        }
    }

    private void ConfigureDiagnosticCapture()
    {
        const string prefix = "--capture-frame=";
        string[] arguments = OS.GetCmdlineUserArgs();
        _startTurnBased = arguments.Any(value => value.Equals("--start-turn-based", StringComparison.OrdinalIgnoreCase));
        _startEnemyPhase = arguments.Any(value => value.Equals("--start-enemy-phase", StringComparison.OrdinalIgnoreCase));
        _startIntentPreview = arguments.Any(value => value.Equals("--start-intent-preview", StringComparison.OrdinalIgnoreCase));
        _startPathPreview = arguments.Any(value => value.Equals("--start-path-preview", StringComparison.OrdinalIgnoreCase));
        _startAttackPreview = arguments.Any(value => value.Equals("--start-attack-preview", StringComparison.OrdinalIgnoreCase));
        _startFogPreview = arguments.Any(value => value.Equals("--start-fog-preview", StringComparison.OrdinalIgnoreCase));
        _startInventory = arguments.Any(value => value.Equals("--start-inventory", StringComparison.OrdinalIgnoreCase));
        _startCharacter = arguments.Any(value => value.Equals("--start-character", StringComparison.OrdinalIgnoreCase));
        _startCharacterCustom = arguments.Any(value => value.Equals("--start-character-custom", StringComparison.OrdinalIgnoreCase));
        _startSaves = arguments.Any(value => value.Equals("--start-saves", StringComparison.OrdinalIgnoreCase));
        _startCharacterSheet = arguments.Any(value => value.Equals("--start-character-sheet", StringComparison.OrdinalIgnoreCase));
        _startProgression = arguments.Any(value => value.Equals("--start-progression", StringComparison.OrdinalIgnoreCase));
        _startOffers = arguments.Any(value => value.Equals("--start-offers", StringComparison.OrdinalIgnoreCase));
        _startSpecialization = arguments.Any(value => value.Equals("--start-specialization", StringComparison.OrdinalIgnoreCase));
        _startAdvancement = arguments.Any(value => value.Equals("--start-advancement", StringComparison.OrdinalIgnoreCase));
        _startLoot = arguments.Any(value => value.Equals("--start-loot", StringComparison.OrdinalIgnoreCase));
        _startCodex = arguments.Any(value => value.Equals("--start-codex", StringComparison.OrdinalIgnoreCase));
        _startCombination = arguments.Any(value => value.Equals("--start-combination", StringComparison.OrdinalIgnoreCase));
        _startSettings = arguments.Any(value => value.Equals("--start-settings", StringComparison.OrdinalIgnoreCase));
        _startChest = arguments.Any(value => value.Equals("--start-chest", StringComparison.OrdinalIgnoreCase));
        _startChestPrompt = arguments.Any(value => value.Equals("--start-chest-prompt", StringComparison.OrdinalIgnoreCase));
        _startDeath = arguments.Any(value => value.Equals("--start-death", StringComparison.OrdinalIgnoreCase));
        string? argument = arguments.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument == null) return;
        _capturePath = Path.GetFullPath(argument[prefix.Length..]);
        _captureCountdown = _startEnemyPhase ? 5 : 8;
    }

    private void StartDiagnosticChest(bool openMenu)
    {
        MazeFeature? chest = _gameState.CurrentMaze.Features
            .FirstOrDefault(feature => feature.Type == MazeFeatureType.Chest);
        if (chest == null) return;

        var neighbors = new[] { Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down };
        Vector2I position = neighbors
            .Select(offset => new Vector2I(chest.X + offset.X, chest.Y + offset.Y))
            .FirstOrDefault(cell => _gameState.CurrentMaze.IsWalkable(cell.X, cell.Y));
        if (position == default) position = new Vector2I(chest.X, chest.Y);
        _gameState.Hero.X = position.X;
        _gameState.Hero.Y = position.Y;
        chest.IsLocked = true;
        chest.IsTrapped = true;
        chest.RequiredKeyId = "diagnostic-chest-key";
        _gameState.Hero.Inventory.Add(CombinableCatalog.ChestKey(chest.RequiredKeyId));
        _gameState.UpdateNearbyInteractable();
        if (!openMenu) _dungeonView.HoveredFeature = chest;
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
        if (openMenu) _ui.ShowChestActions(_gameState, chest);
    }

    private void StartDiagnosticEnemyPhase()
    {
        Enemy? actor = _gameState.Enemies.FirstOrDefault();
        if (actor == null) return;

        var directions = new[] { (x: 1, y: 0), (x: -1, y: 0), (x: 0, y: 1), (x: 0, y: -1) };
        var heroCell = _gameState.CurrentMaze.GetEmptyCells()
            .Where(candidate => directions.Any(direction =>
                _gameState.CurrentMaze.IsWalkable(candidate.x + direction.x, candidate.y + direction.y)))
            .OrderBy(candidate => Math.Abs(candidate.x - _gameState.CurrentMaze.Width / 2) +
                Math.Abs(candidate.y - _gameState.CurrentMaze.Height / 2))
            .First();
        _gameState.Hero.X = heroCell.x;
        _gameState.Hero.Y = heroCell.y;
        var cell = directions.FirstOrDefault(direction =>
            _gameState.CurrentMaze.IsWalkable(_gameState.Hero.GridX + direction.x,
                _gameState.Hero.GridY + direction.y));
        if (cell == default) return;

        _gameState.Enemies.Clear();
        actor.X = _gameState.Hero.GridX + cell.x;
        actor.Y = _gameState.Hero.GridY + cell.y;
        actor.TargetX = actor.X;
        actor.TargetY = actor.Y;
        actor.AttackRange = 1.5f;
        actor.InCombat = true;
        _gameState.Enemies.Add(actor);
        _gameState.EndTacticalTurn();
        _gameState.AdvanceTacticalTurnTick();
        _gameState.AdvanceTacticalTurnTick();
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
    }

    private void StartDiagnosticIntentPreview()
    {
        Enemy[] actors = _gameState.Enemies.Take(2).ToArray();
        if (actors.Length < 2) return;

        var directions = new[] { (x: 1, y: 0), (x: -1, y: 0), (x: 0, y: 1), (x: 0, y: -1) };
        var layout = _gameState.CurrentMaze.GetEmptyCells()
            .Select(cell => (cell, lanes: directions.Where(direction =>
                _gameState.CurrentMaze.IsWalkable(cell.x + direction.x, cell.y + direction.y) &&
                _gameState.CurrentMaze.IsWalkable(cell.x + direction.x * 2, cell.y + direction.y * 2)).ToArray()))
            .Where(candidate => candidate.lanes.Length >= 2)
            .OrderBy(candidate => Math.Abs(candidate.cell.x - _gameState.CurrentMaze.Width / 2) +
                Math.Abs(candidate.cell.y - _gameState.CurrentMaze.Height / 2))
            .First();

        _gameState.Hero.X = layout.cell.x;
        _gameState.Hero.Y = layout.cell.y;
        _gameState.Enemies.Clear();

        Enemy attacker = actors[0];
        attacker.X = layout.cell.x + layout.lanes[0].x;
        attacker.Y = layout.cell.y + layout.lanes[0].y;
        attacker.TargetX = attacker.X;
        attacker.TargetY = attacker.Y;
        attacker.AttackRange = 1.5f;
        attacker.Agility = 20;
        attacker.InCombat = true;
        _gameState.Enemies.Add(attacker);

        Enemy mover = actors[1];
        mover.X = layout.cell.x + layout.lanes[1].x * 2;
        mover.Y = layout.cell.y + layout.lanes[1].y * 2;
        mover.TargetX = mover.X;
        mover.TargetY = mover.Y;
        mover.AttackRange = 0.25f;
        mover.Agility = 10;
        mover.InCombat = true;
        _gameState.Enemies.Add(mover);

        _gameState.RefreshTacticalIntentPreview();
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
    }

    private void StartDiagnosticPathPreview()
    {
        _gameState.Enemies.Clear();
        var heroCell = _gameState.CurrentMaze.GetEmptyCells()
            .OrderBy(cell => Math.Abs(cell.x - _gameState.CurrentMaze.Width / 2) +
                Math.Abs(cell.y - _gameState.CurrentMaze.Height / 2))
            .First();
        _gameState.Hero.X = heroCell.x;
        _gameState.Hero.Y = heroCell.y;

        int desiredCost = Math.Min(4, _gameState.TacticalTurn.MovementRemaining);
        var destination = _gameState.CurrentMaze.BfsDistancesFrom(heroCell.x, heroCell.y)
            .Where(pair => pair.Value == desiredCost)
            .OrderBy(pair => Math.Abs(pair.Key.x - heroCell.x) + Math.Abs(pair.Key.y - heroCell.y))
            .First();
        _dungeonView.TacticalPathPreview =
            _gameState.GetTacticalPathTo(destination.Key.x, destination.Key.y);
        _gameState.RefreshTacticalIntentPreview();
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
    }

    private void StartDiagnosticAttackPreview()
    {
        Enemy[] targets = _gameState.Enemies.Take(2).ToArray();
        if (targets.Length < 2) return;

        var directions = new[] { (x: 1, y: 0), (x: -1, y: 0), (x: 0, y: 1), (x: 0, y: -1) };
        var layout = _gameState.CurrentMaze.GetEmptyCells()
            .Select(cell => (cell, lane: directions.FirstOrDefault(direction =>
                _gameState.CurrentMaze.IsWalkable(cell.x + direction.x, cell.y + direction.y) &&
                _gameState.CurrentMaze.IsWalkable(cell.x + direction.x * 2, cell.y + direction.y * 2) &&
                _gameState.CurrentMaze.IsWalkable(cell.x + direction.x * 3, cell.y + direction.y * 3))))
            .Where(candidate => candidate.lane != default)
            .OrderBy(candidate => Math.Abs(candidate.cell.x - _gameState.CurrentMaze.Width / 2) +
                Math.Abs(candidate.cell.y - _gameState.CurrentMaze.Height / 2))
            .First();

        _gameState.Hero.X = layout.cell.x;
        _gameState.Hero.Y = layout.cell.y;
        _gameState.Enemies.Clear();
        for (int index = 0; index < targets.Length; index++)
        {
            Enemy target = targets[index];
            int distance = index + 2;
            target.X = layout.cell.x + layout.lane.x * distance;
            target.Y = layout.cell.y + layout.lane.y * distance;
            target.TargetX = target.X;
            target.TargetY = target.Y;
            target.InCombat = true;
            _gameState.Enemies.Add(target);
        }

        _gameState.SelectAttack(1);
        int targetX = layout.cell.x + layout.lane.x * 3;
        int targetY = layout.cell.y + layout.lane.y * 3;
        _dungeonView.TacticalPathPreview = Array.Empty<(int x, int y)>();
        _dungeonView.TacticalAttackPreview = _gameState.GetTacticalAttackPreview(targetX, targetY);
        _gameState.RefreshTacticalIntentPreview();
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
    }

    private void StartDiagnosticFogPreview()
    {
        Enemy[] actors = _gameState.Enemies.Take(2).ToArray();
        if (actors.Length < 2) return;

        Maze maze = _gameState.CurrentMaze;
        var start = maze.GetEmptyCells()
            .OrderBy(cell => Math.Abs(cell.x - maze.Width / 2) + Math.Abs(cell.y - maze.Height / 2))
            .First();
        var distances = maze.BfsDistancesFrom(start.x, start.y);
        var target = distances
            .Where(pair => pair.Value >= 9 &&
                new Vector2(pair.Key.x - start.x, pair.Key.y - start.y).Length() > _gameState.VisionRange + 1f)
            .OrderBy(pair => Math.Abs(pair.Value - 11))
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (target == default)
            target = distances.OrderByDescending(pair => pair.Value).First().Key;

        _gameState.Hero.X = start.x;
        _gameState.Hero.Y = start.y;
        _dungeonView.RefreshVisibility();

        _gameState.Hero.X = target.x;
        _gameState.Hero.Y = target.y;
        _gameState.Enemies.Clear();
        var directions = new[] { (x: 1, y: 0), (x: -1, y: 0), (x: 0, y: 1), (x: 0, y: -1) };
        var visibleCell = directions
            .Select(direction => (x: target.x + direction.x, y: target.y + direction.y))
            .First(cell => maze.IsWalkable(cell.x, cell.y));

        Enemy visibleActor = actors[0];
        visibleActor.X = visibleCell.x;
        visibleActor.Y = visibleCell.y;
        visibleActor.TargetX = visibleActor.X;
        visibleActor.TargetY = visibleActor.Y;
        visibleActor.InCombat = false;
        _gameState.Enemies.Add(visibleActor);

        Enemy hiddenActor = actors[1];
        hiddenActor.X = start.x;
        hiddenActor.Y = start.y;
        hiddenActor.TargetX = hiddenActor.X;
        hiddenActor.TargetY = hiddenActor.Y;
        hiddenActor.InCombat = false;
        _gameState.Enemies.Add(hiddenActor);

        _gameState.IsRunning = false;
        _dungeonView.RefreshVisibility();
        bool rememberedOrigin = _dungeonView.IsCellSeen(start.x, start.y) &&
            !_dungeonView.IsCellVisible(start.x, start.y);
        bool visibleEnemyShown = _dungeonView.IsCellVisible(visibleCell.x, visibleCell.y);
        bool hiddenEnemyConcealed = !_dungeonView.IsCellVisible(start.x, start.y);
        if (!rememberedOrigin || !visibleEnemyShown || !hiddenEnemyConcealed)
            throw new InvalidOperationException("Fog diagnostic failed to separate visible, remembered, and hidden cells.");
        GD.Print($"GODOT_FOG_DIAGNOSTIC remembered={rememberedOrigin} visible_enemy={visibleEnemyShown} hidden_enemy={hiddenEnemyConcealed}");
        SnapCameraToHero();
        _ui.RefreshGame(_gameState);
        _dungeonView.QueueRedraw();
    }

    private void SnapCameraToHero()
    {
        _camera.Position = CameraTarget();
        _camera.Offset = Vector2.Zero;
        UpdateCameraLimits();
        _camera.ResetSmoothing();
    }

    private void FollowHero()
    {
        _camera.Position = CameraTarget();
        if (_clientSettings.ScreenShake && _gameState.ScreenShake > 0f)
        {
            float strength = _gameState.ScreenShake;
            _camera.Offset = new Vector2(
                (float)GD.RandRange(-strength, strength),
                (float)GD.RandRange(-strength, strength));
        }
        else
        {
            _camera.Offset = Vector2.Zero;
        }
        UpdateCameraLimits();
    }

    private Vector2 CameraTarget()
    {
        Vector2 insets = _ui.PlayAreaInsets;
        float verticalBias = (insets.Y - insets.X) / 2f;
        return DungeonView.WorldToPixel(_gameState.Hero.X, _gameState.Hero.Y) +
            new Vector2(0f, verticalBias);
    }

    private void ApplyClientSettings()
    {
        _camera.PositionSmoothingEnabled = _clientSettings.CameraSmoothing;
        DisplayServer.WindowSetMode(_clientSettings.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(_clientSettings.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);

        bool muted = _clientSettings.MasterVolume <= 0.001f;
        AudioServer.SetBusMute(0, muted);
        if (!muted)
            AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(_clientSettings.MasterVolume));
    }

    private void UpdateCameraLimits()
    {
        Vector2 insets = _ui.PlayAreaInsets;
        _camera.LimitLeft = 0;
        _camera.LimitTop = -Mathf.CeilToInt(insets.X);
        _camera.LimitRight = _gameState.CurrentMaze.Width * DungeonView.CellSize;
        _camera.LimitBottom = _gameState.CurrentMaze.Height * DungeonView.CellSize +
            Mathf.CeilToInt(insets.Y);
    }

    private static void ConfigureInput()
    {
        BindKey("move_up", Key.W);
        BindKey("move_up", Key.Up);
        BindKey("move_down", Key.S);
        BindKey("move_down", Key.Down);
        BindKey("move_left", Key.A);
        BindKey("move_left", Key.Left);
        BindKey("move_right", Key.D);
        BindKey("move_right", Key.Right);
        BindKey("dash", Key.Space);
        BindKey("interact", Key.E);
        BindKey("toggle_turn_mode", Key.Equal);
        BindKey("end_turn", Key.Enter);
        BindKey("bonus_item", Key.Q);
    }

    private static void BindKey(StringName action, Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var input = new InputEventKey { PhysicalKeycode = key };
        if (!InputMap.ActionHasEvent(action, input)) InputMap.ActionAddEvent(action, input);
    }

    private void AdvanceRealTime(double delta)
    {
        _simulationAccumulator += delta;
        int catchUpTicks = 0;
        while (_simulationAccumulator >= _secondsPerTick && catchUpTicks < 4)
        {
            _gameState.Tick();
            _simulationAccumulator -= _secondsPerTick;
            catchUpTicks++;
        }
        if (catchUpTicks == 4)
            _simulationAccumulator = Math.Min(_simulationAccumulator, _secondsPerTick);
    }

    private bool TryHandleTacticalKey(InputEvent @event)
    {
        if (@event.IsActionPressed("end_turn"))
        {
            CancelQueuedTacticalPath();
            return _gameState.EndTacticalTurn();
        }
        if (@event.IsActionPressed("bonus_item"))
        {
            Item? potion = _gameState.Hero.Inventory.OfType<Item>()
                .FirstOrDefault(item => item.UseEffect == ItemUseEffect.RestoreHealth);
            return potion != null && _gameState.TryUseTacticalBonusItem(potion);
        }
        if (@event.IsActionPressed("dash"))
        {
            CancelQueuedTacticalPath();
            return _gameState.TryTacticalDash(_lastTacticalDirection.X, _lastTacticalDirection.Y);
        }

        Vector2I direction = @event.IsActionPressed("move_up") ? Vector2I.Up
            : @event.IsActionPressed("move_down") ? Vector2I.Down
            : @event.IsActionPressed("move_left") ? Vector2I.Left
            : @event.IsActionPressed("move_right") ? Vector2I.Right
            : Vector2I.Zero;
        if (direction == Vector2I.Zero) return false;
        CancelQueuedTacticalPath();
        _lastTacticalDirection = direction;
        return _gameState.TryTacticalMove(direction.X, direction.Y);
    }

    // Per-slot hotbar bindings shared with the Avalonia client via settings.json ("hotbarKeys",
    // Avalonia Key names). Digit names map to the top row; anything else parses as a Godot key
    // name ("Q" → Key.Q); unparseable entries fall back to the numpad only.
    private static readonly Key[] HotbarBindings = ParseHotbarBindings();

    private static Key[] ParseHotbarBindings()
    {
        string[] names = GameSettings.Current.HotbarKeys;
        var keys = new Key[names.Length];
        for (int i = 0; i < names.Length; i++) keys[i] = TranslateBinding(names[i]);
        return keys;
    }

    private static Key TranslateBinding(string name)
    {
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return Key.Key0 + (name[1] - '0');
        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) &&
            name.Length == 7 && char.IsDigit(name[6]))
            return Key.Kp0 + (name[6] - '0');
        return Enum.TryParse(name, ignoreCase: true, out Key parsed) ? parsed : Key.None;
    }

    private static bool TryGetHotbarIndex(Key key, out int index)
    {
        for (int i = 0; i < HotbarBindings.Length; i++)
        {
            if (HotbarBindings[i] != Key.None && HotbarBindings[i] == key)
            {
                index = i;
                return true;
            }
        }
        index = key switch
        {
            >= Key.Kp1 and <= Key.Kp9 => (int)(key - Key.Kp1),
            _ => -1
        };
        return index >= 0;
    }
}

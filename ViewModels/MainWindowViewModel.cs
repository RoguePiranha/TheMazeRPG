using System;
using System.Threading;
using System.Threading.Tasks;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameState _gameState;
    private PeriodicTimer _timer = null!;
    private CancellationTokenSource _cts = null!;

    public GameState GameState => _gameState;

    public MainWindowViewModel() : this("Hero", "Wanderer", "Human")
    {
    }

    public MainWindowViewModel(string characterName, string className, string raceName)
    {
        // Initialize with a random seed (or let user set it)
        int seed = (int)DateTime.Now.Ticks;
        _gameState = new GameState(seed, characterName, className, raceName);

        // Creation-time save: if the game closes/crashes before the first safe room, the
        // character themselves survives (skip re-creating them) even though the dungeon run
        // restarts from floor 1. Done here rather than in the GameState constructor so headless
        // test GameStates don't each write a save slot.
        SaveService.Save(_gameState);

        Initialize();
    }

    /// <summary>Resume an existing character from a save slot (the Continue flow) instead of
    /// creating a fresh one. The GameState is still built normally (so class/race-derived setup
    /// runs), then LoadFrom overwrites it with the saved progress and drops the hero in the
    /// Overworld — same as GameState.LoadFrom's own contract.</summary>
    public MainWindowViewModel(SaveData saveData)
    {
        int seed = (int)DateTime.Now.Ticks;
        _gameState = GameState.FromSave(seed, saveData);
        Initialize();
    }

    private void Initialize()
    {
        _gameState.IsRunning = true;

        // The player-facing game starts in Manual control (the player drives). A bare GameState
        // still defaults to Auto — that's the right default for the headless simulation demos,
        // which are fundamentally auto-play — so this preference lives here, at the live-game seam.
        _gameState.SetControlMode(ControlMode.Manual);

        // Simulation tick loop at the configured rate (Data/Config/settings.json)
        int tickRate = Math.Max(1, GameSettings.Current.TickRate);
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / tickRate));
        _cts = new CancellationTokenSource();

        // Start the simulation loop
        _ = RunSimulationLoop();
    }
    
    private async Task RunSimulationLoop()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                _gameState.Tick();
                
                // TODO: Trigger UI refresh / notify property changed
                // This will be handled by the renderer
            }
        }
        catch (OperationCanceledException)
        {
            // Timer stopped
        }
    }
    
    public void Stop()
    {
        _cts?.Cancel();
    }
}

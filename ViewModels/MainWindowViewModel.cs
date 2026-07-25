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
        Initialize();
    }

    /// <summary>Resume an existing character from a save slot (the Continue flow) instead of
    /// creating a fresh one. The GameState is still built normally (so class/race-derived setup
    /// runs), then LoadFrom overwrites it with the saved progress and drops the hero in the
    /// Overworld — same as GameState.LoadFrom's own contract.</summary>
    public MainWindowViewModel(SaveData saveData)
    {
        int seed = (int)DateTime.Now.Ticks;
        _gameState = new GameState(seed, saveData.HeroName, saveData.ClassName, saveData.RaceName);
        _gameState.LoadFrom(saveData);
        Initialize();
    }

    private void Initialize()
    {
        _gameState.IsRunning = true;

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

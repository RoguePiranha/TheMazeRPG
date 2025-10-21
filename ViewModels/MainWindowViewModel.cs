using System;
using System.Threading;
using System.Threading.Tasks;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameState _gameState;
    private readonly PeriodicTimer _timer;
    private CancellationTokenSource _cts;
    
    public GameState GameState => _gameState;
    
    public MainWindowViewModel() : this("Hero", "Wanderer", "Human")
    {
    }
    
    public MainWindowViewModel(string characterName, string className, string raceName)
    {
        // Initialize with a random seed (or let user set it)
        int seed = (int)DateTime.Now.Ticks;
        _gameState = new GameState(seed, characterName, className, raceName);
        _gameState.IsRunning = true;
        
        // Create a 30 FPS tick loop
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / 30));
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

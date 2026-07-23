using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using TheMazeRPG.Core.Services;

namespace TheMazeRPG.UI.Controls;

public partial class DeathOverlay : UserControl, INotifyPropertyChanged
{
    private GameState? _gameState;
    private DispatcherTimer? _updateTimer;
    private bool _showOverlay;
    private string _deathMessage = "";
    private string _timerMessage = "";
    
    public new event PropertyChangedEventHandler? PropertyChanged;
    
    public bool ShowOverlay
    {
        get => _showOverlay;
        set
        {
            if (_showOverlay != value)
            {
                _showOverlay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowOverlay)));
            }
        }
    }
    
    public string DeathMessage
    {
        get => _deathMessage;
        set
        {
            if (_deathMessage != value)
            {
                _deathMessage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeathMessage)));
            }
        }
    }
    
    public string TimerMessage
    {
        get => _timerMessage;
        set
        {
            if (_timerMessage != value)
            {
                _timerMessage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimerMessage)));
            }
        }
    }
    
    public DeathOverlay()
    {
        InitializeComponent();
        DataContext = this;
        ShowOverlay = false;
        
        // Update timer to check death state
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }
    
    public void SetGameState(GameState gameState)
    {
        _gameState = gameState;
    }
    
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (_gameState == null) return;
        
        if (_gameState.IsHeroDead)
        {
            ShowOverlay = true;
            DeathMessage = $"{_gameState.Hero.Name} died!";
            TimerMessage = $"Restarting in {_gameState.DeathCountdownSeconds:F1}s...";
        }
        else
        {
            ShowOverlay = false;
        }
    }
    
    private void TryAgain_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _gameState?.RestartGame();
    }
    
    private void NewHero_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // For now, just restart - in the future this could open character creation
        _gameState?.RestartGame();
    }
}

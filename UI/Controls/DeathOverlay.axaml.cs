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

        // Update timer to check death state. Started/stopped with visual-tree attachment: a
        // running DispatcherTimer is dispatcher-rooted, so leaving it on would pin this overlay
        // (and its GameState) forever after the view is torn down.
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _updateTimer?.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _updateTimer?.Stop();
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
            var hero = _gameState.Hero;
            DeathMessage = $"{hero.Name} — {hero.Race} {hero.Class}\nLevel {hero.Level}  ·  Floor {_gameState.CurrentFloor}";
            TimerMessage = $"Restarting in {_gameState.DeathCountdownSeconds:F1}s...";
        }
        else
        {
            ShowOverlay = false;
        }
    }
    
    /// <summary>Raised when the player picks "New Hero" — the shell navigates to character
    /// creation (death already deleted this hero's save; they're gone for good).</summary>
    public event Action? NewHeroRequested;

    private void TryAgain_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_gameState == null) return;
        _gameState.RestartGame();
        // Death deleted the save slot; the restarted character (same identity, fresh run) gets a
        // new creation checkpoint so a crash doesn't force re-creating them.
        SaveService.Save(_gameState);
    }

    private void NewHero_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        NewHeroRequested?.Invoke();
    }
}

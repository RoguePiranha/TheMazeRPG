using Avalonia.Controls;
using Avalonia.Input;
using TheMazeRPG.UI.Controls;
using TheMazeRPG.ViewModels;

namespace TheMazeRPG.Views;

public partial class MainWindow : Window
{
    private StatsOverlay? _statsOverlay;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Connect GameCanvas to GameState when DataContext is set
        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                if (this.FindControl<GameCanvas>("GameCanvas") is GameCanvas canvas)
                {
                    canvas.SetGameState(viewModel.GameState);
                }
                
                // Connect StatsOverlay to GameState
                _statsOverlay = this.FindControl<StatsOverlay>("StatsOverlay");
                if (_statsOverlay != null)
                {
                    _statsOverlay.SetGameState(viewModel.GameState);
                }
            }
        };
        
        // Handle keyboard input for stats overlay
        KeyDown += OnKeyDown;
    }
    
    private void StatsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_statsOverlay != null)
        {
            _statsOverlay.IsOverlayVisible = !_statsOverlay.IsOverlayVisible;
        }
    }
    
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            e.Handled = true; // Prevent default Tab behavior
            if (_statsOverlay != null)
            {
                _statsOverlay.IsOverlayVisible = !_statsOverlay.IsOverlayVisible;
            }
        }
    }
}
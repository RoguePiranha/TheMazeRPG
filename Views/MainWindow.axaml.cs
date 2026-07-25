using Avalonia.Controls;
using Avalonia.Input;
using TheMazeRPG.Core.Services;
using TheMazeRPG.ViewModels;

namespace TheMazeRPG.Views;

/// <summary>
/// The single application window: a shell that swaps its content between the title screen,
/// character creation, and the game. Navigation never closes this window — views raise events
/// and the shell switches what's hosted (the SavesWindow picker is the one modal dialog).
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel? _activeViewModel;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        ShowTitle();
    }

    /// <summary>Keyboard routing: the shell owns the window's KeyDown and forwards it to
    /// whichever view is active (views themselves aren't focused controls).</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var host = this.FindControl<ContentControl>("ViewHost");
        switch (host?.Content)
        {
            case TitleView title:
                title.HandleKeyPressed(e);
                break;
            case GameView game:
                game.HandleKey(e);
                break;
        }
    }

    /// <summary>KeyUp routing — only the game view needs it (held-key Manual movement).</summary>
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (this.FindControl<ContentControl>("ViewHost")?.Content is GameView game)
            game.HandleKeyUp(e);
    }

    private void SetView(Control view)
    {
        var host = this.FindControl<ContentControl>("ViewHost");
        if (host != null) host.Content = view;
    }

    // --- Navigation ---

    private void ShowTitle()
    {
        // Leaving a running game: stop its tick loop before dropping the reference.
        _activeViewModel?.Stop();
        _activeViewModel = null;

        var title = new TitleView();
        title.NewGameRequested += ShowCharacterCreation;
        title.ContinueRequested += ContinueFromSave;
        SetView(title);
    }

    private void ShowCharacterCreation()
    {
        var creation = new CharacterSelectView();
        creation.Confirmed += (name, className, raceName) =>
            StartGame(new MainWindowViewModel(name, className, raceName));
        creation.Cancelled += ShowTitle;
        SetView(creation);
    }

    private void ContinueFromSave(string saveId)
    {
        var saveData = SaveService.Load(saveId);
        if (saveData == null)
        {
            // The chosen slot failed to load (deleted/corrupt on disk — rare). Go back to the
            // title rather than silently starting a wrong character.
            ShowTitle();
            return;
        }

        StartGame(new MainWindowViewModel(saveData));
    }

    private void StartGame(MainWindowViewModel viewModel)
    {
        _activeViewModel = viewModel;

        var game = new GameView();
        game.SetViewModel(viewModel);
        game.QuitToMenuRequested += ShowTitle;
        game.QuitToDesktopRequested += Close; // closing the only window exits the app
        game.NewHeroRequested += () =>
        {
            // The dead hero's save is already deleted (permadeath); stop the old run's tick loop
            // and swap to character creation for a fresh start.
            _activeViewModel?.Stop();
            _activeViewModel = null;
            ShowCharacterCreation();
        };
        SetView(game);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // Window closed directly (X button or quit-to-desktop): stop the sim loop cleanly.
        _activeViewModel?.Stop();
        base.OnClosed(e);
    }
}

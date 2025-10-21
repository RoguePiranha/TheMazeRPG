using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using TheMazeRPG.ViewModels;
using TheMazeRPG.Views;

namespace TheMazeRPG;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Show character selection first
            var characterSelect = new CharacterSelectWindow();
            characterSelect.Closed += (s, e) =>
            {
                if (characterSelect.WasConfirmed)
                {
                    // Create main window with selected character
                    var viewModel = new MainWindowViewModel(
                        characterSelect.CharacterName,
                        characterSelect.SelectedClass,
                        characterSelect.SelectedRace);
                    
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = viewModel,
                    };
                    desktop.MainWindow.Show();
                }
                else
                {
                    // User cancelled, exit application
                    desktop.Shutdown();
                }
            };
            
            desktop.MainWindow = characterSelect;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
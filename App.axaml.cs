using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using TheMazeRPG.Core.Services;
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
            
            ShowCharacterSelect(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows character creation/continue. Re-entrant: if Continue is picked but the
    /// chosen save slot fails to load (e.g. deleted on disk between listing and loading — a rare
    /// race, not a normal path), this loops back here instead of silently dropping the player into
    /// a blank default "Hero" character under their old save's name.</summary>
    private void ShowCharacterSelect(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var characterSelect = new CharacterSelectWindow();
        characterSelect.Closed += (s, e) =>
        {
            if (characterSelect.WasConfirmed)
            {
                if (characterSelect.LoadedSaveId != null)
                {
                    // Continue: load the selected save slot instead of creating a fresh character.
                    var saveData = SaveService.Load(characterSelect.LoadedSaveId);
                    if (saveData == null)
                    {
                        ShowCharacterSelect(desktop);
                        return;
                    }

                    desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(saveData) };
                }
                else
                {
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = new MainWindowViewModel(
                            characterSelect.CharacterName,
                            characterSelect.SelectedClass,
                            characterSelect.SelectedRace)
                    };
                }

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
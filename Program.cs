using Avalonia;
using System;

namespace TheMazeRPG;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // If TEST_SIM is set, run headless simulation for testing and exit
        var testSim = Environment.GetEnvironmentVariable("TEST_SIM");
        if (!string.IsNullOrEmpty(testSim) && testSim == "1")
        {
            RunTestSimulation();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Debug/test entrypoint: if TEST_SIM=1 is set, run a short simulation and exit
    public static void RunTestSimulation()
    {
        // Read character customization from environment variables
        var characterName = Environment.GetEnvironmentVariable("CHARACTER_NAME") ?? "Hero";
        var characterClass = Environment.GetEnvironmentVariable("CHARACTER_CLASS") ?? "Wanderer";
        var characterRace = Environment.GetEnvironmentVariable("CHARACTER_RACE") ?? "Human";

        // Pass customization to GameState
        var gs = new TheMazeRPG.Core.Services.GameState(12345, characterName, characterClass, characterRace);
        gs.RunSimulationTicks(200); // run 200 ticks (~20s at 10 ticks/sec)

        // Run tests for all race-class combinations
        var races = new[] { "Human", "Elf", "Dwarf", "Orc" };
        var characterClasses = new[] { "Wanderer", "Mage Apprentice", "Warrior", "Rogue" };

        foreach (var race in races)
        {
            foreach (var charClass in characterClasses)
            {
                Console.WriteLine($"Running simulation for {charClass} {race}...");
                var gameState = new TheMazeRPG.Core.Services.GameState(12345, "TestHero", charClass, race);
                gameState.RunSimulationTicks(500); // Run 500 ticks (~50s at 10 ticks/sec) for better combat testing
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

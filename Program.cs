using Avalonia;
using System;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;

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

        // If TEST_COMBINE is set, exercise the combine engine and exit
        if (Environment.GetEnvironmentVariable("TEST_COMBINE") == "1")
        {
            RunCombineDemo();
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

    // Debug/test entrypoint: if TEST_COMBINE=1 is set, exercise the combine engine and exit
    public static void RunCombineDemo()
    {
        void Show(string label, Combinable a, Combinable b)
        {
            var r = CombinationEngine.Combine(a, b);
            string attrs = r.Attributes.Count > 0 ? string.Join("/", r.Attributes) : "none";
            Console.WriteLine($"[{label}] {a.Name} + {b.Name} = {r.Name}  ({r.Kind}, {r.Rarity}, Lv{r.Level}, {attrs})");
        }

        Console.WriteLine("=== Combination engine demo ===");
        Show("recipe   ", CombinableCatalog.Fireball(), CombinableCatalog.IceShard());
        Show("recipe   ", CombinableCatalog.Bow(), CombinableCatalog.Fireball());
        Show("recipe   ", CombinableCatalog.Fireball(), CombinableCatalog.DenseMusculature());
        Show("recipe   ", CombinableCatalog.Sword(), CombinableCatalog.Dagger());
        Show("intensify", CombinableCatalog.Fireball(), CombinableCatalog.Fireball());
        Show("generic  ", CombinableCatalog.Sword(), CombinableCatalog.Fireball());
        Show("generic  ", CombinableCatalog.IceShard(), CombinableCatalog.ManaCircuitry());
        Show("generic  ", CombinableCatalog.ShieldGenerator(), CombinableCatalog.IceShard());

        Console.WriteLine("\n=== Attack visual routing (id -> VisualStyle) ===");
        foreach (var cls in new[] { "Warrior", "Mage Apprentice", "Rogue", "Archer", "Bard", "Priest", "Wanderer" })
            foreach (var a in AttackFactory.GetStartingAttacks(cls))
                Console.WriteLine($"  {cls,-16} {a.Name,-22} ({a.Id,-22}) -> {AttackVisuals.For(a)}");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

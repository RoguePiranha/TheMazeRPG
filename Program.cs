using Avalonia;
using System;
using System.Linq;
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

        // If TEST_LOOT is set, exercise loot drops + auto-equip and exit
        if (Environment.GetEnvironmentVariable("TEST_LOOT") == "1")
        {
            RunLootDemo();
            return;
        }

        // If TEST_BALANCE is set, dump enemy damage per floor and exit
        if (Environment.GetEnvironmentVariable("TEST_BALANCE") == "1")
        {
            RunBalanceDemo();
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

    // Debug/test entrypoint: if TEST_LOOT=1 is set, simulate chest loot + auto-equip and exit
    public static void RunLootDemo()
    {
        var gs = new GameState(1, "Looter", "Warrior", "Human");
        Console.WriteLine($"=== Loot / equip demo (Warrior, hotbar capacity {gs.Hero.HotbarCapacity}) ===");
        Console.WriteLine($"Start attacks: [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
        for (int floor = 1; floor <= 6; floor++)
        {
            var loot = LootService.Roll(floor, new Random(floor * 7));
            gs.AcquireLoot(loot);
            string attacks = string.Join(", ", gs.Hero.Attacks.Select(a => a.Name));
            string inventory = gs.Hero.Inventory.Count > 0
                ? string.Join(", ", gs.Hero.Inventory.Select(c => c.Name))
                : "(empty)";
            Console.WriteLine($"Floor {floor}: found {loot.Name} ({loot.Rarity} {loot.Kind})");
            Console.WriteLine($"   equipped attacks: [{attacks}]");
            Console.WriteLine($"   inventory:        [{inventory}]");
        }
    }

    // Debug/test entrypoint: if TEST_BALANCE=1 is set, dump enemy damage per floor vs a fresh hero
    public static void RunBalanceDemo()
    {
        var gs = new GameState(42, "Hero", "Wanderer", "Human");
        // Hero mitigation matches CombatSystem: attacker stat damage minus defense/2.
        int heroDef = gs.Hero.Defense + (int)(gs.Hero.EffectiveConstitution * 0.7f);
        Console.WriteLine($"=== Enemy damage vs a fresh Wanderer (100 HP, defense {heroDef}) ===");
        foreach (int targetFloor in new[] { 1, 3, 5, 8 })
        {
            while (gs.CurrentFloor < targetFloor) gs.StartNewFloor();
            Console.WriteLine($"--- Floor {gs.CurrentFloor} ---");
            foreach (var e in gs.Enemies.OrderByDescending(x => x == gs.Boss))
            {
                var atk = e.CurrentAttack;
                int stat = (atk?.Damage ?? 6) + e.Attack;
                bool magic = atk != null && (atk.Animation == AttackAnimation.Magic || atk.ManaCost > 0);
                stat += magic
                    ? (int)(e.Intelligence * 1.2f) + (int)(e.Wisdom * 0.5f)
                    : (int)(e.Strength * 1.2f) + (int)(e.Dexterity * 0.5f);
                int est = System.Math.Max(1, stat - heroDef / 2); // pre-variance (+-25%)
                if (e.IsBoss) est = System.Math.Max((int)(est * 1.35f), 12 + e.Level * 2);
                string tag = e == gs.Boss ? "BOSS " : "     ";
                Console.WriteLine($"  {tag}L{e.Level,2} {e.Race,-10} {e.Class,-16} {atk?.Name,-14} ~{est,2} dmg  HP {e.MaxHp}");
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

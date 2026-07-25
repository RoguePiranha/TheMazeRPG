using Avalonia;
using System;
using System.Collections.Generic;
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

        // If TEST_CODEX is set, check tier distribution + exercise the Codex end-to-end and exit
        if (Environment.GetEnvironmentVariable("TEST_CODEX") == "1")
        {
            RunCodexDemo();
            return;
        }

        // If TEST_DUNGEON is set, exercise the restructured floor pacing (no per-floor boss/key,
        // far-apart stairs, safe rooms just before each Guardian floor: 4.5, 9.5, ...) and exit
        if (Environment.GetEnvironmentVariable("TEST_DUNGEON") == "1")
        {
            RunDungeonRestructureDemo();
            return;
        }

        // If TEST_OVERWORLD is set, exercise the Overworld vertical slice and exit
        if (Environment.GetEnvironmentVariable("TEST_OVERWORLD") == "1")
        {
            RunOverworldDemo();
            return;
        }

        // If TEST_SAVE is set, exercise the save/load round-trip and exit
        if (Environment.GetEnvironmentVariable("TEST_SAVE") == "1")
        {
            RunSaveLoadDemo();
            return;
        }

        // If TEST_DEBUGRACE is set, verify the Debug testing race's pools/exclusions and exit
        if (Environment.GetEnvironmentVariable("TEST_DEBUGRACE") == "1")
        {
            RunDebugRaceDemo();
            return;
        }

        // If TEST_CONTROL is set, verify Auto vs Manual control modes and exit
        if (Environment.GetEnvironmentVariable("TEST_CONTROL") == "1")
        {
            RunControlModeDemo();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Debug/test entrypoint: verify the Debug race's flat pool overrides and that it never
    // spawns as an enemy.
    public static void RunDebugRaceDemo()
    {
        Console.WriteLine("=== Debug race ===");
        var gs = new GameState(1, "Tester", "Warrior", "Debug");
        var h = gs.Hero;
        Console.WriteLine($"HP {h.CurrentHp}/{h.MaxHp} (expect 1000/1000), Stamina {h.CurrentStamina}/{h.MaxStamina} (expect 1000/1000), Mana {h.CurrentMana}/{h.MaxMana} (expect 1000/1000)");
        Console.WriteLine($"Base stats: Str {h.Strength}, Con {h.Constitution}, Int {h.Intelligence}, Wis {h.Wisdom} (expect 100 each); Agi {h.Agility}, Dex {h.Dexterity}, Cha {h.Charisma} (class defaults)");

        var cds = new CharacterDataService();
        var rng = new Random(7);
        int debugSpawns = 0;
        for (int i = 0; i < 500; i++)
        {
            if (EnemyFactory.RandomRegular(3, cds, rng).Race == "Debug") debugSpawns++;
        }
        Console.WriteLine($"Debug-race enemies out of 500 random spawns: {debugSpawns} (expect 0)");
    }

    // Debug/test entrypoint: verify Auto vs Manual control modes.
    public static void RunControlModeDemo()
    {
        Console.WriteLine("=== Control modes ===");

        // Auto mode: the hero explores on its own (position changes without any input).
        var auto = new GameState(4321, "AutoWalker", "Warrior", "Human");
        auto.IsRunning = true;
        float ax0 = auto.Hero.X, ay0 = auto.Hero.Y;
        for (int t = 0; t < 60; t++) auto.Tick();
        float autoMoved = MathF.Sqrt((auto.Hero.X - ax0) * (auto.Hero.X - ax0) + (auto.Hero.Y - ay0) * (auto.Hero.Y - ay0));
        Console.WriteLine($"Auto mode ({auto.ControlMode}): hero auto-moved {autoMoved:F2} tiles with no input (expect > 0)");

        // Manual mode, no input: the hero should hold position (auto-explore is off).
        var man = new GameState(4321, "Driver", "Warrior", "Human");
        man.IsRunning = true;
        man.SetControlMode(ControlMode.Manual);
        float mx0 = man.Hero.X, my0 = man.Hero.Y;
        for (int t = 0; t < 60; t++) man.Tick();
        float idleMoved = MathF.Sqrt((man.Hero.X - mx0) * (man.Hero.X - mx0) + (man.Hero.Y - my0) * (man.Hero.Y - my0));
        Console.WriteLine($"Manual mode, no input: hero moved {idleMoved:F2} tiles (expect ~0)");

        // Manual mode with a rightward intent: the hero should move right until a wall stops it.
        man.SetManualMoveIntent(1, 0);
        float bx = man.Hero.X;
        for (int t = 0; t < 60; t++) man.Tick();
        Console.WriteLine($"Manual mode, holding right: X {bx:F2} -> {man.Hero.X:F2} (expect increase, then wall-stop)");

        // Toggle back to auto and confirm it resumes auto-movement.
        man.ToggleControlMode();
        float cx = man.Hero.X, cy = man.Hero.Y;
        for (int t = 0; t < 60; t++) man.Tick();
        float resumed = MathF.Sqrt((man.Hero.X - cx) * (man.Hero.X - cx) + (man.Hero.Y - cy) * (man.Hero.Y - cy));
        Console.WriteLine($"Toggled back to {man.ControlMode}: hero auto-moved {resumed:F2} tiles (expect > 0)");

        // Manual click-to-fire: a directional shot damages an enemy in its path.
        var gunner = new GameState(555, "Gunner", "Warrior", "Human");
        gunner.IsRunning = true;
        gunner.SetControlMode(ControlMode.Manual);
        var target = gunner.Enemies.FirstOrDefault();
        if (target != null)
        {
            target.X = gunner.Hero.X + 1.0f;
            target.Y = gunner.Hero.Y;
            int hpBefore = target.Hp;
            gunner.FireManualAttack(1f, 0f); // fire toward the enemy (to the right)
            for (int t = 0; t < 20; t++) gunner.Tick();
            Console.WriteLine($"Manual attack (current: {gunner.Hero.CurrentAttack?.Name}): enemy HP {hpBefore} -> {target.Hp} (expect decrease)");
        }
        else
        {
            Console.WriteLine("Manual attack: no enemy present to test.");
        }

        // Hotbar selection: number-key select changes the current attack.
        var hb = new GameState(556, "Switcher", "Warrior", "Human");
        if (hb.Hero.Attacks.Count > 1)
        {
            var first = hb.Hero.CurrentAttack;
            hb.SelectAttack(1);
            Console.WriteLine($"Hotbar select: attack '{first?.Name}' -> '{hb.Hero.CurrentAttack?.Name}' (expect different)");
        }
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
                int xp = (int)((10 + e.MaxHp / 4) * e.XpMultiplier);
                Console.WriteLine($"  {e.Tier,-5} L{e.Level,2} {e.Race,-10} {e.Class,-16} {atk?.Name,-14} ~{est,2} dmg  HP {e.MaxHp,3}  XP {xp,3}");
            }
        }
    }

    // Debug/test entrypoint: if TEST_CODEX=1 is set, check tier distribution + run a real
    // auto-play sim and dump the resulting Codex (bestiary + play stats) and exit
    public static void RunCodexDemo()
    {
        // Tally tier distribution over many floor spawns (expect ~18% Elite, rest Basic, plus
        // exactly 1 Boss per floor). Re-generating StartNewFloor repeatedly replaces gs.Enemies
        // each time, so we tally right after each generation rather than at the end.
        var tallyGs = new GameState(99, "Tally", "Warrior", "Human");
        int basic = 0, elite = 0, boss = 0;
        for (int i = 0; i < 40; i++)
        {
            tallyGs.StartNewFloor();
            foreach (var e in tallyGs.Enemies)
            {
                if (e.IsBoss) boss++;
                else if (e.IsElite) elite++;
                else basic++;
            }
        }
        int total = basic + elite + boss;
        Console.WriteLine($"=== Tier distribution over 40 floor spawns ({total} enemies) ===");
        Console.WriteLine($"  Basic: {basic} ({basic * 100.0 / total:0.0}%)   Elite: {elite} ({elite * 100.0 / total:0.0}%)   Boss: {boss}");

        // The tally loop above called StartNewFloor() 40 times, which (correctly, for real
        // gameplay) records 40 floor-clears through the shared Codex singleton. Wipe that before
        // the real sim below so its report reflects only the real run.
        CodexService.Instance.Reset();

        // Real auto-play sim to exercise kill/death/floor Codex hooks end-to-end.
        var gs = new GameState(7, "Adventurer", "Warrior", "Human");
        gs.IsRunning = true;
        for (int i = 0; i < 20000 && gs.CurrentFloor < 8; i++)
        {
            gs.Tick();
        }
        Console.WriteLine($"\n=== Codex demo: reached Floor {gs.CurrentFloor}, Hero Level {gs.Hero.Level} (XP {gs.Hero.Experience}/{gs.Hero.ExperienceToNext}) ===");
        var data = CodexService.Instance.Data;
        Console.WriteLine($"PlayStats: Kills={data.PlayStats.TotalKills} Deaths={data.PlayStats.TotalDeaths} DeepestFloor={data.PlayStats.DeepestFloor} FloorsCleared={data.PlayStats.TotalFloorsCleared}");
        Console.WriteLine($"Bestiary ({data.Bestiary.Count} species discovered):");
        foreach (var kv in data.Bestiary.OrderByDescending(kv => kv.Value.Killed))
        {
            var e = kv.Value;
            Console.WriteLine($"  {kv.Key,-24} seen {e.Seen,3}  killed {e.Killed,3}  floors {e.FirstFloor}-{e.LastFloor}");
        }
    }

    // Debug/test entrypoint: if TEST_DUNGEON=1 is set, drive a real auto-play sim through several
    // floors and log floor pacing, safe-room entry, Guardian spawn/defeat, and codex results
    public static void RunDungeonRestructureDemo()
    {
        var gs = new GameState(11, "Explorer", "Warrior", "Human");
        gs.IsRunning = true;

        bool wasInSafeRoom = false;
        bool hadGuardian = false;
        int lastFloor = gs.CurrentFloor;

        Console.WriteLine("=== Dungeon restructure demo (floor pacing, safe rooms, guardians) ===");
        Console.WriteLine($"Floor {gs.CurrentFloor} start. Stairs BFS-distance from entrance: {StairsDistance(gs)}");

        for (int i = 0; i < 60000; i++)
        {
            gs.Tick();

            if (gs.CurrentFloor != lastFloor && !gs.IsInSafeRoom)
            {
                lastFloor = gs.CurrentFloor;
                Console.WriteLine($"[tick {i}] Entered Floor {lastFloor}. Stairs BFS-distance: {StairsDistance(gs)}. Enemies: {gs.Enemies.Count} (Boss present: {gs.Boss != null})");
            }
            if (gs.IsInSafeRoom && !wasInSafeRoom)
            {
                wasInSafeRoom = true;
                Console.WriteLine($"[tick {i}] Entered SAFE ROOM after floor {lastFloor}.");
            }
            if (!gs.IsInSafeRoom && wasInSafeRoom)
            {
                wasInSafeRoom = false;
                Console.WriteLine($"[tick {i}] Left safe room (exited to overworld, or floor advanced past it).");
            }
            if (gs.IsInSafeRoom && gs.Boss != null && !hadGuardian)
            {
                hadGuardian = true;
                Console.WriteLine($"[tick {i}] Guardian spawned: {gs.Boss.Race} {gs.Boss.Class} L{gs.Boss.Level}, HP {gs.Boss.MaxHp}");
            }
            if (hadGuardian && gs.Boss == null)
            {
                hadGuardian = false; // resolved (defeated and floor advanced, or room exited)
                Console.WriteLine($"[tick {i}] Guardian resolved.");
            }
        }

        Console.WriteLine($"\nFinal: Floor {gs.CurrentFloor}, InSafeRoom={gs.IsInSafeRoom}, Hero Level {gs.Hero.Level}, HP {gs.Hero.CurrentHp}/{gs.Hero.MaxHp}");
        var codex = CodexService.Instance.Data;
        Console.WriteLine($"Codex: Kills={codex.PlayStats.TotalKills} Deaths={codex.PlayStats.TotalDeaths} DungeonExits={codex.PlayStats.TotalDungeonExits} FloorsCleared={codex.PlayStats.TotalFloorsCleared}");

        // The organic run above may never reach floor 4 (far-apart stairs + repeated deaths).
        // Directly verify the safe-room/Guardian path by teleporting to each floor's stairs
        // (skipping the maze-solving itself) so real game logic drives the interesting part.
        Console.WriteLine("\n=== Fast-forward to floor 4's safe room (teleport to stairs each floor) ===");
        var gsGuardian = new GameState(22, "Fast", "Warrior", "Human");
        gsGuardian.IsRunning = true;
        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gsGuardian.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gsGuardian.Hero.X = stairs.X;
            gsGuardian.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gsGuardian.CurrentFloor == floor; t++) gsGuardian.Tick();
        }
        Console.WriteLine($"Reached floor 4 stairs -> InSafeRoom={gsGuardian.IsInSafeRoom}, Floor={gsGuardian.CurrentFloor}");

        var guardianDoor = gsGuardian.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.GuardianDoor);
        gsGuardian.Hero.X = guardianDoor.X;
        gsGuardian.Hero.Y = guardianDoor.Y;
        for (int t = 0; t < 10 && gsGuardian.Boss == null; t++) gsGuardian.Tick();
        Console.WriteLine($"Approached Guardian door -> Boss={(gsGuardian.Boss != null ? $"{gsGuardian.Boss.Race} {gsGuardian.Boss.Class} L{gsGuardian.Boss.Level} HP{gsGuardian.Boss.MaxHp}" : "null (not spawned!)")}");

        if (gsGuardian.Boss != null)
        {
            Console.WriteLine($"Guardian fight is floor {gsGuardian.CurrentFloor} (expect 5 — the Guardian floor itself)");
            gsGuardian.Boss.Hp = 1; // force a quick, deterministic kill to verify the defeat hook
            for (int t = 0; t < 500 && gsGuardian.Boss != null; t++) gsGuardian.Tick();
            Console.WriteLine($"Guardian defeated -> Floor={gsGuardian.CurrentFloor} (expect 6), InSafeRoom={gsGuardian.IsInSafeRoom}, Boss={(gsGuardian.Boss == null ? "null (resolved correctly)" : "STILL SET (bug)")}");
        }

        // Continue to the second gate: floors 6-9, then the safe room must be 9.5 (not 8.5 —
        // Guardian floors are every 5th floor, with the safe room just before each).
        for (int floor = 6; floor <= 9 && !gsGuardian.IsInSafeRoom; floor++)
        {
            var stairs = gsGuardian.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gsGuardian.Hero.X = stairs.X;
            gsGuardian.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gsGuardian.CurrentFloor == floor && !gsGuardian.IsInSafeRoom; t++) gsGuardian.Tick();
        }
        Console.WriteLine($"Second gate: InSafeRoom={gsGuardian.IsInSafeRoom} at floor {gsGuardian.CurrentFloor} (expect True at 9 — i.e. safe room 9.5)");
        var door2 = gsGuardian.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.GuardianDoor);
        gsGuardian.Hero.X = door2.X;
        gsGuardian.Hero.Y = door2.Y;
        for (int t = 0; t < 10 && gsGuardian.Boss == null; t++) gsGuardian.Tick();
        Console.WriteLine($"Second Guardian: spawned={gsGuardian.Boss != null} at floor {gsGuardian.CurrentFloor} (expect 10)");

        // Message log: real play should have generated a feed (floor descents, safe rooms,
        // guardian events, kills/loot). Show the last few so the wiring is visibly working.
        var recent = gsGuardian.Messages.Messages;
        Console.WriteLine($"\nMessage log has {recent.Count} entries (expect > 0). Last few:");
        foreach (var m in recent.Skip(Math.Max(0, recent.Count - 5)))
            Console.WriteLine($"  [{m.Kind}] {m.Text}");

        // Separately verify the shrine-exit path (fresh instance so it isn't affected by the fight above).
        Console.WriteLine("\n=== Verify shrine exit preserves hero progress ===");
        var gsShrine = new GameState(33, "Fast2", "Warrior", "Human");
        gsShrine.IsRunning = true;
        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gsShrine.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gsShrine.Hero.X = stairs.X;
            gsShrine.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gsShrine.CurrentFloor == floor; t++) gsShrine.Tick();
        }
        gsShrine.Hero.GainExperience(500); // give the hero real progress so "preserved" is a meaningful check
        int levelBeforeExit = gsShrine.Hero.Level;
        int xpBeforeExit = gsShrine.Hero.Experience;
        var shrine = gsShrine.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Shrine);
        gsShrine.Hero.X = shrine.X;
        gsShrine.Hero.Y = shrine.Y;
        for (int t = 0; t < 10 && gsShrine.IsInSafeRoom; t++) gsShrine.Tick();
        Console.WriteLine($"After touching shrine -> Floor={gsShrine.CurrentFloor}, InSafeRoom={gsShrine.IsInSafeRoom}");
        Console.WriteLine($"Hero progress preserved: Level {levelBeforeExit}->{gsShrine.Hero.Level}, XP {xpBeforeExit}->{gsShrine.Hero.Experience} (should be unchanged, unlike death)");
        var codexShrine = CodexService.Instance.Data;
        Console.WriteLine($"DungeonExits recorded: {codexShrine.PlayStats.TotalDungeonExits}");

        // Verify the trap hazard (find a seed whose floor 1 rolled one, then step on it).
        Console.WriteLine("\n=== Verify trap hazard ===");
        for (int seed = 1; seed <= 30; seed++)
        {
            var gsTrap = new GameState(seed, "Trapper", "Warrior", "Human");
            gsTrap.IsRunning = true;
            var trap = gsTrap.CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.Trap);
            if (trap == null) continue;

            int hpBefore = gsTrap.Hero.CurrentHp;
            gsTrap.Hero.X = trap.X;
            gsTrap.Hero.Y = trap.Y;
            for (int t = 0; t < 10 && trap.IsUsed == false; t++) gsTrap.Tick();
            Console.WriteLine($"Seed {seed}: trap triggered={trap.IsUsed}, HP {hpBefore}->{gsTrap.Hero.CurrentHp}");
            break;
        }

        // Verify chest-opening via the new Activity system (walk to a real chest and let the
        // full multi-tick Activity run to completion — not a shortcut call to AcquireLoot).
        Console.WriteLine("\n=== Verify chest-opening Activity ===");
        var gsChest = new GameState(1, "Looter2", "Warrior", "Human");
        gsChest.IsRunning = true;
        var chest = gsChest.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Chest);
        int xpBefore = gsChest.Hero.Experience;
        int invBefore = gsChest.Hero.Inventory.Count + gsChest.Hero.Loadout.Count;
        gsChest.Hero.X = chest.X;
        gsChest.Hero.Y = chest.Y;
        int ticksToOpen = 0;
        for (int t = 0; t < 200 && !chest.IsUsed; t++) { gsChest.Tick(); ticksToOpen++; }
        Console.WriteLine($"Chest opened in {ticksToOpen} ticks (CurrentActivity null after: {gsChest.CurrentActivity == null}); IsUsed={chest.IsUsed}");
        Console.WriteLine($"XP {xpBefore}->{gsChest.Hero.Experience}, inventory+loadout count {invBefore}->{gsChest.Hero.Inventory.Count + gsChest.Hero.Loadout.Count}");
    }

    private static int StairsDistance(GameState gs)
    {
        var stairs = gs.CurrentMaze.Features.FirstOrDefault(f => f.Type == Core.Models.MazeFeatureType.Stairs);
        if (stairs == null) return -1;
        var distances = gs.CurrentMaze.BfsDistancesFrom(1, 1);
        return distances.GetValueOrDefault((stairs.X, stairs.Y), -1);
    }

    // Debug/test entrypoint: if TEST_OVERWORLD=1 is set, exercise the Overworld vertical slice and exit
    public static void RunOverworldDemo()
    {
        Console.WriteLine("=== Step 2: Overworld entry/exit wiring ===");
        var gs = new GameState(44, "Pioneer", "Warrior", "Human");
        gs.IsRunning = true;
        gs.Hero.GainExperience(300); // real progress, so "preserved on exit" is a meaningful check
        int levelBeforeExit = gs.Hero.Level;

        // Fast-forward to floor 4's safe room (same teleport technique as TEST_DUNGEON).
        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gs.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gs.Hero.X = stairs.X;
            gs.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gs.CurrentFloor == floor; t++) gs.Tick();
        }
        Console.WriteLine($"Reached safe room -> InSafeRoom={gs.IsInSafeRoom}");

        var shrine = gs.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Shrine);
        gs.Hero.X = shrine.X;
        gs.Hero.Y = shrine.Y;
        for (int t = 0; t < 10 && gs.IsInSafeRoom; t++) gs.Tick();

        var entrance = gs.CurrentMaze.Features.FirstOrDefault(f => f.Type == MazeFeatureType.DungeonEntrance);
        Console.WriteLine($"After shrine touch -> IsInOverworld={gs.IsInOverworld}, HeroPos=({gs.Hero.X},{gs.Hero.Y}), EntrancePos=({entrance?.X},{entrance?.Y})");
        Console.WriteLine($"Hero preserved: Level {levelBeforeExit}->{gs.Hero.Level} (should be unchanged)");

        Console.WriteLine("\n=== Step 3: OverworldGoal drives movement (no teleporting — real auto-play) ===");
        var mine = gs.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.MineEntrance);
        float startDist = Dist(gs.Hero.X, gs.Hero.Y, mine.X, mine.Y);
        Console.WriteLine($"Goal={gs.CurrentOverworldGoal}, start distance to mine: {startDist:0.00}");
        for (int t = 0; t < 60; t++) gs.Tick();
        float endDist = Dist(gs.Hero.X, gs.Hero.Y, mine.X, mine.Y);
        Console.WriteLine($"After 60 ticks of real auto-play: distance to mine {endDist:0.00} (should have decreased)");

        Console.WriteLine("\n=== Step 4: Mining ===");
        int oreBefore = gs.Hero.Resources.GetValueOrDefault("iron-ore", 0);
        for (int t = 0; t < 300 && gs.CurrentOverworldGoal != OverworldGoal.ToSmithy; t++) gs.Tick();
        int oreAfter = gs.Hero.Resources.GetValueOrDefault("iron-ore", 0);
        Console.WriteLine($"Goal after mining: {gs.CurrentOverworldGoal}, iron-ore {oreBefore}->{oreAfter}, CurrentActivity null: {gs.CurrentActivity == null}");

        Console.WriteLine("\n=== Step 5: Smelt + craft at the Smithy ===");
        int itemCountBefore = gs.Hero.Inventory.Count + gs.Hero.Loadout.Count;
        for (int t = 0; t < 2000 && gs.CurrentOverworldGoal != OverworldGoal.ToStall; t++) gs.Tick();
        int itemCountAfter = gs.Hero.Inventory.Count + gs.Hero.Loadout.Count;
        var sword = gs.Hero.Inventory.Concat(gs.Hero.Loadout).OfType<Weapon>().FirstOrDefault(w => w.Id == "iron-sword");
        Console.WriteLine($"Goal after crafting: {gs.CurrentOverworldGoal}, iron-ore={gs.Hero.Resources.GetValueOrDefault("iron-ore", 0)}, iron-ingot={gs.Hero.Resources.GetValueOrDefault("iron-ingot", 0)}");
        Console.WriteLine($"Item count {itemCountBefore}->{itemCountAfter}, Iron Sword crafted: {sword != null}");

        // Verify Forge-combine is reachable at the Smithy (reusing the Phase 1 combine system).
        var combined = gs.CombineAtForge(CombinableCatalog.Sword(), CombinableCatalog.Dagger());
        Console.WriteLine($"Forge-combine reachable: {combined != null} -> {combined?.Name} ({combined?.Kind})");

        Console.WriteLine("\n=== Step 6: Selling at the Stall ===");
        int goldBefore = gs.Hero.Gold;
        for (int t = 0; t < 300 && gs.CurrentOverworldGoal != OverworldGoal.ToDungeonEntrance; t++) gs.Tick();
        bool swordStillOwned = gs.Hero.Inventory.Concat(gs.Hero.Loadout).OfType<Weapon>().Any(w => w.Id == "iron-sword");
        Console.WriteLine($"Goal after selling: {gs.CurrentOverworldGoal}, Gold {goldBefore}->{gs.Hero.Gold} (expect +30 for a Common item), sword still owned: {swordStillOwned}");

        // Walk onto the dungeon entrance to start the return trip.
        gs.Hero.X = entrance!.X;
        gs.Hero.Y = entrance.Y;
        for (int t = 0; t < 10 && gs.IsInOverworld; t++) gs.Tick();
        Console.WriteLine($"After walking onto DungeonEntrance -> IsInOverworld={gs.IsInOverworld}, Floor={gs.CurrentFloor}");

        Console.WriteLine("\n=== Full loop proven, same Hero object throughout ===");
        Console.WriteLine($"{gs.Hero.Name}: Level {gs.Hero.Level}, Gold {gs.Hero.Gold}, back in the dungeon at Floor {gs.CurrentFloor}");
        Console.WriteLine("Dungeon exit -> mine ore -> smelt+craft a sword -> sell it -> return to a fresh dive: complete.");
    }

    private static float Dist(float x1, float y1, float x2, float y2)
        => MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    // Debug/test entrypoint: if TEST_SAVE=1 is set, exercise the save/load round-trip and exit
    public static void RunSaveLoadDemo()
    {
        Console.WriteLine("=== Save/Load round-trip ===");

        // Build up real progress, then trigger the shrine's EnterOverworld (which auto-saves).
        var gs1 = new GameState(55, "Saver", "Warrior", "Human");
        gs1.IsRunning = true;
        gs1.Hero.GainExperience(500);
        gs1.Hero.Gold = 42;
        gs1.Hero.Resources["iron-ore"] = 5;
        gs1.Hero.Inventory.Add(CraftedItemCatalog.Build("iron-sword")!);

        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gs1.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gs1.Hero.X = stairs.X;
            gs1.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gs1.CurrentFloor == floor; t++) gs1.Tick();
        }
        var shrine = gs1.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Shrine);
        gs1.Hero.X = shrine.X;
        gs1.Hero.Y = shrine.Y;
        for (int t = 0; t < 10 && gs1.IsInSafeRoom; t++) gs1.Tick();

        Console.WriteLine($"Before save: Level {gs1.Hero.Level}, Gold {gs1.Hero.Gold}, iron-ore {gs1.Hero.Resources.GetValueOrDefault("iron-ore", 0)}, Inventory count {gs1.Hero.Inventory.Count}");
        Console.WriteLine($"Save slot exists (written by EnterOverworld's auto-save): {SaveService.HasAnySaves()}");

        var summaries = SaveService.ListSaves();
        Console.WriteLine($"ListSaves() sees {summaries.Count} slot(s): {string.Join(", ", summaries.Select(s => $"{s.HeroName} (Lvl {s.Level}, {s.PlaytimeDisplay})"))}");

        // Construct a totally separate, fresh GameState and load the save into it — proves this
        // isn't just reading gs1's own in-memory state back.
        var loaded = SaveService.Load(gs1.SaveId);
        var gs2 = new GameState(999, loaded!.HeroName, loaded.ClassName, loaded.RaceName);
        gs2.LoadFrom(loaded);

        Console.WriteLine($"After load into a FRESH GameState: Level {gs2.Hero.Level}, Gold {gs2.Hero.Gold}, iron-ore {gs2.Hero.Resources.GetValueOrDefault("iron-ore", 0)}, Inventory count {gs2.Hero.Inventory.Count}, IsInOverworld {gs2.IsInOverworld}");

        var loadedSword = gs2.Hero.Inventory.OfType<Weapon>().FirstOrDefault(w => w.Id == "iron-sword");
        Console.WriteLine($"Polymorphic type preserved: sword found={loadedSword != null}, BaseDamage={loadedSword?.BaseDamage} (expect 7)");

        bool match = gs1.Hero.Level == gs2.Hero.Level && gs1.Hero.Gold == gs2.Hero.Gold
            && gs1.Hero.Resources.GetValueOrDefault("iron-ore", 0) == gs2.Hero.Resources.GetValueOrDefault("iron-ore", 0)
            && gs1.Hero.Inventory.Count == gs2.Hero.Inventory.Count;
        Console.WriteLine($"Round-trip exact match: {match}");

        // Exercise the exact code path the Continue button drives: MainWindowViewModel(SaveData).
        var vm = new TheMazeRPG.ViewModels.MainWindowViewModel(loaded);
        Console.WriteLine($"MainWindowViewModel(SaveData) constructed: Level {vm.GameState.Hero.Level}, IsInOverworld {vm.GameState.IsInOverworld}, IsRunning {vm.GameState.IsRunning}");
        vm.Stop();

        // Exercise the saves-picker's Delete action. Only this character's slot is asserted on —
        // other demos run in the same process (e.g. TEST_OVERWORLD's hero auto-saving on shrine
        // exit) may legitimately own other slots, so HasAnySaves isn't a valid check here.
        SaveService.Delete(gs1.SaveId);
        Console.WriteLine($"After Delete: slot removed, Load returns null={SaveService.Load(gs1.SaveId) == null} (expect True)");

        // --- Safe-room checkpoint: entering the safe room auto-saves; continuing that save
        // resumes back in the safe room, not the Overworld. ---
        Console.WriteLine("\n=== Safe-room checkpoint resume ===");
        var gs3 = new GameState(77, "Camper", "Warrior", "Human");
        gs3.IsRunning = true;
        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gs3.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gs3.Hero.X = stairs.X;
            gs3.Hero.Y = stairs.Y;
            for (int t = 0; t < 30 && gs3.CurrentFloor == floor && !gs3.IsInSafeRoom; t++) gs3.Tick();
        }
        Console.WriteLine($"In safe room: {gs3.IsInSafeRoom} (floor {gs3.CurrentFloor}); CanSave={gs3.CanSave} (expect True)");
        var safeRoomSave = SaveService.Load(gs3.SaveId);
        Console.WriteLine($"Auto-saved on safe-room entry: SafeRoomFloor={safeRoomSave?.SafeRoomFloor} (expect 4)");

        var gs4 = new GameState(888, safeRoomSave!.HeroName, safeRoomSave.ClassName, safeRoomSave.RaceName);
        gs4.LoadFrom(safeRoomSave);
        Console.WriteLine($"Resumed: IsInSafeRoom={gs4.IsInSafeRoom} (expect True), Floor={gs4.CurrentFloor} (expect 4), IsInOverworld={gs4.IsInOverworld} (expect False)");

        // --- Permadeath: dying deletes the hero's save slot. ---
        gs4.IsRunning = true;
        gs4.Hero.CurrentHp = 0;
        gs4.Tick();
        Console.WriteLine($"After death tick: save deleted={SaveService.Load(gs4.SaveId) == null} (expect True)");

        // Mid-dungeon: CanSave must be false on a regular floor.
        var gs5 = new GameState(99, "Diver", "Warrior", "Human");
        Console.WriteLine($"Fresh dungeon floor: CanSave={gs5.CanSave} (expect False)");

        // --- Creation-time save: starting a new game (the ViewModel path) immediately writes a
        // DungeonStart save so a crash doesn't force re-creating the character. ---
        Console.WriteLine("\n=== Creation-time save (new-game ViewModel path) ===");
        var newGameVm = new TheMazeRPG.ViewModels.MainWindowViewModel("Rookie", "Warrior", "Human");
        newGameVm.Stop();
        var rookieSave = SaveService.Load(newGameVm.GameState.SaveId);
        Console.WriteLine($"Save written at creation: {rookieSave != null}, ResumePoint={rookieSave?.ResumePoint} (expect DungeonStart), Level={rookieSave?.Level} (expect 1)");
        var gs6 = new GameState(111, rookieSave!.HeroName, rookieSave.ClassName, rookieSave.RaceName);
        gs6.LoadFrom(rookieSave);
        Console.WriteLine($"Resumed rookie: Floor={gs6.CurrentFloor} (expect 1), IsInOverworld={gs6.IsInOverworld} (expect False), IsInSafeRoom={gs6.IsInSafeRoom} (expect False)");

        // Cleanup all slots created by this demo.
        SaveService.Delete(gs3.SaveId);
        SaveService.Delete(newGameVm.GameState.SaveId);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

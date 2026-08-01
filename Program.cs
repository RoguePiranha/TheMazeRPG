using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using TheMazeRPG.Core.Models;
using TheMazeRPG.Core.Services;
using TheMazeRPG.Core.Systems;

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

        // If TEST_MAPGEN is set, validate dungeon structure, determinism, and room-aware
        // population across a broad range of seeds and floors.
        if (Environment.GetEnvironmentVariable("TEST_MAPGEN") == "1")
        {
            RunMapGenerationDemo();
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

        // If TEST_AFFINITY is set, verify the elemental affinity system and exit
        if (Environment.GetEnvironmentVariable("TEST_AFFINITY") == "1")
        {
            RunAffinityDemo();
            return;
        }

        // If TEST_INTERACT is set, verify dodge / perception / disarm / corpse-loot and exit
        if (Environment.GetEnvironmentVariable("TEST_INTERACT") == "1")
        {
            RunInteractDemo();
            return;
        }

        // If TEST_STATS is set, verify manual level-up stat allocation and exit
        if (Environment.GetEnvironmentVariable("TEST_STATS") == "1")
        {
            RunStatsDemo();
            return;
        }

        // If TEST_CONSOLE is set, verify the debug-console command executor and exit
        if (Environment.GetEnvironmentVariable("TEST_CONSOLE") == "1")
        {
            RunConsoleDemo();
            return;
        }

        // If TEST_SPRITES is set, validate the sprite manifest against the files on disk and exit
        if (Environment.GetEnvironmentVariable("TEST_SPRITES") == "1")
        {
            RunSpritesDemo();
            return;
        }

        if (Environment.GetEnvironmentVariable("TEST_MAPRENDER") == "1")
        {
            RunMapRenderDemo();
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

            // Scroll-wheel cycle wraps around the hotbar.
            hb.SelectAttack(0);
            hb.CycleAttack(-1); // wrap backwards from the first slot to the last
            Console.WriteLine($"Scroll cycle -1 from slot 0 -> '{hb.Hero.CurrentAttack?.Name}' (expect last: '{hb.Hero.Attacks[^1].Name}')");
        }

        // Magic element mapping (drives spell projectile colors).
        Console.WriteLine("Element mapping:");
        foreach (var (id, name) in new[] { ("fireball", "Fireball"), ("ice-shard", "Ice Shard"), ("magic-dart", "Mana Dart"), ("holy-touch", "Holy Touch"), ("mana-bolt", "Mana Bolt"), ("quick-slash", "Quick Slash") })
        {
            var el = MagicElements.For(new Attack { Id = id, Name = name });
            Console.WriteLine($"  {name,-12} -> {el}");
        }

        // Dodge: a very agile hero evades a meaningful share of incoming hits (vs a low-accuracy
        // attacker) — so Agility/Dexterity now matter. Debug race gives enough HP to survive the run.
        var dodgeGs = new GameState(202, "Nimble", "Warrior", "Debug");
        dodgeGs.IsRunning = true;
        dodgeGs.Hero.Agility = 40; // extremely evasive
        var attacker = dodgeGs.Enemies.FirstOrDefault();
        if (attacker != null)
        {
            attacker.X = dodgeGs.Hero.X + 1;
            attacker.Y = dodgeGs.Hero.Y;
            attacker.MaxHp = attacker.Hp = 1_000_000; // won't die → keeps attacking
            attacker.Dexterity = 2;   // low accuracy
            attacker.AttackSpeed = 12; // attacks often, for a good sample
            for (int t = 0; t < 800; t++) dodgeGs.Tick();
            int dodges = dodgeGs.Messages.Messages.Count(m => m.Text.Contains("dodge", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"High-agility hero, 800 ticks vs a low-accuracy attacker: {dodges} dodge event(s) in the recent log (expect > 0)");
        }
    }

    // Debug/test entrypoint: verify active dodge, trap perception, disarm, and corpse looting.
    public static void RunInteractDemo()
    {
        Console.WriteLine("=== Interaction: dodge / perception / disarm / loot ===");

        // (a) Perception: spot chance rises with Wisdom and falls off with distance.
        float hiWis = PerceptionService.SpotChancePerTick(30f, 1.0f, 3);
        float loWis = PerceptionService.SpotChancePerTick(3f, 1.0f, 3);
        float faR = PerceptionService.SpotChancePerTick(30f, PerceptionService.PerceptionRadius + 1f, 3);
        Console.WriteLine($"Spot/tick: Wis30@1tile={hiWis:0.000}, Wis3@1tile={loWis:0.000}, Wis30@far={faR:0.000} (expect hi>lo>0, far=0)");

        // (b) Examine reveals a hidden trap.
        var gs = new GameState(1, "Scout", "Warrior", "Human");
        var trap = new MazeFeature { X = 5, Y = 5, Type = MazeFeatureType.Trap, Hidden = true };
        gs.CurrentMaze.Features.Add(trap);
        gs.ExamineFeature(trap);
        Console.WriteLine($"Examine → trap.Perceived={trap.Perceived} (expect True)");

        // (c) Disarm chance rises with Dexterity; run high-Dex attempts and count outcomes.
        Console.WriteLine($"Disarm chance: Dex20={PerceptionService.DisarmChance(20f, 1):0.00}, Dex2={PerceptionService.DisarmChance(2f, 1):0.00} (expect higher for Dex20)");
        int disarmed = 0, sprung = 0;
        for (int i = 0; i < 200; i++)
        {
            var g = new GameState(100 + i, "Rogue", "Rogue", "Human");
            g.Hero.Dexterity = 18;
            var t = new MazeFeature { X = 5, Y = 5, Type = MazeFeatureType.Trap, Hidden = true, Perceived = true };
            g.CurrentMaze.Features.Add(t);
            int hpBefore = g.Hero.CurrentHp;
            g.TryDisarm(t);
            if (t.IsUsed && g.Hero.CurrentHp == hpBefore) disarmed++;
            else if (t.IsUsed) sprung++;
        }
        Console.WriteLine($"High-Dex disarm x200: {disarmed} clean, {sprung} sprung on failure (expect mostly clean, some sprung)");

        // (d) Dash i-frames: an enemy shot during a dash deals no damage.
        var dodge = new GameState(7, "Nimble", "Warrior", "Human");
        dodge.IsRunning = true;
        dodge.SetControlMode(ControlMode.Manual);
        dodge.SetManualMoveIntent(1, 0); // moving right → dash has a direction
        dodge.TryDash();
        dodge.Projectiles.Add(new Projectile { Team = ProjectileTeam.Enemy, CurrentX = dodge.Hero.X, CurrentY = dodge.Hero.Y, TargetX = dodge.Hero.X, TargetY = dodge.Hero.Y, Damage = 50, Radius = 0.4f, MaxLifeTime = 30 });
        int hp0 = dodge.Hero.CurrentHp;
        dodge.Tick();
        Console.WriteLine($"Dash i-frames: invulnerable={dodge.IsHeroInvulnerable}, enemy shot during dash → HP {hp0}->{dodge.Hero.CurrentHp} (expect unchanged)");

        // (e) Corpse loot: an item on a body transfers to the hero via LootItem / LootAll.
        var loot = new GameState(9, "Looter", "Warrior", "Human");
        var corpse = loot.Enemies.First();
        corpse.Hp = 0; // make it a corpse
        var drop = CraftedItemCatalog.Build("iron-sword")!;
        corpse.Inventory.Add(drop);
        corpse.Gold = 12;
        int invBefore = loot.Hero.Inventory.Count + loot.Hero.Loadout.Count;
        loot.LootItem(corpse, drop);
        Console.WriteLine($"LootItem: body now has {corpse.Inventory.Count} item(s); hero gear {invBefore}->{loot.Hero.Inventory.Count + loot.Hero.Loadout.Count} (expect body 0, hero +1)");
        int goldBefore = loot.Hero.Gold;
        loot.LootAll(corpse);
        Console.WriteLine($"LootAll gold: hero gold {goldBefore}->{loot.Hero.Gold}, body gold {corpse.Gold} (expect +12, body 0)");
    }

    // Debug/test entrypoint: verify manual level-up stat allocation (no class auto-growth; points
    // bank up and are spent by hand; Constitution/Intelligence bumps flow to derived caps; the
    // banked pool persists through a load).
    public static void RunStatsDemo()
    {
        Console.WriteLine("=== Manual stat allocation ===");

        var gs = new GameState(1, "Cadet", "Warrior", "Human");
        var h = gs.Hero;
        Console.WriteLine($"Level 1 unspent points: {h.UnspentStatPoints} (expect 0)");

        // Capture starting core stats to prove level-ups no longer auto-allocate them.
        int str0 = h.Strength, con0 = h.Constitution, dex0 = h.Dexterity, agi0 = h.Agility,
            int0 = h.Intelligence, wis0 = h.Wisdom, cha0 = h.Charisma;

        h.GainExperience(2000); // enough for several levels
        int gained = h.Level - 1;
        Console.WriteLine($"After 2000 XP: Level {h.Level}, unspent {h.UnspentStatPoints} (expect {gained * Hero.StatPointsPerLevel})");
        bool autoAllocated = h.Strength != str0 || h.Constitution != con0 || h.Dexterity != dex0 ||
                             h.Agility != agi0 || h.Intelligence != int0 || h.Wisdom != wis0 || h.Charisma != cha0;
        Console.WriteLine($"Core stats auto-changed on level-up: {autoAllocated} (expect False — allocation is manual)");

        // Spend a Constitution point: stat + points update, and MaxHp/MaxStamina rise immediately.
        int hpBefore = h.MaxHp, stamBefore = h.MaxStamina, ptsBefore = h.UnspentStatPoints, conBefore = h.Constitution;
        bool okCon = gs.SpendStatPoint("Constitution");
        Console.WriteLine($"Spend Constitution: ok={okCon}, Con {conBefore}->{h.Constitution}, points {ptsBefore}->{h.UnspentStatPoints}, MaxHp {hpBefore}->{h.MaxHp}, MaxStamina {stamBefore}->{h.MaxStamina} (expect +1 Con, -1 pt, HP & Stamina up)");

        // Spend an Intelligence point: MaxMana rises.
        int manaBefore = h.MaxMana;
        gs.SpendStatPoint("Intelligence");
        Console.WriteLine($"Spend Intelligence: MaxMana {manaBefore}->{h.MaxMana} (expect up)");

        // Unknown stat is rejected without consuming a point.
        int ptsX = h.UnspentStatPoints;
        bool okBogus = gs.SpendStatPoint("Luck");
        Console.WriteLine($"Spend unknown stat: ok={okBogus}, points {ptsX}->{h.UnspentStatPoints} (expect False, unchanged)");

        // Drain the pool; spending past zero returns false.
        while (h.UnspentStatPoints > 0) gs.SpendStatPoint("Agility");
        bool okEmpty = gs.SpendStatPoint("Agility");
        Console.WriteLine($"Spend with 0 points: ok={okEmpty}, points={h.UnspentStatPoints} (expect False, 0)");

        // Persistence: UnspentStatPoints round-trips through LoadFrom.
        var g2 = new GameState(2, "Cadet2", "Warrior", "Human");
        g2.LoadFrom(new SaveData
        {
            SaveId = "t", ClassName = "Warrior", RaceName = "Human",
            Level = 3, ExperienceToNext = 900, MaxHp = 150, CurrentHp = 150,
            Strength = 5, Constitution = 5, Agility = 5, Dexterity = 5,
            Intelligence = 5, Wisdom = 5, Charisma = 5,
            UnspentStatPoints = 7, ResumePoint = ResumePoint.DungeonStart
        });
        Console.WriteLine($"LoadFrom unspent points: {g2.Hero.UnspentStatPoints} (expect 7)");
    }

    // Debug/test entrypoint: validate Data/Sprites/sprites.json against the files on disk — every
    // mapped sheet exists and slices cleanly, and every class/race the game can actually produce
    // resolves through the lookup chain. Deliberately file-level (reads PNG headers directly) rather
    // than going through SpriteService, which needs an initialized Avalonia asset loader.
    public static void RunSpritesDemo()
    {
        Console.WriteLine("=== Sprite manifest ===");

        const string manifestPath = "Data/Sprites/sprites.json";
        const string spriteRoot = "Assets/Sprites/";
        if (!System.IO.File.Exists(manifestPath))
        {
            Console.WriteLine($"MISSING manifest: {manifestPath}");
            return;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath));
        var map = new Dictionary<string, string>();
        int actorCatalogFailures = 0;
        var actorSets = doc.RootElement.GetProperty("sets");
        foreach (var setProperty in actorSets.EnumerateObject())
        {
            var set = setProperty.Value;
            string sourcePack = set.GetProperty("sourcePack").GetString() ?? "";
            string placement = set.GetProperty("placement").GetString() ?? "";
            string facing = set.GetProperty("facing").GetString() ?? "";
            string anchor = set.GetProperty("anchor").GetString() ?? "";
            string animation = set.GetProperty("animation").GetString() ?? "";
            int frame = set.GetProperty("frame").GetInt32();
            if (string.IsNullOrWhiteSpace(sourcePack) || placement != "actor" ||
                facing != "screen-south" || anchor != "bottom-center" ||
                animation != "idle" || frame < 0)
            {
                Console.WriteLine($"  INVALID ACTOR SET METADATA {setProperty.Name}");
                actorCatalogFailures++;
            }

            foreach (var sprite in set.GetProperty("sprites").EnumerateObject())
            {
                string asset = sprite.Value.GetProperty("asset").GetString() ?? "";
                if (!map.TryAdd(sprite.Name, asset))
                {
                    Console.WriteLine($"  DUPLICATE ACTOR KEY {sprite.Name}");
                    actorCatalogFailures++;
                }
            }
        }
        Console.WriteLine($"Actor sets: {actorSets.EnumerateObject().Count()}; mappings: {map.Count}; " +
                          $"catalog failures: {actorCatalogFailures} (expect 0)");

        // (a) Every mapped path exists and is a horizontal strip of square frames.
        int missing = 0, unsliceable = 0;
        foreach (var (key, rel) in map)
        {
            string full = spriteRoot + rel;
            if (!System.IO.File.Exists(full)) { Console.WriteLine($"  MISSING FILE  {key} -> {rel}"); missing++; continue; }
            var (w, h) = PngSize(full);
            if (h <= 0 || w % h != 0) { Console.WriteLine($"  NOT SLICEABLE {key} -> {rel} ({w}x{h})"); unsliceable++; }
        }
        Console.WriteLine($"Missing files: {missing} (expect 0); non-sliceable sheets: {unsliceable} (expect 0)");

        // Terrain atlases use explicit source rectangles rather than animation-strip slicing.
        const string terrainManifestPath = "Data/Sprites/terrain.json";
        int terrainFailures = 0;
        if (!System.IO.File.Exists(terrainManifestPath))
        {
            Console.WriteLine($"MISSING terrain manifest: {terrainManifestPath}");
            terrainFailures++;
        }
        else
        {
            using var terrainDoc = System.Text.Json.JsonDocument.Parse(
                System.IO.File.ReadAllText(terrainManifestPath));
            var terrainMap = terrainDoc.RootElement.GetProperty("sets");
            string[] requiredSprites =
            {
                "floor.room", "floor.corridor", "doorway.east-west",
                "doorway.north-south", "wall.fill"
            };
            foreach (var theme in Enum.GetValues<DungeonTheme>())
            {
                if (!terrainMap.TryGetProperty(theme.ToString(), out var definition))
                {
                    Console.WriteLine($"  MISSING TERRAIN {theme}");
                    terrainFailures++;
                    continue;
                }

                string relativePath = definition.GetProperty("atlas").GetString() ?? "";
                string sourcePack = definition.GetProperty("sourcePack").GetString() ?? "";
                string fullPath = spriteRoot + relativePath;
                int tileSize = definition.GetProperty("gridSize").GetInt32();
                if (!System.IO.File.Exists(fullPath))
                {
                    Console.WriteLine($"  MISSING TERRAIN FILE {theme} -> {relativePath}");
                    terrainFailures++;
                    continue;
                }

                var (atlasWidth, atlasHeight) = PngSize(fullPath);
                var sprites = definition.GetProperty("sprites");
                foreach (string spriteId in requiredSprites)
                {
                    if (!sprites.TryGetProperty(spriteId, out var sprite))
                    {
                        Console.WriteLine($"  MISSING TERRAIN SPRITE {theme}:{spriteId}");
                        terrainFailures++;
                        continue;
                    }

                    int sourceX = sprite.GetProperty("sourceX").GetInt32();
                    int sourceY = sprite.GetProperty("sourceY").GetInt32();
                    int columns = sprite.GetProperty("columns").GetInt32();
                    int rows = sprite.GetProperty("rows").GetInt32();
                    string placement = sprite.GetProperty("placement").GetString() ?? "";
                    string facing = sprite.GetProperty("facing").GetString() ?? "";
                    string layer = sprite.GetProperty("layer").GetString() ?? "";
                    bool walkable = sprite.GetProperty("walkable").GetBoolean();
                    string expectedPlacement = spriteId switch
                    {
                        "floor.room" => "room-floor",
                        "floor.corridor" => "corridor-floor",
                        "wall.fill" => "wall",
                        _ => "doorway"
                    };
                    string expectedFacing = spriteId switch
                    {
                        "doorway.east-west" => "east-west",
                        "doorway.north-south" => "north-south",
                        _ => "none"
                    };
                    bool expectedWalkable = spriteId != "wall.fill";
                    string expectedLayer = expectedWalkable ? "ground" : "structure";
                    if (string.IsNullOrWhiteSpace(sourcePack) || tileSize <= 0 || columns <= 0 ||
                        rows <= 0 || sourceX < 0 || sourceY < 0 ||
                        sourceX + tileSize * columns > atlasWidth ||
                        sourceY + tileSize * rows > atlasHeight ||
                        placement != expectedPlacement || facing != expectedFacing ||
                        layer != expectedLayer || walkable != expectedWalkable)
                    {
                        Console.WriteLine($"  INVALID TERRAIN SPRITE {theme}:{spriteId} -> " +
                                          $"({sourceX},{sourceY},{tileSize},{columns}x{rows}) " +
                                          $"{placement}/{facing}/{layer}/walkable={walkable}");
                        terrainFailures++;
                    }
                }
            }
        }
        Console.WriteLine($"Terrain mapping failures: {terrainFailures} (expect 0)");

        // (b) Every hero class resolves (hero:{Class}).
        var cds = new CharacterDataService();
        var unmappedHeroes = cds.Classes.Keys.Where(c => !map.ContainsKey($"hero:{c}")).ToList();
        Console.WriteLine($"Hero classes mapped: {cds.Classes.Count - unmappedHeroes.Count}/{cds.Classes.Count}"
            + (unmappedHeroes.Count > 0 ? $" — unmapped (will draw circles): {string.Join(", ", unmappedHeroes)}" : ""));

        // (c) Every spawnable race x class resolves through race+class -> race -> class.
        var spawnRaces = cds.Races.Where(kv => !kv.Value.Debug).Select(kv => kv.Key).ToList();
        int resolved = 0, total = 0;
        var unresolved = new List<string>();
        foreach (var race in spawnRaces)
            foreach (var cls in cds.Classes.Keys)
            {
                total++;
                if (map.ContainsKey($"enemy:{race}:{cls}") || map.ContainsKey($"enemy:{race}") || map.ContainsKey($"enemy:{cls}")) resolved++;
                else unresolved.Add($"{race} {cls}");
            }
        Console.WriteLine($"Enemy race x class resolved: {resolved}/{total} (expect all — the class-level fallback covers any race)");
        if (unresolved.Count > 0) Console.WriteLine($"  unresolved: {string.Join(", ", unresolved.Take(10))}");

        // (d) Show which sheet a few real spawns would use, proving the specificity order.
        Console.WriteLine("Resolution samples (most specific wins):");
        foreach (var (race, cls) in new[] { ("Orc", "Warrior"), ("Orc", "Priest"), ("Elf", "Mage Apprentice"), ("Dwarf", "Rogue"), ("Kobold", "Warrior") })
        {
            string key = map.ContainsKey($"enemy:{race}:{cls}") ? $"enemy:{race}:{cls}"
                       : map.ContainsKey($"enemy:{race}") ? $"enemy:{race}"
                       : map.ContainsKey($"enemy:{cls}") ? $"enemy:{cls}" : "(none)";
            Console.WriteLine($"  {race,-8} {cls,-16} -> {(key == "(none)" ? "procedural shape" : map[key])}   [{key}]");
        }

        // (e) The real runtime path: sprites ship as embedded avares:// resources, so a correct file
        // on disk still renders as a circle if the URI/bundling is wrong. Initialize Avalonia far
        // enough to use the asset loader and actually decode every sheet through SpriteService.
        Console.WriteLine("Runtime load check (embedded avares:// resources via SpriteService):");
        try
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            int loaded = 0, failed = 0;
            var normalizedFrames = new HashSet<SkiaSharp.SKBitmap>();
            foreach (var cls in cds.Classes.Keys)
            {
                var bmp = TheMazeRPG.UI.Rendering.SpriteService.ForHero(cls);
                if (bmp == null) { Console.WriteLine($"  hero:{cls} did NOT load"); failed++; }
                else if (bmp.Width != bmp.Height) { Console.WriteLine($"  hero:{cls} frame not square ({bmp.Width}x{bmp.Height})"); failed++; }
                else { loaded++; normalizedFrames.Add(bmp); }
            }
            foreach (var race in spawnRaces)
            {
                foreach (var cls in cds.Classes.Keys)
                {
                    var bmp = TheMazeRPG.UI.Rendering.SpriteService.ForEnemy(race, cls);
                    if (bmp == null) { Console.WriteLine($"  enemy {race} {cls} did NOT load"); failed++; }
                    else { loaded++; normalizedFrames.Add(bmp); }
                }
            }

            var occupancies = new List<float>();
            foreach (var frame in normalizedFrames)
            {
                var bounds = OpaqueBounds(frame);
                if (bounds == null || frame.Width != frame.Height || bounds.Value.Bottom != frame.Height)
                {
                    Console.WriteLine($"  normalized frame invalid ({frame.Width}x{frame.Height})");
                    failed++;
                    continue;
                }

                float occupancy = bounds.Value.Height / (float)frame.Height;
                occupancies.Add(occupancy);
                if (occupancy is < 0.80f or > 0.95f)
                {
                    Console.WriteLine($"  normalized frame has {occupancy:P0} visible-height occupancy");
                    failed++;
                }
            }
            Console.WriteLine($"  normalized {normalizedFrames.Count} unique actor frame(s); " +
                              $"visible-height occupancy {occupancies.Min():P0}-{occupancies.Max():P0}");

            using var terrainSurface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(64, 64));
            string[] runtimeTerrainSprites =
            {
                "floor.room", "floor.corridor", "doorway.east-west",
                "doorway.north-south", "wall.fill"
            };
            foreach (var theme in Enum.GetValues<DungeonTheme>())
            {
                foreach (string spriteId in runtimeTerrainSprites)
                {
                    if (terrainSurface == null || !TheMazeRPG.UI.Rendering.TerrainService.DrawTile(
                            terrainSurface.Canvas, theme, spriteId, 0, 0, 0, 0, 64, 255))
                    {
                        Console.WriteLine($"  terrain {theme}:{spriteId} did NOT load");
                        failed++;
                    }
                    else
                    {
                        loaded++;
                    }
                }
            }
            Console.WriteLine($"  decoded {loaded} sprite(s), {failed} failure(s) (expect 0 failures)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SKIPPED — could not init Avalonia headlessly ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>Read a PNG's dimensions straight from its IHDR chunk (width/height are big-endian
    /// ints at byte offsets 16 and 20) — avoids pulling in an image library just to sanity-check.</summary>
    private static (int w, int h) PngSize(string path)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            var head = new byte[24];
            if (fs.Read(head, 0, 24) < 24) return (0, 0);
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return (w, h);
        }
        catch { return (0, 0); }
    }

    private static SkiaSharp.SKRectI? OpaqueBounds(SkiaSharp.SKBitmap bitmap)
    {
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha <= 8) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left || bottom < top
            ? null
            : new SkiaSharp.SKRectI(left, top, right + 1, bottom + 1);
    }

    // Debug/test entrypoint: verify the debug-console command executor (GameState.ExecuteDebugCommand).
    public static void RunConsoleDemo()
    {
        Console.WriteLine("=== Debug console ===");
        var gs = new GameState(1, "Dev", "Warrior", "Human");
        var h = gs.Hero;

        // addxp / addlevel grant progression (and, per manual allocation, stat points).
        Console.WriteLine(gs.ExecuteDebugCommand("/addxp 500"));
        int lvlBefore = h.Level;
        Console.WriteLine(gs.ExecuteDebugCommand("addlevel 2"));
        Console.WriteLine($"  Level {lvlBefore}->{h.Level} (expect +2), unspent points {h.UnspentStatPoints}");

        // addgold / addpoints.
        Console.WriteLine(gs.ExecuteDebugCommand("addgold 250") + $"  (gold now {h.Gold})");
        Console.WriteLine(gs.ExecuteDebugCommand("addpoints 3") + $"  (points now {h.UnspentStatPoints})");

        // additem / addspell resolve from the catalog (case/spacing tolerant) into the inventory.
        int invBefore = h.Inventory.Count;
        Console.WriteLine(gs.ExecuteDebugCommand("/additem Sword 2"));
        Console.WriteLine(gs.ExecuteDebugCommand("addspell fireball 1"));
        Console.WriteLine($"  Inventory {invBefore}->{h.Inventory.Count} (expect +3)");
        Console.WriteLine(gs.ExecuteDebugCommand("additem NoSuchThing 1") + "  (expect 'Unknown ...')");

        // reset restores resources.
        h.CurrentHp = 1; h.CurrentMana = 0;
        Console.WriteLine(gs.ExecuteDebugCommand("reset health") + $"  (HP {h.CurrentHp}/{h.MaxHp})");
        Console.WriteLine(gs.ExecuteDebugCommand("reset mana") + $"  (Mana {h.CurrentMana}/{h.MaxMana})");

        // moveplayer jumps floors / worlds.
        Console.WriteLine(gs.ExecuteDebugCommand("moveplayer dungeon 4") + $"  (CurrentFloor {gs.CurrentFloor}, overworld {gs.IsInOverworld})");
        Console.WriteLine(gs.ExecuteDebugCommand("moveplayer overworld") + $"  (overworld {gs.IsInOverworld})");

        // A leading slash is optional; unknown commands report cleanly.
        Console.WriteLine(gs.ExecuteDebugCommand("boguscmd") + "  (expect 'Unknown command ...')");
    }

    // Debug/test entrypoint: verify the elemental affinity system.
    public static void RunAffinityDemo()
    {
        Console.WriteLine("=== Affinity ===");

        // Seeding from race + class.
        var mage = new GameState(1, "Wiz", "Mage Apprentice", "Elf");
        var seed = mage.Hero.Affinities;
        Console.WriteLine($"Mage/Elf seeded: Arcane {seed.Get(MagicElement.Arcane):0}, Fire {seed.Get(MagicElement.Fire):0}, Water {seed.Get(MagicElement.Water):0} (neutral is {Affinities.Neutral:0})");

        // (a) Caster power: higher affinity hits harder; neutral is identity.
        Console.WriteLine($"Power x: affinity 80={AffinityService.PowerMultiplier(80):0.00}, 20(neutral)={AffinityService.PowerMultiplier(20):0.00}, 0={AffinityService.PowerMultiplier(0):0.00} (expect >1, =1.00, <1)");

        // (b) Resistance: a fire-affine target takes less fire damage.
        var fireAffine = new Affinities();
        fireAffine.Set(MagicElement.Fire, 90);
        int vsNeutral = AffinityService.ApplyResistance(100, new Affinities(), MagicElement.Fire);
        int vsResist = AffinityService.ApplyResistance(100, fireAffine, MagicElement.Fire);
        Console.WriteLine($"100 fire damage: vs neutral target={vsNeutral}, vs fire-affine(90) target={vsResist} (expect reduced)");

        // (c) Mana cost: cheaper when affine, costlier when unattuned.
        var unattuned = new Affinities();
        unattuned.Set(MagicElement.Fire, 0);
        Console.WriteLine($"Mana cost of a 10-mana fire spell: affine(90)={AffinityService.EffectiveManaCost(fireAffine, MagicElement.Fire, 10)}, neutral={AffinityService.EffectiveManaCost(new Affinities(), MagicElement.Fire, 10)}, unattuned(0)={AffinityService.EffectiveManaCost(unattuned, MagicElement.Fire, 10)} (expect <10, =10, >10)");

        // (d) Grow-with-use. Casual casting (below the elementalist threshold) raises Fire but does
        // NOT lower opposed Water/Ice; only a committed Fire elementalist's practice erodes them.
        var dabbler = new Affinities();
        for (int i = 0; i < 30; i++) AffinityService.OnElementCast(dabbler, MagicElement.Fire);
        Console.WriteLine($"Dabbler, 30 fire casts (never reaches elementalist {AffinityService.ElementalistThreshold:0}): Fire {Affinities.Neutral:0}->{dabbler.Get(MagicElement.Fire):0} (up), Water {dabbler.Get(MagicElement.Water):0}, Ice {dabbler.Get(MagicElement.Ice):0} (expect still {Affinities.Neutral:0} — no drain)");

        var elementalist = new Affinities();
        elementalist.Set(MagicElement.Fire, 60); // already a Fire elementalist
        for (int i = 0; i < 10; i++) AffinityService.OnElementCast(elementalist, MagicElement.Fire);
        Console.WriteLine($"Fire elementalist(60), 10 fire casts: Fire ->{elementalist.Get(MagicElement.Fire):0} (up), Water ->{elementalist.Get(MagicElement.Water):0}, Ice ->{elementalist.Get(MagicElement.Ice):0} (opposed, now below neutral)");

        // (e) Tier gate.
        Console.WriteLine($"Learnable tier by affinity: 10->{AffinityService.LearnableTier(10)}, 50->{AffinityService.LearnableTier(50)}, 95->{AffinityService.LearnableTier(95)} (expect rising)");
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

    // Debug/test entrypoint: if TEST_LOOT=1 is set, simulate loot pickup + MANUAL equip and exit.
    // Loot now always goes to the inventory (no auto-equip); the player equips it themselves.
    public static void RunLootDemo()
    {
        var gs = new GameState(1, "Looter", "Warrior", "Human");
        Console.WriteLine($"=== Loot / equip demo (Warrior, hotbar capacity {gs.Hero.HotbarCapacity}) ===");
        Console.WriteLine($"Start attacks: [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
        for (int floor = 1; floor <= 4; floor++)
        {
            var loot = LootService.Roll(floor, new Random(floor * 7));
            gs.AcquireLoot(loot);
            Console.WriteLine($"Floor {floor}: found {loot.Name} ({loot.Rarity} {loot.Kind}) -> inventory (not auto-equipped)");
        }
        Console.WriteLine($"After pickups — equipped attacks: [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
        Console.WriteLine($"                inventory:        [{string.Join(", ", gs.Hero.Inventory.Select(c => c.Name))}]");

        // Manual equip: player moves an attack item from inventory to the hotbar.
        var toEquip = gs.Hero.Inventory.OfType<Weapon>().FirstOrDefault() ?? gs.Hero.Inventory.OfType<Spell>().FirstOrDefault() as Combinable;
        if (toEquip != null)
        {
            bool equipped = gs.EquipFromInventory(toEquip);
            Console.WriteLine($"Manually equipped '{toEquip.Name}': {equipped}; attacks now [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
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
            gsGuardian.Enemies.Clear(); // this section validates pacing, not exit-room combat
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
            // Clear enemies so combat doesn't keep CheckFeatures (and the stairs) from triggering
            // on crowded deep floors — this demo only cares about floor pacing, not the fights.
            gsGuardian.Enemies.Clear();
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
            gsShrine.Enemies.Clear(); // this section validates the safe-room exit path
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

    public static void RunMapRenderDemo()
    {
        const int panelWidth = 640;
        const int panelHeight = 420;
        const int columns = 3;
        var themes = Enum.GetValues<DungeonTheme>();

        BuildAvaloniaApp().SetupWithoutStarting();
        using var montage = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
            panelWidth * columns, panelHeight * 2));
        montage.Canvas.Clear(SkiaSharp.SKColors.Black);

        for (int i = 0; i < themes.Length; i++)
        {
            var theme = themes[i];
            var gameState = new GameState((int)theme, $"{theme} Tester", "Warrior", "Human");
            var landmark = gameState.CurrentMaze.Dungeon?.ThemeFeatures.SingleOrDefault();
            if (landmark != null)
            {
                var nearby = new[]
                {
                    (x: landmark.X - 1, y: landmark.Y),
                    (x: landmark.X + 1, y: landmark.Y),
                    (x: landmark.X, y: landmark.Y - 1),
                    (x: landmark.X, y: landmark.Y + 1)
                }.First(cell => !gameState.CurrentMaze.Walls[cell.x, cell.y]);
                gameState.Hero.X = nearby.x;
                gameState.Hero.Y = nearby.y;
            }
            using var panel = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(panelWidth, panelHeight));
            var renderer = new TheMazeRPG.UI.Rendering.MazeRenderer();
            for (int frame = 0; frame < 48; frame++)
                renderer.Render(panel.Canvas, gameState, panelWidth, panelHeight);
            using var panelImage = panel.Snapshot();
            int column = i % columns;
            int row = i / columns;
            montage.Canvas.DrawImage(panelImage,
                new SkiaSharp.SKRect(
                    column * panelWidth, row * panelHeight,
                    (column + 1) * panelWidth, (row + 1) * panelHeight));
        }

        System.IO.Directory.CreateDirectory("obj");
        const string outputPath = "obj/map-theme-preview.png";
        using var image = montage.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = System.IO.File.Open(outputPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        data.SaveTo(stream);
        Console.WriteLine($"Rendered six-theme preview: {System.IO.Path.GetFullPath(outputPath)}");
    }

    public static void RunMapGenerationDemo()
    {
        const int seedCount = 200;
        const int floorCount = 8;
        int generated = 0;
        int minimumRooms = int.MaxValue;
        int maximumRooms = 0;
        int minimumExitDistance = int.MaxValue;
        int maximumExitDistance = 0;
        int totalLoops = 0;
        int totalDecorations = 0;
        int totalEncounters = 0;
        int totalPatrols = 0;
        int eastWestDoorways = 0;
        int northSouthDoorways = 0;
        var themeCounts = Enum.GetValues<DungeonTheme>().ToDictionary(theme => theme, _ => 0);

        Console.WriteLine("=== Dungeon map generation validation ===");
        for (int seed = 1; seed <= seedCount; seed++)
        {
            DungeonTheme? previousTheme = null;
            for (int floor = 1; floor <= floorCount; floor++)
            {
                var maze = new MazeGenerator(seed).Generate(41, 31, floor);
                var errors = DungeonGenerationValidator.Validate(maze);
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Seed {seed}, floor {floor}: {string.Join("; ", errors)}");

                var repeat = new MazeGenerator(seed).Generate(41, 31, floor);
                if (!SameDungeon(maze, repeat))
                    throw new InvalidOperationException($"Seed {seed}, floor {floor} was not deterministic.");

                var layout = maze.Dungeon!;
                if (layout.Theme == previousTheme)
                    throw new InvalidOperationException($"Seed {seed}: theme repeated on consecutive floor {floor}.");
                previousTheme = layout.Theme;
                themeCounts[layout.Theme]++;
                var landmark = layout.ThemeFeatures.Single();
                if (landmark.Type != ExpectedThemeFeature(layout.Theme))
                    throw new InvalidOperationException(
                        $"Seed {seed}, floor {floor}: {layout.Theme} generated {landmark.Type}.");
                int exitDistance = maze.BfsDistancesFrom(layout.EntranceX, layout.EntranceY)
                    .GetValueOrDefault((layout.ExitX, layout.ExitY), -1);
                int loopCount = layout.Connections.Count(connection => connection.IsLoop);
                var requiredArchetypes = new[]
                {
                    DungeonRoomArchetype.EntranceHall,
                    DungeonRoomArchetype.GuardPost,
                    DungeonRoomArchetype.Barracks,
                    DungeonRoomArchetype.Lair,
                    DungeonRoomArchetype.Vault,
                    DungeonRoomArchetype.TrapGallery,
                    DungeonRoomArchetype.AbandonedCamp,
                    DungeonRoomArchetype.StoreRoom,
                    DungeonRoomArchetype.ExitChamber
                };
                if (requiredArchetypes.Any(archetype => layout.Rooms.All(room => room.Archetype != archetype)))
                    throw new InvalidOperationException($"Seed {seed}, floor {floor} lacks required room variety.");

                generated++;
                minimumRooms = Math.Min(minimumRooms, layout.Rooms.Count);
                maximumRooms = Math.Max(maximumRooms, layout.Rooms.Count);
                minimumExitDistance = Math.Min(minimumExitDistance, exitDistance);
                maximumExitDistance = Math.Max(maximumExitDistance, exitDistance);
                totalLoops += loopCount;
                totalDecorations += layout.Decorations.Count;
                for (int x = 0; x < maze.Width; x++)
                {
                    for (int y = 0; y < maze.Height; y++)
                    {
                        if (layout.Tiles[x, y] != DungeonTileType.Doorway) continue;
                        if (layout.DoorwayOrientationAt(x, y) == DungeonPassageOrientation.EastWest)
                            eastWestDoorways++;
                        else
                            northSouthDoorways++;
                    }
                }
            }
            // The compact simulation map uses the same generator contract and needs its own
            // coverage because room packing behaves differently at this size.
            var compact = new MazeGenerator(seed).Generate(21, 15, 1);
            var compactErrors = DungeonGenerationValidator.Validate(compact);
            if (compactErrors.Count > 0)
                throw new InvalidOperationException($"Compact seed {seed}: {string.Join("; ", compactErrors)}");

            var minimumSize = new MazeGenerator(seed).Generate(15, 11, 1);
            var minimumSizeErrors = DungeonGenerationValidator.Validate(minimumSize);
            if (minimumSizeErrors.Count > 0)
                throw new InvalidOperationException(
                    $"Minimum-size seed {seed}: {string.Join("; ", minimumSizeErrors)}");
        }

        // Exercise GameState's population pass separately; constructing every topology above as a
        // full game would obscure generator failures with unrelated data/service work.
        for (int seed = 1; seed <= 50; seed++)
        {
            var gameState = new GameState(seed, "Map Tester", "Warrior", "Human");
            var layout = gameState.CurrentMaze.Dungeon
                ?? throw new InvalidOperationException($"GameState seed {seed} has no dungeon metadata.");

            AssertFeatureRoomRole(gameState, MazeFeatureType.Stairs, DungeonRoomRole.Exit, seed);
            AssertFeatureRoomRole(gameState, MazeFeatureType.Chest, DungeonRoomRole.Treasure, seed);
            var trap = gameState.CurrentMaze.Features.FirstOrDefault(feature => feature.Type == MazeFeatureType.Trap);
            if (trap != null)
                AssertFeatureRoomRole(gameState, MazeFeatureType.Trap, DungeonRoomRole.Hazard, seed);

            foreach (var enemy in gameState.Enemies)
            {
                int enemyX = (int)MathF.Round(enemy.X);
                int enemyY = (int)MathF.Round(enemy.Y);
                var room = layout.RoomAt(enemyX, enemyY);
                if (room == null || room.Role is DungeonRoomRole.Entrance or DungeonRoomRole.Treasure)
                    throw new InvalidOperationException(
                        $"Seed {seed}: enemy at ({enemyX},{enemyY}) is outside an encounter room.");
                if (enemy.EncounterId < 0 || enemy.HomeRoomId < 0)
                    throw new InvalidOperationException($"Seed {seed}: enemy has no encounter assignment.");
            }

            var reservedCells = layout.Decorations.Select(item => (item.X, item.Y))
                .Concat(layout.ThemeFeatures.Select(item => (item.X, item.Y)))
                .ToHashSet();
            if (gameState.CurrentMaze.Features.Any(feature => reservedCells.Contains((feature.X, feature.Y))) ||
                gameState.Enemies.Any(enemy => reservedCells.Contains(
                    ((int)MathF.Round(enemy.X), (int)MathF.Round(enemy.Y)))))
            {
                throw new InvalidOperationException($"Seed {seed}: an actor or feature spawned on a reserved landmark tile.");
            }

            foreach (var encounter in layout.Encounters)
            {
                var members = gameState.Enemies.Where(enemy => enemy.EncounterId == encounter.Id).ToList();
                if (members.Count != encounter.MemberCount || members.Any(enemy =>
                        enemy.HomeRoomId != encounter.HomeRoomId || enemy.Race != encounter.Race ||
                        !enemy.PatrolRoomIds.SequenceEqual(encounter.PatrolRoomIds)))
                {
                    throw new InvalidOperationException($"Seed {seed}: encounter {encounter.Id} membership is inconsistent.");
                }
                if (!ExpectedThemeRaces(layout.Theme).Contains(encounter.Race))
                    throw new InvalidOperationException(
                        $"Seed {seed}: {layout.Theme} encounter used unexpected race {encounter.Race}.");
            }

            totalEncounters += layout.Encounters.Count;
            totalPatrols += layout.Encounters.Count(encounter => encounter.PatrolRoomIds.Count > 1);
        }

        if (totalPatrols == 0)
            throw new InvalidOperationException("No patrol routes were produced by the population sweep.");
        if (eastWestDoorways == 0 || northSouthDoorways == 0)
            throw new InvalidOperationException("Dungeon sweep did not produce both doorway orientations.");

        foreach (var theme in Enum.GetValues<DungeonTheme>())
        {
            var gameState = new GameState((int)theme, "Feature Tester", "Warrior", "Human")
            {
                IsRunning = true
            };
            var landmark = gameState.CurrentMaze.Dungeon!.ThemeFeatures.Single();
            if (gameState.CurrentMaze.Dungeon.Theme != theme || landmark.Type != ExpectedThemeFeature(theme))
                throw new InvalidOperationException($"Theme feature test seed did not produce {theme}.");
            gameState.Hero.X = landmark.X;
            gameState.Hero.Y = landmark.Y;
            gameState.Tick();
            if (!landmark.IsTriggered)
                throw new InvalidOperationException($"{landmark.Type} did not trigger on contact.");
        }

        Console.WriteLine($"Validated {generated} floors ({seedCount} seeds x {floorCount} floors).");
        Console.WriteLine($"Rooms: {minimumRooms}-{maximumRooms}; exit BFS distance: {minimumExitDistance}-{maximumExitDistance}; " +
                          $"average loops: {(double)totalLoops / generated:0.00}.");
        Console.WriteLine($"Average decorations: {(double)totalDecorations / generated:0.00}; " +
                          $"encounters: {totalEncounters}; patrols: {totalPatrols}.");
        Console.WriteLine($"Doorways: east-west={eastWestDoorways}, north-south={northSouthDoorways}.");
        Console.WriteLine("Themes: " + string.Join(", ", themeCounts.Select(item => $"{item.Key}={item.Value}")));
        Console.WriteLine("Validated deterministic themes, room archetypes, and grouped population for 50 GameState seeds.");
    }

    private static string[] ExpectedThemeRaces(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Castle => new[] { "Human", "Elf", "Dwarf" },
        DungeonTheme.Sewer => new[] { "Kobold", "Goblin", "Orc" },
        DungeonTheme.Cemetery => new[] { "Tiefling", "Orc", "Human" },
        DungeonTheme.Library => new[] { "Elf", "Human", "Tiefling" },
        DungeonTheme.Forge => new[] { "Dwarf", "Dragonborn", "Orc" },
        _ => new[] { "Human", "Halfling", "Goblin", "Orc" }
    };

    private static DungeonThemeFeatureType ExpectedThemeFeature(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Castle => DungeonThemeFeatureType.CastleAlarm,
        DungeonTheme.Sewer => DungeonThemeFeatureType.SewerRunoff,
        DungeonTheme.Cemetery => DungeonThemeFeatureType.RestlessGrave,
        DungeonTheme.Library => DungeonThemeFeatureType.ArcaneWard,
        DungeonTheme.Forge => DungeonThemeFeatureType.HeatVent,
        _ => DungeonThemeFeatureType.HideoutTripwire
    };

    private static void AssertFeatureRoomRole(
        GameState gameState,
        MazeFeatureType featureType,
        DungeonRoomRole expectedRole,
        int seed)
    {
        var feature = gameState.CurrentMaze.Features.Single(item => item.Type == featureType);
        var room = gameState.CurrentMaze.Dungeon!.RoomAt(feature.X, feature.Y);
        if (room?.Role != expectedRole)
        {
            throw new InvalidOperationException(
                $"Seed {seed}: {featureType} at ({feature.X},{feature.Y}) is in {room?.Role.ToString() ?? "no room"}, " +
                $"expected {expectedRole}.");
        }
    }

    private static bool SameDungeon(Maze first, Maze second)
    {
        if (first.Width != second.Width || first.Height != second.Height ||
            first.Dungeon == null || second.Dungeon == null ||
            first.Dungeon.Theme != second.Dungeon.Theme ||
            first.Dungeon.Rooms.Count != second.Dungeon.Rooms.Count ||
            first.Dungeon.Connections.Count != second.Dungeon.Connections.Count ||
            first.Dungeon.Decorations.Count != second.Dungeon.Decorations.Count ||
            first.Dungeon.ThemeFeatures.Count != second.Dungeon.ThemeFeatures.Count)
        {
            return false;
        }

        for (int x = 0; x < first.Width; x++)
        {
            for (int y = 0; y < first.Height; y++)
            {
                if (first.Walls[x, y] != second.Walls[x, y] ||
                    first.Dungeon.Tiles[x, y] != second.Dungeon.Tiles[x, y] ||
                    first.Dungeon.RegionIds[x, y] != second.Dungeon.RegionIds[x, y])
                {
                    return false;
                }
            }
        }

        for (int i = 0; i < first.Dungeon.Rooms.Count; i++)
        {
            var left = first.Dungeon.Rooms[i];
            var right = second.Dungeon.Rooms[i];
            if (left.X != right.X || left.Y != right.Y || left.Width != right.Width ||
                left.Height != right.Height || left.Role != right.Role || left.Archetype != right.Archetype)
            {
                return false;
            }
        }

        for (int i = 0; i < first.Dungeon.Connections.Count; i++)
        {
            var left = first.Dungeon.Connections[i];
            var right = second.Dungeon.Connections[i];
            if (left.FromRoomId != right.FromRoomId || left.ToRoomId != right.ToRoomId ||
                left.IsLoop != right.IsLoop)
            {
                return false;
            }
        }

        for (int i = 0; i < first.Dungeon.Decorations.Count; i++)
        {
            var left = first.Dungeon.Decorations[i];
            var right = second.Dungeon.Decorations[i];
            if (left.X != right.X || left.Y != right.Y || left.RoomId != right.RoomId ||
                left.Type != right.Type || left.Variant != right.Variant)
            {
                return false;
            }
        }

        for (int i = 0; i < first.Dungeon.ThemeFeatures.Count; i++)
        {
            var left = first.Dungeon.ThemeFeatures[i];
            var right = second.Dungeon.ThemeFeatures[i];
            if (left.X != right.X || left.Y != right.Y || left.RoomId != right.RoomId ||
                left.Type != right.Type)
            {
                return false;
            }
        }

        return true;
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
            gs.Enemies.Clear(); // this section validates Overworld wiring, not exit-room combat
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

        Console.WriteLine("\n=== Step 3: Town is player-driven (Manual control, no auto-walk script) ===");
        Console.WriteLine($"ControlMode after entering town: {gs.ControlMode} (expect Manual)");

        Console.WriteLine("\n=== Step 4: Mining (player action) ===");
        int oreBefore = gs.Hero.Resources.GetValueOrDefault("iron-ore", 0);
        gs.MineOre();
        for (int t = 0; t < 300 && gs.CurrentActivity != null; t++) gs.Tick();
        int oreAfter = gs.Hero.Resources.GetValueOrDefault("iron-ore", 0);
        Console.WriteLine($"iron-ore {oreBefore}->{oreAfter} (expect 3), CurrentActivity null: {gs.CurrentActivity == null}");

        Console.WriteLine("\n=== Step 5: Smelt + craft at the Smithy (player actions) ===");
        var smelt = RecipeDataService.Instance.Get("smelt-iron")!;
        var craftSword = RecipeDataService.Instance.Get("craft-iron-sword")!;
        Console.WriteLine($"CanCraft smelt-iron: {gs.CanCraft(smelt)} (expect True with 3 ore)");
        gs.Craft(smelt);
        for (int t = 0; t < 2000 && gs.CurrentActivity != null; t++) gs.Tick();
        Console.WriteLine($"After smelt: iron-ore={gs.Hero.Resources.GetValueOrDefault("iron-ore", 0)} (expect 1), iron-ingot={gs.Hero.Resources.GetValueOrDefault("iron-ingot", 0)} (expect 1)");
        gs.Craft(craftSword);
        for (int t = 0; t < 2000 && gs.CurrentActivity != null; t++) gs.Tick();
        var sword = gs.Hero.Inventory.Concat(gs.Hero.Loadout).OfType<Weapon>().FirstOrDefault(w => w.Id == "iron-sword");
        Console.WriteLine($"After craft: Iron Sword in inventory: {sword != null}, iron-ingot={gs.Hero.Resources.GetValueOrDefault("iron-ingot", 0)} (expect 0)");

        // Forge-combine is still reachable at the Smithy (reusing the Phase 1 combine system).
        var combined = gs.CombineAtForge(CombinableCatalog.Sword(), CombinableCatalog.Dagger());
        Console.WriteLine($"Forge-combine reachable: {combined != null} -> {combined?.Name} ({combined?.Kind})");

        Console.WriteLine("\n=== Step 6: Selling at the Stall (player action) ===");
        int goldBefore = gs.Hero.Gold;
        if (sword != null) gs.SellItem(sword);
        bool swordStillOwned = gs.Hero.Inventory.Concat(gs.Hero.Loadout).OfType<Weapon>().Any(w => w.Id == "iron-sword");
        Console.WriteLine($"Gold {goldBefore}->{gs.Hero.Gold} (expect +30 for a Common item), sword still owned: {swordStillOwned}");

        Console.WriteLine("\n=== Step 7: Enter the Dungeon (player action) ===");
        gs.EnterDungeon();
        Console.WriteLine($"After EnterDungeon -> IsInOverworld={gs.IsInOverworld}, Floor={gs.CurrentFloor}");

        Console.WriteLine("\n=== Full loop proven, same Hero object throughout ===");
        Console.WriteLine($"{gs.Hero.Name}: Level {gs.Hero.Level}, Gold {gs.Hero.Gold}, back in the dungeon at Floor {gs.CurrentFloor}");
        Console.WriteLine("Dungeon exit -> mine ore -> smelt+craft a sword -> sell it -> return to a fresh dive: complete (all player-driven).");
    }

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
            gs1.Enemies.Clear(); // this section validates persistence, not exit-room combat
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
            gs3.Enemies.Clear(); // this section validates checkpoint persistence
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

using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        // Clears the pre-split Saves/Characters folder (owner ruling 2026-08-05: no save migration).
        // Runs before anything can touch a save, demos included.
        WorldService.Initialize();

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

        if (Environment.GetEnvironmentVariable("TEST_EQUIPMENT") == "1")
        {
            RunEquipmentDemo();
            return;
        }

        if (Environment.GetEnvironmentVariable("TEST_PROGRESSION") == "1")
        {
            RunProgressionDemo();
            return;
        }

        if (Environment.GetEnvironmentVariable("TEST_CREATION") == "1")
        {
            RunEquipmentFirstCreationDemo();
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

        if (Environment.GetEnvironmentVariable("TEST_TACTICAL") == "1")
        {
            RunTacticalModeDemo();
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

        // If TEST_ALIVE is set, verify the alive-world layer (world clock, floating combat text,
        // ambience/critters + RNG isolation, affinity persistence, level-unlock banking) and exit
        if (Environment.GetEnvironmentVariable("TEST_ALIVE") == "1")
        {
            RunAliveDemo();
            return;
        }

        // If TEST_HOTBAR is set, verify the positional hotbar (slot assignment/swap/clear,
        // auto-placement rules, consumable quick-slots, persistence) and exit
        if (Environment.GetEnvironmentVariable("TEST_HOTBAR") == "1")
        {
            RunHotbarDemo();
            return;
        }

        // If TEST_RARITY is set, verify intrinsic rarity (fixed per definition, weighted drops,
        // power scaling) and exit
        if (Environment.GetEnvironmentVariable("TEST_RARITY") == "1")
        {
            RunRarityDemo();
            return;
        }

        // If TEST_DAMAGE is set, verify the ruled damage pipeline (weapon × stats × proficiency ×
        // affinity × creature scale; no flat attack stats, no boss floors) and exit
        if (Environment.GetEnvironmentVariable("TEST_DAMAGE") == "1")
        {
            RunDamageDemo();
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

        // If TEST_WORLD is set, verify the world layer — creation, the world/character save split,
        // hostility profiles, legacy records on death, and start-location choice — and exit
        if (Environment.GetEnvironmentVariable("TEST_WORLD") == "1")
        {
            RunWorldDemo();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void Expect(bool condition, string what)
    {
        Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {what}");
        if (!condition) throw new InvalidOperationException($"TEST_WORLD assertion failed: {what}");
    }

    // Debug/test entrypoint: if TEST_WORLD=1 is set, verify the world layer and exit.
    // Covers: world creation + folder structure, generation determinism, the world/character save
    // split (two characters coexisting in one world), permadeath writing a legacy record while
    // leaving the world intact, hostility profiles actually reaching spawn counts, the pre-split
    // save purge, and start-location choice.
    public static void RunWorldDemo()
    {
        Console.WriteLine("=== World creation, save split, and legacy ===");

        // The demos share one process and each GameState save resolves the active world scope, so
        // start from a known-clean scope rather than whatever a previous demo left behind.
        WorldService.ResetCachesForTesting();

        // Every world this demo makes gets removed at the end — otherwise repeated runs silt the
        // save tree up with probe worlds full of dead test characters.
        var scratchWorlds = new List<string>();

        // --- 1. Creation + folder structure ---
        Console.WriteLine("\n-- Creation --");
        var options = new WorldGenOptions { Seed = 4242, Size = WorldSize.Small, Hostility = Hostility.Normal };
        var world = WorldService.Create(options, "Testholm");
        scratchWorlds.Add(world.WorldId);
        string worldDir = WorldService.WorldDirectory(world.WorldId);
        Expect(File.Exists(Path.Combine(worldDir, "world.json")), "world.json written");
        Expect(File.Exists(Path.Combine(worldDir, "delta.json")), "delta.json written");
        Expect(Directory.Exists(Path.Combine(worldDir, "Characters")), "Characters/ created");
        Expect(world.EffectiveSeed == 4242, $"effective seed recorded ({world.EffectiveSeed})");

        // --- 2. Determinism: identical options produce an identical generation payload ---
        // Identity fields (world id, name, creation timestamp) differ by design, so the comparison
        // is over the recipe and its result — which is what PR 6's generated map extends.
        Console.WriteLine("\n-- Determinism --");
        var twin = WorldService.Create(options, "Testholm");
        string PayloadOf(WorldData data) => JsonSerializer.Serialize(new { data.Options, data.EffectiveSeed });
        Expect(PayloadOf(world) == PayloadOf(twin), "same options → identical generation payload");
        Expect(world.WorldId != twin.WorldId, "but distinct world ids");
        WorldService.Delete(twin.WorldId);

        // --- 3. Two characters coexist inside one world ---
        Console.WriteLine("\n-- Two characters, one world --");
        WorldService.ActiveWorldId = world.WorldId;
        var alice = new GameState(11, "Alice", "Warrior", "Human") { IsRunning = true };
        alice.Hero.Gold = 30;
        SaveService.Save(alice);
        var bob = new GameState(12, "Bob", "Mage Apprentice", "Elf") { IsRunning = true };
        SaveService.Save(bob);

        Expect(alice.WorldId == world.WorldId && bob.WorldId == world.WorldId, "both characters carry the world id");
        var slots = SaveService.ListSaves();
        Expect(slots.Count == 2, $"world lists 2 characters ({slots.Count})");
        Expect(slots.All(s => File.Exists(Path.Combine(worldDir, "Characters", $"{s.SaveId}.json"))),
            "both character files sit inside the world folder");
        var summary = WorldService.ListWorlds().Single(w => w.WorldId == world.WorldId);
        Expect(summary.LivingCharacters == 2, $"worlds picker counts 2 living ({summary.LivingCharacters})");

        // --- 4. Permadeath: character deleted, world remembers ---
        Console.WriteLine("\n-- Permadeath writes a legacy record --");
        alice.Hero.Kills = 7;
        string aliceSaveId = alice.SaveId;
        int aliceFloor = alice.CurrentFloor;
        alice.Hero.CurrentHp = 0;
        alice.Tick(); // the death tick: records the legacy, then deletes the slot

        Expect(alice.IsHeroDead, "hero registered dead");
        Expect(SaveService.Load(aliceSaveId) == null, "character file deleted (permadeath)");
        Expect(File.Exists(Path.Combine(worldDir, "world.json")), "world survives the death");

        var delta = WorldService.LoadDelta(world.WorldId);
        Expect(delta.FallenHeroes.Count == 1, $"one fallen hero recorded ({delta.FallenHeroes.Count})");
        var fallen = delta.FallenHeroes[0];
        Console.WriteLine($"     legacy: {fallen.SummaryLine}");
        Expect(fallen.Name == "Alice", "legacy name correct");
        Expect(fallen.Race == "Human" && fallen.Class == "Warrior", "legacy race/class correct");
        Expect(fallen.Kills == 7, $"legacy kill count correct ({fallen.Kills})");
        Expect(fallen.Gold == 30, $"legacy gold correct ({fallen.Gold})");
        Expect(fallen.DeathLocation == $"the Dungeon, floor {aliceFloor}",
            $"legacy death location correct ({fallen.DeathLocation})");
        Expect(fallen.DaysLived >= 1, $"legacy days lived recorded ({fallen.DaysLived})");
        Expect(fallen.InnocentKills == 0, "innocent kills 0 until NPCs exist");

        // --- 5. Character B still loads out of the same world afterwards ---
        Console.WriteLine("\n-- The surviving character is unaffected --");
        var bobData = SaveService.Load(bob.SaveId);
        Expect(bobData != null, "surviving character still loads");
        Expect(bobData!.WorldId == world.WorldId, "and still belongs to the world");
        Expect(SaveService.ListSaves().Count == 1, "world now lists 1 character");
        Expect(WorldService.ListWorlds().Single(w => w.WorldId == world.WorldId).FallenCharacters == 1,
            "worlds picker counts 1 fallen");

        // --- 6. Cause of death from a real killing blow ---
        Console.WriteLine("\n-- Cause of death names the killer --");
        var victim = new GameState(77, "Victim", "Warrior", "Human") { IsRunning = true };
        var killer = victim.Enemies.FirstOrDefault();
        if (killer != null)
        {
            // Stand next to a real enemy with 1 HP and let its attack land, so the cause of death
            // comes from the projectile's own attribution rather than a hand-set string.
            victim.Hero.X = killer.X + 0.6f;
            victim.Hero.Y = killer.Y;
            victim.Hero.CurrentHp = 1;
            for (int t = 0; t < 400 && !victim.IsHeroDead; t++) victim.Tick();

            var victimDelta = WorldService.LoadDelta(victim.WorldId);
            var victimRecord = victimDelta.FallenHeroes.LastOrDefault(h => h.Name == "Victim");
            Expect(victimRecord != null, "victim recorded in the world delta");
            Console.WriteLine($"     cause: {victimRecord!.CauseOfDeath}");
            Expect(victimRecord.CauseOfDeath.StartsWith("slain by", StringComparison.Ordinal),
                "cause of death attributes a killer");
        }
        else Console.WriteLine("  (skipped: floor 1 spawned no enemies)");

        // --- 7. Hostility profiles actually reach the game ---
        Console.WriteLine("\n-- Hostility profiles apply --");
        var peaceful = WorldService.ResolveProfile(new WorldGenOptions { Hostility = Hostility.Peaceful });
        var hostile = WorldService.ResolveProfile(new WorldGenOptions { Hostility = Hostility.Hostile });
        Expect(peaceful.EnemyDensity < 1f && hostile.EnemyDensity > 1f, "density multipliers straddle 1.0");
        Expect(peaceful.EliteChance < hostile.EliteChance, "elite chance rises with hostility");

        int peacefulSpawns = CountSpawnsUnder(Hostility.Peaceful);
        int hostileSpawns = CountSpawnsUnder(Hostility.Hostile);
        Console.WriteLine($"     floor-5 spawns over 10 seeds — Peaceful {peacefulSpawns}, Hostile {hostileSpawns}");
        Expect(hostileSpawns > peacefulSpawns, "a Hostile world really does spawn more enemies");

        // Level bands shift too, and never fall below level 1 on shallow Peaceful floors.
        var quiet = WorldService.Create(new WorldGenOptions { Seed = 5, Hostility = Hostility.Peaceful }, "Quiet");
        scratchWorlds.Add(quiet.WorldId);
        WorldService.ActiveWorldId = quiet.WorldId;
        var (peacefulMin, _) = EnemyFactory.LevelRange(1);
        Expect(peacefulMin >= 1, $"Peaceful floor 1 still spawns level >= 1 ({peacefulMin})");

        // --- 8. Start location ---
        Console.WriteLine("\n-- Start location --");
        var townborn = new GameState(21, new CharacterCreationSelection
        {
            Name = "Townie",
            RaceName = "Human",
            StartLocation = StartLocation.Town
        });
        Expect(townborn.IsInOverworld, "a Town-origin character begins in the town");
        Expect(townborn.CurrentFloor == 0, $"and has never been down ({townborn.CurrentFloor})");

        var dungeonborn = new GameState(22, new CharacterCreationSelection
        {
            Name = "Delver",
            RaceName = "Human",
            StartLocation = StartLocation.Dungeon
        });
        Expect(!dungeonborn.IsInOverworld, "a Dungeon-origin character begins in the dungeon");
        Expect(dungeonborn.CurrentFloor == 1, $"on floor 1 ({dungeonborn.CurrentFloor})");

        // --- 9. The pre-split save folder is gone ---
        Console.WriteLine("\n-- Pre-split saves purged --");
        Expect(!Directory.Exists(GamePaths.Save("Saves", "Characters")),
            "legacy Saves/Characters/ no longer exists");

        // --- Cleanup: deleting a world takes its characters and delta with it ---
        Console.WriteLine("\n-- Cleanup --");
        foreach (string id in scratchWorlds) WorldService.Delete(id);
        Expect(scratchWorlds.All(id => !Directory.Exists(WorldService.WorldDirectory(id))),
            $"all {scratchWorlds.Count} scratch world(s) removed");

        Console.WriteLine("\nPASS: world creation, determinism, save split, permadeath legacy, " +
                          "hostility profiles, start location, and the legacy purge all verified.");
    }

    /// <summary>Total enemies generated across 10 floor-5 dives in a world of the given hostility —
    /// enough samples that the density multiplier dominates per-seed variation.</summary>
    private static int CountSpawnsUnder(Hostility hostility)
    {
        var world = WorldService.Create(new WorldGenOptions { Seed = 900, Hostility = hostility }, $"Probe-{hostility}");
        WorldService.ActiveWorldId = world.WorldId;
        int total = 0;
        for (int seed = 1; seed <= 10; seed++)
        {
            var probe = new GameState(seed, "Probe", "Warrior", "Human");
            probe.ExecuteDebugCommand("moveplayer dungeon 5");
            total += probe.Enemies.Count;
        }
        WorldService.Delete(world.WorldId);
        return total;
    }

    // Debug/test entrypoint: verify the ruled damage pipeline — weapon × stats × proficiency ×
    // affinity × creature scale, with flat attack stats and the old boss floor gone.
    public static void RunDamageDemo()
    {
        Console.WriteLine("=== Damage pipeline: weapon x stats x affinity x scale ===");

        var gs = new GameState(31, "Fencer", "Warrior", "Human");
        gs.IsRunning = true;
        gs.SetControlMode(ControlMode.Manual);
        var h = gs.Hero;

        // Fire directional shots and take the minimum StatDamage (crits are 1.5x outliers).
        int MinShot()
        {
            int min = int.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                gs.Projectiles.Clear();
                h.AttackCooldown = 0;
                gs.FireManualAttack(1, 0);
                var p = gs.Projectiles.FirstOrDefault(pr => pr.Team == ProjectileTeam.Hero);
                if (p != null && p.StatDamage > 0) min = Math.Min(min, p.StatDamage);
            }
            return min;
        }

        var atk = h.CurrentAttack!;
        float prof = WeaponProficiencyService.Evaluate(h, atk).DamageMultiplier;
        float statMult = 1f + h.EffectiveStrength * 0.15f + h.EffectiveDexterity * 0.06f;
        int expected = Math.Max(1, (int)MathF.Round((atk.Damage + h.EquippedWeaponDamage) * statMult * prof));
        int baseline = MinShot();
        Console.WriteLine($"{atk.Name}: non-crit stat damage = {baseline}, formula = {expected} (expect equal)");

        // (a) The flat Attack stat is OUT of the formula.
        h.Attack += 100;
        Console.WriteLine($"Hero.Attack +100 -> {MinShot()} (expect {baseline} — flat stat no longer feeds damage)");
        h.Attack -= 100;

        // (b) Stats amplify the weapon multiplicatively.
        h.Strength += 5;
        int stronger = MinShot();
        h.Strength -= 5;
        Console.WriteLine($"+5 Strength -> {stronger} (expect > {baseline}; each primary point = +15% weapon damage)");

        // (c) Boss floor is gone: a support-class boss hits like the support it is.
        var cds = new CharacterDataService();
        var bardBoss = EnemyFactory.Create("Bard", "Human", 10, EnemyTier.Boss, cds, new Random(3));
        var bAtk = bardBoss.CurrentAttack;
        bool magic = bAtk != null && (bAtk.Animation == AttackAnimation.Magic || bAtk.ManaCost > 0);
        float bPrimary = magic ? bardBoss.Intelligence : bardBoss.Strength;
        float bSecondary = magic ? bardBoss.Wisdom : bardBoss.Dexterity;
        var bElement = bAtk != null ? MagicElements.For(bAtk) : MagicElement.None;
        float bAffinity = AffinityService.PowerMultiplier(bardBoss.Affinities.Get(bElement));
        int bardStat = Math.Max(1, (int)MathF.Round((bAtk?.Damage ?? 3) *
            (1f + bPrimary * 0.15f + bSecondary * 0.06f) * bAffinity * bardBoss.SizeScale));
        int oldFloor = 12 + bardBoss.Level * 2;
        Console.WriteLine($"L10 Bard boss ({bAtk?.Name}, size x{bardBoss.SizeScale:0.0}): pre-mitigation {bardStat} — the old hardcoded floor would have forced >= {oldFloor} (expect the honest number, floor gone: {bardStat < oldFloor})");

        // (d) SizeScale is the large-creature lever: double the bulk, double the hit.
        var wolfSized = EnemyFactory.Create("Warrior", "Orc", 5, EnemyTier.Basic, cds, new Random(4));
        var direSized = EnemyFactory.Create("Warrior", "Orc", 5, EnemyTier.Basic, cds, new Random(4));
        direSized.SizeScale = 2.0f;
        var wAtk = wolfSized.CurrentAttack!;
        float wMult = 1f + wolfSized.Strength * 0.15f + wolfSized.Dexterity * 0.06f;
        int normalHit = Math.Max(1, (int)MathF.Round(wAtk.Damage * wMult * 1f));
        int direHit = Math.Max(1, (int)MathF.Round(wAtk.Damage * wMult * 2f));
        Console.WriteLine($"Same Orc at size x1 vs x2: {normalHit} vs {direHit} (expect ~double — the Dire/large-creature lever)");
    }

    // Debug/test entrypoint: verify intrinsic rarity — fixed per definition, weighted selection,
    // and the mechanical scaling that finally makes rarity matter.
    public static void RunRarityDemo()
    {
        Console.WriteLine("=== Intrinsic rarity ===");

        // (a) A definition's rarity is FIXED: 300 rolls never change any entry's rarity.
        var rng = new Random(17);
        bool anyMutated = false;
        var seenRarities = new Dictionary<string, Rarity>();
        for (int i = 0; i < 300; i++)
        {
            var item = LootService.Roll(3, rng);
            if (seenRarities.TryGetValue(item.Id, out var prior) && prior != item.Rarity) anyMutated = true;
            seenRarities[item.Id] = item.Rarity;
        }
        Console.WriteLine($"300 rolls, any definition's rarity varied: {anyMutated} (expect False — Iron is Iron)");
        Console.WriteLine($"  e.g. sword={seenRarities.GetValueOrDefault("sword")}, ward-ring={seenRarities.GetValueOrDefault("ward-ring")}, night-sight={seenRarities.GetValueOrDefault("night-sight")}");

        // (b) Depth shifts WHICH items drop: rare+ share rises with floor.
        float RareShare(int floor)
        {
            var r = new Random(99);
            int rare = 0, total = 2000;
            for (int i = 0; i < total; i++)
                if (LootService.Roll(floor, r).Rarity >= Rarity.Rare) rare++;
            return rare / (float)total;
        }
        float shallow = RareShare(1), deep = RareShare(12);
        Console.WriteLine($"Rare+ drop share: floor 1 = {shallow:P1}, floor 12 = {deep:P1} (expect rising)");

        // (c) Rarity scales power at projection: same spell, higher rarity, harder hit.
        var fire = CombinableCatalog.Fireball();
        var common = fire.ToAttack();
        fire.Rarity = Rarity.Epic;
        var epic = fire.ToAttack();
        Console.WriteLine($"Fireball Common: {common.Damage} dmg / cd {common.Cooldown}; Epic: {epic.Damage} dmg / cd {epic.Cooldown} (expect +24% dmg, -9% cd)");

        // (d) Armor defense scales with rarity.
        var gs = new GameState(21, "Wearer", "Warrior", "Human");
        var coat = CombinableCatalog.LeatherCoat();
        gs.Hero.Inventory.Add(coat);
        gs.EquipFromInventory(coat);
        int commonDef = gs.Hero.EquipmentDefenseBonus;
        coat.Rarity = Rarity.Legendary;
        int legendaryDef = gs.Hero.EquipmentDefenseBonus;
        Console.WriteLine($"Equipped coat defense: Common={commonDef}, Legendary={legendaryDef} (expect +60%)");

        // (e) Consumable effect scales with rarity.
        var gsHeal = new GameState(22, "Drinker", "Warrior", "Human");
        gsHeal.IsRunning = true;
        var potion = CombinableCatalog.HealthPotion();
        var epicPotion = CombinableCatalog.HealthPotion();
        epicPotion.Rarity = Rarity.Epic;
        gsHeal.Hero.Inventory.Add(potion);
        gsHeal.Hero.Inventory.Add(epicPotion);
        gsHeal.Hero.CurrentHp = 1;
        gsHeal.UseConsumable(potion);
        int afterCommon = gsHeal.Hero.CurrentHp;
        gsHeal.Hero.CurrentHp = 1;
        gsHeal.UseConsumable(epicPotion);
        int afterEpic = gsHeal.Hero.CurrentHp;
        Console.WriteLine($"Potion heal from 1 HP: Common -> {afterCommon} (expect 31), Epic -> {afterEpic} (expect 53 — round(30 x 1.75) + 1)");
    }

    // Debug/test entrypoint: verify the positional hotbar.
    public static void RunHotbarDemo()
    {
        Console.WriteLine("=== Positional hotbar ===");
        var gs = new GameState(9, "Slots", "Mage Apprentice", "Human");
        var h = gs.Hero;
        Console.WriteLine($"Capacity: {h.HotbarCapacity} (expect 6)");
        string Bar() => string.Join(" | ", Enumerable.Range(0, h.HotbarCapacity)
            .Select(i => gs.HotbarAttackAt(i)?.Name ?? "—"));
        Console.WriteLine($"Auto-assigned on creation: [{Bar()}]");

        // Slot a backpack spell directly onto a specific slot (replaces the occupant).
        var fireball = CombinableCatalog.Fireball();
        h.Inventory.Add(fireball);
        bool ok = gs.AssignSpellToHotbar(fireball, 1, out string reason);
        Console.WriteLine($"Assign Fireball to slot 2: ok={ok}{reason} -> slot 2 = {gs.HotbarAttackAt(1)?.Name} (expect Fireball)");

        // SelectAttack is slot-based.
        gs.SelectAttack(1);
        Console.WriteLine($"SelectAttack(slot 2) -> {h.CurrentAttack?.Name} (expect Fireball)");

        // Re-assigning an already-assigned action swaps the two slots.
        var slotOne = gs.HotbarAttackAt(0)!;
        gs.AssignAttackToHotbar(slotOne.Id, 1, out _);
        Console.WriteLine($"Swap 1<->2: slot 1 = {gs.HotbarAttackAt(0)?.Name} (expect Fireball), slot 2 = {gs.HotbarAttackAt(1)?.Name} (expect {slotOne.Name})");

        // Clearing a spell's slot returns it to the backpack.
        gs.ClearHotbarSlot(0);
        Console.WriteLine($"Clear slot 1: Fireball back in backpack = {h.Inventory.Contains(fireball)} (expect True), slot 1 = {gs.HotbarAttackAt(0)?.Name ?? "—"} (expect —)");

        // A deliberately cleared class action stays cleared through an Attacks rebuild.
        var classAction = gs.HotbarAttackAt(1)!;
        gs.ClearHotbarSlot(1);
        var iceShard = CombinableCatalog.IceShard();
        h.Inventory.Add(iceShard);
        gs.EquipFromInventory(iceShard);  // triggers RefreshAttacks + auto-place of the NEW spell
        bool classActionStillCleared = Enumerable.Range(0, h.HotbarCapacity)
            .All(i => gs.HotbarAttackAt(i)?.Id != classAction.Id);
        Console.WriteLine($"Cleared class action stays off the bar after a rebuild: {classActionStillCleared} (expect True); Ice Shard landed at a free slot: {Enumerable.Range(0, h.HotbarCapacity).Any(i => gs.HotbarAttackAt(i)?.Id == "ice-shard")} (expect True)");

        // Full-bar behavior: fill every free slot with distinct spells, then plain equip refuses.
        int fillerIndex = 0;
        while (Enumerable.Range(0, h.HotbarCapacity).Any(i => gs.HotbarAttackAt(i) == null) && fillerIndex < 12)
        {
            var filler = CombinableCatalog.Fireball();
            filler.Id = $"fireball-variant-{fillerIndex}";
            filler.Name = $"Fireball {++fillerIndex}";
            h.Inventory.Add(filler);
            if (!gs.EquipFromInventory(filler)) break;
        }
        var overflow = CombinableCatalog.Fireball();
        overflow.Id = "fireball-overflow";
        overflow.Name = "Overflow Fireball";
        h.Inventory.Add(overflow);
        bool refused = !gs.EquipFromInventory(overflow, out string fullReason);
        Console.WriteLine($"Equip into a full bar refused: {refused} (expect True) — \"{fullReason}\"");

        // Duplicate guard: a second copy of an already-slotted spell is refused by plain equip.
        var dupe = CombinableCatalog.IceShard();
        h.Inventory.Add(dupe);
        bool dupeRefused = !gs.EquipFromInventory(dupe, out string dupeReason);
        Console.WriteLine($"Duplicate of a slotted spell refused: {dupeRefused} (expect True) — \"{dupeReason}\"");

        // Consumable quick-slots: a potion binds to a slot, the slot key uses one copy, and the
        // binding survives while copies remain.
        var potion1 = CombinableCatalog.HealthPotion();
        var potion2 = CombinableCatalog.HealthPotion();
        h.Inventory.Add(potion1);
        h.Inventory.Add(potion2);
        gs.ClearHotbarSlot(5);
        bool potionOk = gs.AssignConsumableToHotbar(potion1, 5, out _);
        Console.WriteLine($"Assign Health Potion to slot 6: {potionOk} (expect True), count badge = {gs.HotbarConsumableCount(5)} (expect 2)");
        gs.IsRunning = true;
        h.CurrentHp = 10;
        gs.ActivateHotbarSlot(5);
        Console.WriteLine($"Activate slot 6: HP 10 -> {h.CurrentHp} (expect 40), copies left = {gs.HotbarConsumableCount(5)} (expect 1)");
        gs.ActivateHotbarSlot(5); // second copy
        gs.ActivateHotbarSlot(5); // nothing left — must no-op
        Console.WriteLine($"Drained: copies = {gs.HotbarConsumableCount(5)} (expect 0), slot resolves = {gs.HotbarConsumableAt(5) != null} (expect False), empty press was a no-op HP {h.CurrentHp}");

        // Persistence: assignments + known-ids round-trip.
        SaveService.Save(gs);
        var loaded = SaveService.Load(gs.SaveId)!;
        var gs2 = new GameState(10, loaded.HeroName, loaded.ClassName, loaded.RaceName);
        gs2.LoadFrom(loaded);
        string before = Bar();
        string after = string.Join(" | ", Enumerable.Range(0, gs2.Hero.HotbarCapacity)
            .Select(i => gs2.HotbarAttackAt(i)?.Name ?? "—"));
        Console.WriteLine($"Round-trip match: {before == after} (expect True)");
        Console.WriteLine($"  before: [{before}]");
        Console.WriteLine($"  after:  [{after}]");
        SaveService.Delete(gs.SaveId);
    }

    // Debug/test entrypoint: verify the alive-world layer.
    public static void RunAliveDemo()
    {
        Console.WriteLine("=== World clock ===");
        var clock = new WorldClock();
        Console.WriteLine($"Fresh hero clock: {clock.TimeDisplay} (expect Day 1, 08:00)");
        for (int t = 0; t < 600; t++) clock.AdvanceTick(10); // 60 real seconds at 10 tps
        Console.WriteLine($"After 60 real seconds: {clock.TimeDisplay} (expect Day 1, 08:48 — 0.8 game-min/sec)");
        clock.TotalGameMinutes = 12 * 60;
        Console.WriteLine($"Darkness at noon: {clock.Darkness:0.00} (expect 0.00)");
        clock.TotalGameMinutes = 0;
        Console.WriteLine($"Darkness at midnight: {clock.Darkness:0.00} (expect 1.00), IsNight={clock.IsNight} (expect True)");
        clock.TotalGameMinutes = 18 * 60;
        Console.WriteLine($"Darkness at 18:00 (mid-dusk): {clock.Darkness:0.00} (expect 0.50)");

        Console.WriteLine("\n=== Clock + affinity save/load round-trip ===");
        var gs1 = new GameState(321, "Chrono", "Warrior", "Human");
        gs1.Hero.Affinities.Set(MagicElement.Fire, 63f);
        gs1.Clock.TotalGameMinutes = 3 * 1440 + 14 * 60 + 20; // Day 4, 14:20
        SaveService.Save(gs1);
        var loaded = SaveService.Load(gs1.SaveId)!;
        var gs2 = new GameState(999, loaded.HeroName, loaded.ClassName, loaded.RaceName);
        gs2.LoadFrom(loaded);
        Console.WriteLine($"Fire affinity after load: {gs2.Hero.Affinities.Get(MagicElement.Fire):0} (expect 63)");
        Console.WriteLine($"Clock after load: {gs2.Clock.TimeDisplay} (expect Day 4, 14:20)");
        SaveService.Delete(gs1.SaveId);

        Console.WriteLine("\n=== Level-unlock banking (RefreshAttacks can no longer wipe unlocks) ===");
        var gsU = new GameState(5, "Climber", "Warrior", "Human");
        gsU.IsRunning = true;
        gsU.Hero.GainExperience(3000); // enough for level 5
        gsU.Tick();                    // drain PendingUnlocks
        var strike = gsU.Hero.Inventory.FirstOrDefault(c => c.Id == "power-strike");
        Console.WriteLine($"Level {gsU.Hero.Level}: power-strike in inventory={strike != null} (expect True — not auto-equipped)");
        bool equipped = strike != null && gsU.EquipFromInventory(strike);
        bool inAttacks = gsU.Hero.Attacks.Any(a => a.Id == "power-strike");
        Console.WriteLine($"Equipped: {equipped}, projects into Attacks: {inAttacks} (expect True/True)");
        SaveService.Save(gsU);
        var uLoaded = SaveService.Load(gsU.SaveId)!;
        var gsU2 = new GameState(6, uLoaded.HeroName, uLoaded.ClassName, uLoaded.RaceName);
        gsU2.LoadFrom(uLoaded);
        gsU2.IsRunning = true;
        gsU2.Tick(); // any re-drain must not duplicate
        int copies = gsU2.Hero.Loadout.Concat(gsU2.Hero.Inventory).Count(c => c.Id == "power-strike");
        bool survives = gsU2.Hero.Attacks.Any(a => a.Id == "power-strike");
        Console.WriteLine($"After save/load: still in Attacks={survives} (expect True — the old bug wiped it), copies={copies} (expect 1)");
        SaveService.Delete(gsU.SaveId);

        Console.WriteLine("\n=== Floating combat text ===");
        var gsF = new GameState(7, "Puncher", "Warrior", "Human");
        gsF.IsRunning = true;
        gsF.Projectiles.Add(new Projectile { Team = ProjectileTeam.Enemy, CurrentX = gsF.Hero.X, CurrentY = gsF.Hero.Y, TargetX = gsF.Hero.X, TargetY = gsF.Hero.Y, Damage = 9, Radius = 0.4f, MaxLifeTime = 30 });
        gsF.Tick();
        var ft = gsF.FloatingTexts.FirstOrDefault();
        Console.WriteLine($"Hero hit for 9: floating text '{ft?.Text}' kind={ft?.Kind} (expect a damage number, HeroDamage)");
        for (int t = 0; t < FloatingText.MaxAge + 2; t++) gsF.Tick();
        Console.WriteLine($"After {FloatingText.MaxAge + 2} more ticks: {gsF.FloatingTexts.Count} texts remain (expect 0 — aged out)");

        Console.WriteLine("\n=== Ambience: critters spawn, and NEVER perturb gameplay RNG ===");
        var quiet = new GameState(777, "Twin", "Warrior", "Human");                     // ambience off (default)
        var lively = new GameState(777, "Twin", "Warrior", "Human") { EnableAmbience = true };
        quiet.IsRunning = true; lively.IsRunning = true;
        for (int t = 0; t < 400; t++) { quiet.Tick(); lively.Tick(); }
        Console.WriteLine($"Critters: quiet={quiet.Critters.Count} (expect 0), lively={lively.Critters.Count} (expect 2-4)");
        bool identical = MathF.Abs(quiet.Hero.X - lively.Hero.X) < 0.0001f
                      && MathF.Abs(quiet.Hero.Y - lively.Hero.Y) < 0.0001f
                      && quiet.Enemies.Count == lively.Enemies.Count
                      && quiet.Hero.CurrentHp == lively.Hero.CurrentHp;
        Console.WriteLine($"Same seed, 400 ticks, ambience on vs off — hero/enemy state identical: {identical} (expect True: separate RNG stream)");

        Console.WriteLine("\n=== Town critters (the dog and the cat) ===");
        lively.ExecuteDebugCommand("moveplayer overworld");
        lively.Tick();
        var kinds = string.Join(", ", lively.Critters.Select(c => c.Kind).OrderBy(k => k));
        Console.WriteLine($"Town critters: [{kinds}] (expect Dog, Cat)");
        SaveService.Delete(lively.SaveId); // moveplayer overworld auto-saved; clean the slot

        Console.WriteLine("\n=== Night, darkvision, torches, Night Sight ===");
        var human = new GameState(11, "Norm", "Warrior", "Human");
        var elf = new GameState(12, "Sylv", "Warrior", "Elf");
        Console.WriteLine($"Darkvision: Human={human.Hero.HasDarkvision} (expect False), Elf={elf.Hero.HasDarkvision} (expect True)");
        Console.WriteLine($"Starter torch in pack: {human.HasTorch} (expect True)");
        Console.WriteLine($"Hero light radius: Elf={elf.HeroLightRadius:0.0} (expect {elf.VisionRange:0.0} — full range), Human w/ torch={human.HeroLightRadius:0.0} (expect 4.5)");
        var torch = human.Hero.Inventory.First(c => c.Id == "torch");
        human.Hero.Inventory.Remove(torch);
        Console.WriteLine($"Human, torch dropped: {human.HeroLightRadius:0.0} (expect 2.2 — barely arm's reach)");
        Console.WriteLine($"Night Sight before: {human.HasNightSight} (expect False)");
        human.ExecuteDebugCommand("additem night-sight");
        Console.WriteLine($"Night Sight after additem: {human.HasNightSight} (expect True — whole-screen brightening flag)");
        var lamps = TheMazeRPG.Core.Systems.OverworldGenerator.Generate().Features.Count(f => f.Type == MazeFeatureType.Lamp);
        Console.WriteLine($"Street lamps in town: {lamps} (expect 5)");
        Console.WriteLine($"Hotbar bindings loaded: {GameSettings.Current.HotbarKeys.Length} keys, labels [{string.Join(",", GameSettings.Current.HotbarKeyLabels[..4])}] (expect 9 keys, labels 1,2,3,4)");
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

    public static void RunTacticalModeDemo()
    {
        Console.WriteLine("=== Tactical turn mode ===");
        var gs = new GameState(8675309, "Tactician", "Rogue", "Human") { IsRunning = true };
        gs.SetControlMode(ControlMode.Manual);
        Enemy tacticalActor = gs.Enemies.First();
        Enemy tacticalSecondActor = gs.Enemies.Skip(1).First();
        gs.Enemies.Clear();
        gs.Hero.Agility = 100;
        gs.SetSimulationMode(SimulationMode.TurnBased);

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        Require(gs.TacticalTurn.IsPlayerTurn, "Turn mode must begin with the player phase.");
        Require(gs.TacticalTurn.MovementAllowance == GameState.TacticalMovementCap,
            "Extreme agility must be capped.");

        int frozenTick = gs.TickCount;
        gs.Tick();
        Require(gs.TickCount == frozenTick, "Normal Tick must not advance during the player phase.");

        var directions = new[] { (dx: 1, dy: 0), (dx: -1, dy: 0), (dx: 0, dy: 1), (dx: 0, dy: -1) };
        var origin = gs.CurrentMaze.GetEmptyCells().First(cell =>
            directions.Any(direction => gs.CurrentMaze.IsWalkable(cell.x + direction.dx, cell.y + direction.dy)));
        gs.Hero.X = origin.x;
        gs.Hero.Y = origin.y;
        var move = directions.First(direction => gs.CurrentMaze.IsWalkable(origin.x + direction.dx, origin.y + direction.dy));
        int movementBefore = gs.TacticalTurn.MovementRemaining;
        Require(gs.TryTacticalMove(move.dx, move.dy), "A valid tactical move should succeed.");
        Require(gs.TacticalTurn.MovementRemaining == movementBefore - 1,
            "A cardinal step must spend one movement point.");

        var destination = gs.CurrentMaze.BfsDistancesFrom(gs.Hero.GridX, gs.Hero.GridY)
            .Where(pair => pair.Value is >= 2 and <= 3)
            .OrderByDescending(pair => pair.Value)
            .First();
        IReadOnlyList<(int x, int y)> destinationPath =
            gs.GetTacticalPathTo(destination.Key.x, destination.Key.y);
        int movementBeforeDestination = gs.TacticalTurn.MovementRemaining;
        Require(destinationPath.Count == destination.Value,
            "Destination preview must return the shortest path and exact movement cost.");
        Require(gs.TryTacticalMoveTo(destination.Key.x, destination.Key.y) &&
                gs.Hero.GridX == destination.Key.x && gs.Hero.GridY == destination.Key.y &&
                gs.TacticalTurn.MovementRemaining == movementBeforeDestination - destinationPath.Count,
            "Destination movement must follow the preview and spend one point per cell.");

        var attackDirection = directions.First(direction =>
            gs.CurrentMaze.IsWalkable(gs.Hero.GridX + direction.dx, gs.Hero.GridY + direction.dy));
        Require(gs.TryTacticalAttack(attackDirection.dx, attackDirection.dy),
            "The primary attack should be available once.");
        Require(!gs.TryTacticalAttack(attackDirection.dx, attackDirection.dy),
            "A second primary action in the same turn must fail.");

        gs.Hero.CurrentHp = Math.Max(1, gs.Hero.MaxHp - 40);
        var potion = CombinableCatalog.HealthPotion();
        gs.Hero.Inventory.Add(potion);
        int hpBefore = gs.Hero.CurrentHp;
        Require(gs.TryUseTacticalBonusItem(potion), "A health potion should use the bonus action.");
        Require(gs.Hero.CurrentHp > hpBefore && !gs.Hero.Inventory.Contains(potion),
            "The potion must heal and be consumed.");
        var secondPotion = CombinableCatalog.HealthPotion();
        gs.Hero.Inventory.Add(secondPotion);
        Require(!gs.TryUseTacticalBonusItem(secondPotion),
            "A second bonus action in the same turn must fail.");

        Require(gs.EndTacticalTurn(), "The player should be able to end a partially spent turn.");
        int resolutionTicks = 0;
        while (!gs.TacticalTurn.IsPlayerTurn && resolutionTicks < 100)
        {
            Require(gs.AdvanceTacticalTurnTick(), "The world phase should accept tactical ticks.");
            resolutionTicks++;
        }

        Require(gs.TacticalTurn.IsPlayerTurn && gs.TacticalTurn.TurnNumber == 2,
            "The next player turn must begin after world resolution.");
        Require(gs.TacticalTurn.ActionAvailable && gs.TacticalTurn.BonusActionAvailable,
            "Action budgets must refresh on the next player turn.");
        Require(gs.TickCount > frozenTick, "World resolution must advance the simulation.");

        var enemyCell = directions.First(direction =>
            gs.CurrentMaze.IsWalkable(gs.Hero.GridX + direction.dx, gs.Hero.GridY + direction.dy));
        tacticalActor.X = gs.Hero.GridX + enemyCell.dx;
        tacticalActor.Y = gs.Hero.GridY + enemyCell.dy;
        tacticalActor.TargetX = tacticalActor.X;
        tacticalActor.TargetY = tacticalActor.Y;
        tacticalActor.MaxHp = tacticalActor.Hp = 100_000;
        tacticalActor.AttackRange = 1.5f;
        tacticalActor.AttackCooldown = int.MaxValue;
        tacticalActor.InCombat = true;
        gs.Enemies.Add(tacticalActor);
        gs.Hero.MaxHp = gs.Hero.CurrentHp = 100_000;
        Require(gs.GetTacticalPathTo((int)tacticalActor.X, (int)tacticalActor.Y).Count == 0,
            "A living enemy cell must never be a tactical movement destination.");
        gs.RefreshTacticalIntentPreview();
        Require(gs.TacticalTurn.EnemyIntents.Count == 1 &&
                gs.TacticalTurn.EnemyIntents[0].Kind == TacticalIntentKind.Attack &&
                ReferenceEquals(gs.TacticalTurn.EnemyIntents[0].Actor, tacticalActor),
            "An adjacent enemy must preview its attack before the phase begins.");

        int attacksBefore = gs.Messages.Messages.Count(message =>
            message.Text.Contains(" uses ", StringComparison.OrdinalIgnoreCase));
        Require(gs.EndTacticalTurn(), "An unspent player turn should still be endable.");
        int enemyPhaseTicks = 0;
        while (!gs.TacticalTurn.IsPlayerTurn && enemyPhaseTicks < 100)
        {
            Require(gs.AdvanceTacticalTurnTick(), "The enemy phase should accept tactical ticks.");
            enemyPhaseTicks++;
        }

        int attacksAfter = gs.Messages.Messages.Count(message =>
            message.Text.Contains(" uses ", StringComparison.OrdinalIgnoreCase));
        Require(gs.TacticalTurn.IsPlayerTurn && gs.TacticalTurn.TurnNumber == 3,
            "A discrete enemy phase must return control for turn three.");
        Require(gs.TacticalTurn.EnemyActionsTotal == 1,
            "Exactly one engaged enemy should have been queued.");
        Require(attacksAfter == attacksBefore + 1,
            "An engaged adjacent enemy must attack exactly once, regardless of real-time cooldown.");

        var movementLane = gs.CurrentMaze.GetEmptyCells()
            .SelectMany(cell => directions.Select(direction => (cell, direction)))
            .First(pair =>
                gs.CurrentMaze.IsWalkable(pair.cell.x + pair.direction.dx, pair.cell.y + pair.direction.dy) &&
                gs.CurrentMaze.IsWalkable(pair.cell.x + pair.direction.dx * 2, pair.cell.y + pair.direction.dy * 2));
        gs.Hero.X = movementLane.cell.x;
        gs.Hero.Y = movementLane.cell.y;
        tacticalActor.X = movementLane.cell.x + movementLane.direction.dx * 2;
        tacticalActor.Y = movementLane.cell.y + movementLane.direction.dy * 2;
        tacticalActor.TargetX = tacticalActor.X;
        tacticalActor.TargetY = tacticalActor.Y;
        tacticalActor.AttackRange = 0.25f;
        float actorStartX = tacticalActor.X;
        float actorStartY = tacticalActor.Y;
        gs.RefreshTacticalIntentPreview();
        TacticalEnemyIntent movementIntent = gs.TacticalTurn.EnemyIntents.Single();
        Require(movementIntent.Kind == TacticalIntentKind.Advance &&
                movementIntent.TargetX.HasValue && movementIntent.TargetY.HasValue &&
                Math.Abs(movementIntent.TargetX.Value - actorStartX) +
                Math.Abs(movementIntent.TargetY.Value - actorStartY) == 1f,
            "An out-of-range enemy must preview the same one-tile advance it will execute.");
        Require(gs.EndTacticalTurn(), "The movement scenario should start an enemy phase.");
        int movementPhaseTicks = 0;
        while (!gs.TacticalTurn.IsPlayerTurn && movementPhaseTicks < 100)
        {
            Require(gs.AdvanceTacticalTurnTick(), "The enemy movement phase should accept tactical ticks.");
            movementPhaseTicks++;
        }

        float actorTravel = MathF.Abs(tacticalActor.X - actorStartX) +
            MathF.Abs(tacticalActor.Y - actorStartY);
        Require(gs.TacticalTurn.IsPlayerTurn && gs.TacticalTurn.TurnNumber == 4,
            "A discrete movement phase must return control for turn four.");
        Require(MathF.Abs(actorTravel - 1f) < 0.001f,
            "An out-of-range enemy must move exactly one cardinal tile per phase.");
        tacticalActor.AttackRange = 4f;
        gs.RefreshTacticalIntentPreview();
        Require(gs.TacticalTurn.EnemyIntents.Single().Kind == TacticalIntentKind.Reposition,
            "A ranged enemy crowded by the hero must preview a retreat before considering an attack.");
        float retreatStartX = tacticalActor.X;
        float retreatStartY = tacticalActor.Y;
        float distanceBeforeRetreat = MathF.Abs(tacticalActor.X - gs.Hero.X) +
            MathF.Abs(tacticalActor.Y - gs.Hero.Y);
        Require(gs.EndTacticalTurn(), "The retreat scenario should start an enemy phase.");
        int retreatPhaseTicks = 0;
        while (!gs.TacticalTurn.IsPlayerTurn && retreatPhaseTicks < 100)
        {
            Require(gs.AdvanceTacticalTurnTick(), "The enemy retreat phase should accept tactical ticks.");
            retreatPhaseTicks++;
        }
        float retreatTravel = MathF.Abs(tacticalActor.X - retreatStartX) +
            MathF.Abs(tacticalActor.Y - retreatStartY);
        float distanceAfterRetreat = MathF.Abs(tacticalActor.X - gs.Hero.X) +
            MathF.Abs(tacticalActor.Y - gs.Hero.Y);
        Require(gs.TacticalTurn.TurnNumber == 5 && MathF.Abs(retreatTravel - 1f) < 0.001f &&
                distanceAfterRetreat > distanceBeforeRetreat,
            "The ranged retreat must execute as exactly one tile away from the hero.");

        var orderOrigin = gs.CurrentMaze.GetEmptyCells().First(cell =>
            directions.Count(direction =>
                gs.CurrentMaze.IsWalkable(cell.x + direction.dx, cell.y + direction.dy)) >= 2);
        var orderCells = directions.Where(direction =>
            gs.CurrentMaze.IsWalkable(orderOrigin.x + direction.dx, orderOrigin.y + direction.dy)).Take(2).ToArray();
        gs.Hero.X = orderOrigin.x;
        gs.Hero.Y = orderOrigin.y;
        tacticalActor.X = orderOrigin.x + orderCells[0].dx;
        tacticalActor.Y = orderOrigin.y + orderCells[0].dy;
        tacticalActor.AttackRange = 1.5f;
        tacticalActor.Agility = 2;
        tacticalSecondActor.X = orderOrigin.x + orderCells[1].dx;
        tacticalSecondActor.Y = orderOrigin.y + orderCells[1].dy;
        tacticalSecondActor.MaxHp = tacticalSecondActor.Hp = 100_000;
        tacticalSecondActor.AttackRange = 1.5f;
        tacticalSecondActor.Agility = 20;
        gs.Enemies.Add(tacticalSecondActor);
        gs.RefreshTacticalIntentPreview();
        Require(gs.TacticalTurn.EnemyIntents.Count == 2 &&
                ReferenceEquals(gs.TacticalTurn.EnemyIntents[0].Actor, tacticalSecondActor) &&
                gs.TacticalTurn.EnemyIntents.Select(intent => intent.Order).SequenceEqual(new[] { 1, 2 }),
            "Intent order must place the more agile enemy first and number the sequence.");
        gs.Enemies.Remove(tacticalSecondActor);

        gs.SetSimulationMode(SimulationMode.RealTime);
        int realtimeTick = gs.TickCount;
        gs.Tick();
        Require(gs.TickCount == realtimeTick + 1, "Real-time ticking must resume after toggling off.");

        var aimGs = new GameState(424242, "Arcanist", "Mage Apprentice", "Human") { IsRunning = true };
        Enemy[] areaTargets = aimGs.Enemies.Take(2).ToArray();
        aimGs.Enemies.Clear();
        aimGs.SetControlMode(ControlMode.Manual);
        // Select Arcane Blast by id, not by slot index — the basic-attack insertion shifted the
        // hotbar order and silently pointed the old SelectAttack(1) at Mana Dart (no AoE), which
        // is exactly what this demo's area asserts then tripped over.
        int arcaneSlot = Enumerable.Range(0, aimGs.Hero.HotbarCapacity)
            .First(i => aimGs.HotbarAttackAt(i)?.Id == "arcane-blast");
        aimGs.SelectAttack(arcaneSlot);
        aimGs.SetSimulationMode(SimulationMode.TurnBased);
        var aimLane = aimGs.CurrentMaze.GetEmptyCells()
            .SelectMany(cell => directions.Select(direction => (cell, direction)))
            .First(pair =>
                aimGs.CurrentMaze.IsWalkable(pair.cell.x + pair.direction.dx, pair.cell.y + pair.direction.dy) &&
                aimGs.CurrentMaze.IsWalkable(pair.cell.x + pair.direction.dx * 2, pair.cell.y + pair.direction.dy * 2) &&
                aimGs.CurrentMaze.IsWalkable(pair.cell.x + pair.direction.dx * 3, pair.cell.y + pair.direction.dy * 3));
        aimGs.Hero.X = aimLane.cell.x;
        aimGs.Hero.Y = aimLane.cell.y;
        var areaTargetCell = (x: aimLane.cell.x + aimLane.direction.dx * 3,
            y: aimLane.cell.y + aimLane.direction.dy * 3);
        var nearAreaCell = (x: aimLane.cell.x + aimLane.direction.dx * 2,
            y: aimLane.cell.y + aimLane.direction.dy * 2);
        areaTargets[0].X = areaTargetCell.x;
        areaTargets[0].Y = areaTargetCell.y;
        areaTargets[0].MaxHp = areaTargets[0].Hp = 100_000;
        areaTargets[1].X = nearAreaCell.x;
        areaTargets[1].Y = nearAreaCell.y;
        areaTargets[1].MaxHp = areaTargets[1].Hp = 100_000;
        aimGs.Enemies.AddRange(areaTargets);

        TacticalAttackPreview readyAim = aimGs.GetTacticalAttackPreview(areaTargetCell.x, areaTargetCell.y);
        Require(readyAim.CanCommit && readyAim.InRange && readyAim.HasLineOfSight &&
                readyAim.AreaRadius > 1f && readyAim.AffectedEnemyCount == 2 &&
                readyAim.RangeCells.Contains(areaTargetCell) && readyAim.AffectedCells.Contains(areaTargetCell),
            "Arcane targeting must preview range, clear LOS, affected cells, and both area targets.");

        var distantTarget = aimGs.CurrentMaze.GetEmptyCells().First(cell =>
            MathF.Sqrt(MathF.Pow(cell.x - aimGs.Hero.X, 2) + MathF.Pow(cell.y - aimGs.Hero.Y, 2)) >
            readyAim.Range + 2f);
        TacticalAttackPreview distantAim = aimGs.GetTacticalAttackPreview(distantTarget.x, distantTarget.y);
        Require(!distantAim.CanCommit && !distantAim.InRange && distantAim.Status == "OUT OF RANGE",
            "A target beyond authored range must preview as out of range without spending the action.");

        TacticalAttackPreview? blockedAim = null;
        foreach (var start in aimGs.CurrentMaze.GetEmptyCells())
        {
            aimGs.Hero.X = start.x;
            aimGs.Hero.Y = start.y;
            int extent = (int)MathF.Ceiling(readyAim.Range);
            for (int x = start.x - extent; x <= start.x + extent && blockedAim == null; x++)
            {
                for (int y = start.y - extent; y <= start.y + extent; y++)
                {
                    if (!aimGs.CurrentMaze.IsWalkable(x, y)) continue;
                    TacticalAttackPreview candidate = aimGs.GetTacticalAttackPreview(x, y);
                    if (candidate.InRange && !candidate.HasLineOfSight)
                    {
                        blockedAim = candidate;
                        break;
                    }
                }
            }
            if (blockedAim != null) break;
        }
        Require(blockedAim is { CanCommit: false, Status: "BLOCKED" },
            "A walkable target behind a wall must preview as LOS-blocked.");

        aimGs.Hero.X = aimLane.cell.x;
        aimGs.Hero.Y = aimLane.cell.y;
        int manaBeforeAim = aimGs.Hero.CurrentMana;
        Require(aimGs.TryTacticalAttackAt(areaTargetCell.x, areaTargetCell.y) &&
                !aimGs.TacticalTurn.ActionAvailable && aimGs.Hero.CurrentMana < manaBeforeAim,
            "A legal targeted spell must spend the action and its resource cost.");
        Projectile aimedProjectile = aimGs.Projectiles.Last();
        Require(MathF.Abs(aimedProjectile.TargetX - areaTargetCell.x) < 0.001f &&
                MathF.Abs(aimedProjectile.TargetY - areaTargetCell.y) < 0.001f &&
                MathF.Abs(aimedProjectile.CurrentX - areaTargetCell.x) < 0.001f &&
                MathF.Abs(aimedProjectile.CurrentY - areaTargetCell.y) < 0.001f,
            "The committed tactical area effect must resolve from the previewed target cell.");

        Console.WriteLine($"Movement cap={gs.CalculateTacticalMovementAllowance()}, empty={resolutionTicks}, attack={enemyPhaseTicks}, move={movementPhaseTicks}, retreat={retreatPhaseTicks} ticks.");
        Console.WriteLine("Tactical pathing, destination movement, attack targeting, enemy intents, upkeep, and mode exit passed.");
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

    // Debug/test entrypoint: verify the class-authored half-automatic/half-free attribute split,
    // derived resource updates, manual spending, and persistence.
    public static void RunStatsDemo()
    {
        Console.WriteLine("=== Class and manual stat allocation ===");

        var gs = new GameState(1, "Cadet", "Warrior", "Human");
        var h = gs.Hero;
        Console.WriteLine($"Level 1 unspent points: {h.UnspentStatPoints} (expect 0)");

        int str0 = h.Strength, con0 = h.Constitution;
        ProgressionService.Instance.GrantSharedXp(h, 100);
        ProgressionSlot classSlot = h.Progression.ClassSlots[0];
        ProgressionAdvanceResult advance = gs.AllocateClassXp(classSlot.SlotId, 100);
        Console.WriteLine($"Warrior advance: success={advance.Success}, Level {h.Level}, " +
            $"Str {str0}->{h.Strength}, Con {con0}->{h.Constitution}, free points {h.UnspentStatPoints} " +
            "(expect level 2, +1 Str, +1 Con, 2 free)");

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
            UnspentStatPoints = 7, Progression = h.Progression, ResumePoint = ResumePoint.DungeonStart
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

        // settime jumps the world clock within the current day (night testing).
        Console.WriteLine(gs.ExecuteDebugCommand("settime 22") + "  (expect 22:00, darkness 1.00)");

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

        var mage = new GameState(71, "Synthesist", "Mage Apprentice", "Human");
        Combinable first = CombinableCatalog.Fireball();
        Combinable second = CombinableCatalog.IceShard();
        mage.Hero.Inventory.Add(first);
        mage.Hero.Inventory.Add(second);
        Combinable? ownedResult = mage.CombineOwned(first, second, CombineLocation.Anywhere, out string ownedReason);
        if (ownedResult == null || mage.Hero.Inventory.Contains(first) || mage.Hero.Inventory.Contains(second) ||
            !mage.Hero.Inventory.Contains(ownedResult))
            throw new InvalidOperationException($"Owned combination transaction failed: {ownedReason}");
        Console.WriteLine($"[owned    ] consumed 2 equipped spells -> {ownedResult.Name} in inventory");

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
        Console.WriteLine($"=== Loot / equip demo (Warrior, action capacity {gs.Hero.HotbarCapacity}) ===");
        Console.WriteLine($"Start attacks: [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
        for (int floor = 1; floor <= 4; floor++)
        {
            var loot = LootService.Roll(floor, new Random(floor * 7));
            gs.AcquireLoot(loot);
            Console.WriteLine($"Floor {floor}: found {loot.Name} ({loot.Rarity} {loot.Kind}) -> inventory (not auto-equipped)");
        }
        Console.WriteLine($"After pickups — equipped attacks: [{string.Join(", ", gs.Hero.Attacks.Select(a => a.Name))}]");
        Console.WriteLine($"                inventory:        [{string.Join(", ", gs.Hero.Inventory.Select(c => c.Name))}]");

        // Manual equip: weapons use hands while spells use open action-bar positions.
        var toEquip = gs.Hero.Inventory.OfType<Spell>().FirstOrDefault() as Combinable;
        if (toEquip == null && gs.Hero.Inventory.OfType<Weapon>().FirstOrDefault() is { } foundWeapon)
        {
            if (gs.Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is { } held)
                gs.UnequipToInventory(held);
            toEquip = foundWeapon;
        }
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
                // Mirrors CombatSystem's ruled pipeline: weapon × stats × affinity × size scale
                // (owner ruling 2026-08-05 — no flat attack stat, no boss multiplier/floor).
                var atk = e.CurrentAttack;
                bool magic = atk != null && (atk.Animation == AttackAnimation.Magic || atk.ManaCost > 0);
                bool faith = atk != null && atk.FaithCost > 0;
                int weaponBase = atk?.Damage ?? 3;
                float primary = magic ? e.Intelligence : faith ? e.Wisdom : e.Strength;
                float secondary = magic ? e.Wisdom : faith ? e.Charisma : e.Dexterity;
                float statMult = 1f + primary * 0.15f + secondary * 0.06f;
                var element = atk != null ? MagicElements.For(atk) : MagicElement.None;
                float affinity = AffinityService.PowerMultiplier(e.Affinities.Get(element));
                int stat = System.Math.Max(1, (int)MathF.Round(weaponBase * statMult * affinity * e.SizeScale));
                int est = System.Math.Max(1, stat - heroDef / 2); // pre-variance (+-25%)
                int xp = (int)((10 + e.MaxHp / 4) * e.XpMultiplier);
                Console.WriteLine($"  {e.Tier,-5} L{e.Level,2} {e.Race,-10} {e.Class,-16} {atk?.Name,-14} ~{est,2} dmg  HP {e.MaxHp,3}  XP {xp,3}  x{e.SizeScale:0.0}");
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
        // Debug race: durable enough that a Mage guardian can't one-shot the pacing demo's hero
        // (shared-XP banking keeps demo heroes at level 1 — this demo tests floor pacing, not combat).
        var gsGuardian = new GameState(22, "Fast", "Warrior", "Debug");
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
        gsGuardian.UpdateNearbyInteractable();
        gsGuardian.EnterGuardianChamber();
        Console.WriteLine($"Approached Guardian door -> Boss={(gsGuardian.Boss != null ? $"{gsGuardian.Boss.Race} {gsGuardian.Boss.Class} L{gsGuardian.Boss.Level} HP{gsGuardian.Boss.MaxHp}" : "null (not spawned!)")}");

        if (gsGuardian.Boss != null)
        {
            Console.WriteLine($"Guardian fight is floor {gsGuardian.CurrentFloor} (expect 5 — the Guardian floor itself)");
            gsGuardian.Boss.Hp = 1; // force a quick, deterministic kill to verify the defeat hook
            // Safe rooms force Manual control; hand the chamber fight back to auto-play so the
            // hero actually swings (this demo verifies the defeat hook, not player input).
            gsGuardian.SetControlMode(ControlMode.Auto);
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
        gsGuardian.UpdateNearbyInteractable();
        gsGuardian.EnterGuardianChamber();
        Console.WriteLine($"Second Guardian: spawned={gsGuardian.Boss != null} at floor {gsGuardian.CurrentFloor} (expect 10)");

        // Message log: real play should have generated a feed (floor descents, safe rooms,
        // guardian events, kills/loot). Show the last few so the wiring is visibly working.
        var recent = gsGuardian.Messages.Messages;
        Console.WriteLine($"\nMessage log has {recent.Count} entries (expect > 0). Last few:");
        foreach (var m in recent.Skip(Math.Max(0, recent.Count - 5)))
            Console.WriteLine($"  [{m.Kind}] {m.Text}");

        // Separately verify the shrine-exit path (fresh instance so it isn't affected by the fight above).
        Console.WriteLine("\n=== Verify shrine exit preserves hero progress ===");
        var gsShrine = new GameState(33, "Fast2", "Warrior", "Debug");
        gsShrine.IsRunning = true;
        for (int floor = 1; floor <= 4; floor++)
        {
            var stairs = gsShrine.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Stairs);
            gsShrine.Hero.X = stairs.X;
            gsShrine.Hero.Y = stairs.Y;
            gsShrine.Enemies.Clear(); // this section validates the safe-room exit path
            for (int t = 0; t < 30 && gsShrine.CurrentFloor == floor; t++) gsShrine.Tick();
        }
        ProgressionService.Instance.GrantSharedXp(gsShrine.Hero, 500); // bank real progress for the preservation check
        int levelBeforeExit = gsShrine.Hero.Level;
        int xpBeforeExit = gsShrine.Hero.Experience;
        var shrine = gsShrine.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Shrine);
        gsShrine.Hero.X = shrine.X;
        gsShrine.Hero.Y = shrine.Y;
        gsShrine.UpdateNearbyInteractable();
        gsShrine.UseSafeRoomShrine();
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

        // Verify explicit chest interaction: proximity alone does not open it, then E-equivalent
        // actions unlock, open, and transfer its contents.
        Console.WriteLine("\n=== Verify chest interaction ===");
        var gsChest = new GameState(1, "Looter2", "Warrior", "Human");
        gsChest.IsRunning = true;
        var chest = gsChest.CurrentMaze.Features.First(f => f.Type == MazeFeatureType.Chest);
        int xpBefore = gsChest.Hero.Experience;
        int invBefore = gsChest.Hero.Inventory.Count;
        gsChest.Hero.X = chest.X;
        gsChest.Hero.Y = chest.Y;
        chest.IsLocked = false;
        chest.IsTrapped = false;
        gsChest.Tick();
        Console.WriteLine($"Proximity prompt={ReferenceEquals(gsChest.NearbyInteractable, chest)}, auto-opened={chest.IsOpened} (expect True, False)");
        bool opened = gsChest.OpenChest(chest);
        gsChest.LootAll(chest);
        Console.WriteLine($"Opened={opened}, emptied={chest.IsUsed}, no timer={gsChest.CurrentActivity == null}");
        Console.WriteLine($"XP {xpBefore}->{gsChest.Hero.Experience}, inventory count {invBefore}->{gsChest.Hero.Inventory.Count}");
    }

    private static int StairsDistance(GameState gs)
    {
        var stairs = gs.CurrentMaze.Features.FirstOrDefault(f => f.Type == Core.Models.MazeFeatureType.Stairs);
        if (stairs == null) return -1;
        var distances = gs.CurrentMaze.BfsDistancesFrom(1, 1);
        return distances.GetValueOrDefault((stairs.X, stairs.Y), -1);
    }

    public static void RunEquipmentDemo()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        Console.WriteLine("=== Equipment, migration, and chest interaction ===");
        var gs = new GameState(314, "GearTester", "Warrior", "Human");
        Require(gs.Hero.Loadout.All(item => item is Spell), "Physical weapons leaked into the action loadout.");
        Require(gs.Hero.Attacks[0] is { Id: "basic-attack", Name: "Attack", Animation: AttackAnimation.Melee },
            "Held sword did not produce the universal weapon command.");
        Require(gs.Hero.Attacks.Any(attack => attack.Id == "quick-slash"), "Class technique missing.");
        Require(gs.Hero.Equipment[EquipmentSlot.MainHand] is Weapon { Id: "sword" }, "Starting sword not held.");
        WeaponUseProfile warriorSword = WeaponProficiencyService.Evaluate(gs.Hero, gs.Hero.CurrentAttack!);
        Require(warriorSword.IsTrained && warriorSword.DamageMultiplier == 1f &&
            warriorSword.AccuracyMultiplier == 1f, "Warrior sword affinity was not applied.");

        var wanderer = new GameState(313, "Learner", "Wanderer", "Human");
        Weapon unfamiliarSword = CombinableCatalog.Sword();
        wanderer.Hero.Inventory.Add(unfamiliarSword);
        Require(wanderer.EquipFromInventory(unfamiliarSword, out _), "Wanderer could not equip a sword.");
        Require(wanderer.Hero.Attacks[0].Description.Contains("Sword"),
            "Equipping a sword did not rebuild the universal attack command.");
        WeaponUseProfile unfamiliar = WeaponProficiencyService.Evaluate(
            wanderer.Hero, wanderer.Hero.CurrentAttack!);
        Require(!unfamiliar.IsTrained &&
            unfamiliar.DamageMultiplier == WeaponProficiencyService.UntrainedDamageMultiplier &&
            unfamiliar.AccuracyMultiplier == WeaponProficiencyService.UntrainedAccuracyMultiplier,
            "Untrained sword penalties were not applied.");

        var testAttack = new Attack
        {
            Id = "training-test", Name = "Training Test", Damage = 8,
            Range = 1f, Cooldown = 20, Animation = AttackAnimation.Melee
        };
        wanderer.Hero.CurrentAttack = testAttack;
        var untrainedProjectiles = new List<Projectile>();
        new CombatSystem(901).PerformHeroDirectionalAttack(wanderer.Hero, 1f, 0f, untrainedProjectiles);
        wanderer.Hero.WeaponTraining.Add(WeaponType.Sword);
        Require(WeaponProficiencyService.Evaluate(wanderer.Hero, wanderer.Hero.CurrentAttack!).IsTrained,
            "Learned sword training did not remove the penalty.");
        var trainedProjectiles = new List<Projectile>();
        new CombatSystem(901).PerformHeroDirectionalAttack(wanderer.Hero, 1f, 0f, trainedProjectiles);
        Require(untrainedProjectiles[0].StatDamage < trainedProjectiles[0].StatDamage &&
            untrainedProjectiles[0].Accuracy < trainedProjectiles[0].Accuracy,
            "Weapon proficiency did not affect spawned attack damage and accuracy.");

        wanderer.Hero.Level = 7;
        wanderer.RestartGame();
        Require(wanderer.Hero.Name == "Learner" && wanderer.Hero.Race == "Human" &&
            wanderer.Hero.Class == "Wanderer" && wanderer.Hero.Level == 1 && wanderer.IsRunning,
            "Restart did not preserve identity while resetting the run.");

        gs.UnequipToInventory(gs.Hero.Equipment[EquipmentSlot.MainHand]);
        Weapon bow = CombinableCatalog.Bow();
        gs.Hero.Inventory.Add(bow);
        Require(gs.EquipFromInventory(bow, out _), "Bow did not equip.");
        Require(gs.IsOffHandBlocked, "Bow did not reserve the off hand.");
        var blockedHeldItem = new Item
        {
            Id = "test-held-item", Name = "Held Item", EquipSlot = EquipmentSlot.OffHand
        };
        gs.Hero.Inventory.Add(blockedHeldItem);
        Require(!gs.EquipFromInventory(blockedHeldItem, out _),
            "A held item equipped into the bow's reserved off hand.");
        Weapon blockedDagger = CombinableCatalog.Dagger();
        gs.Hero.Inventory.Add(blockedDagger);
        Require(!gs.EquipFromInventory(blockedDagger, out _), "One-handed weapon equipped beside a bow.");
        gs.UnequipToInventory(bow);
        Require(gs.EquipFromInventory(blockedDagger, out _), "First dagger did not equip.");
        Weapon secondDagger = CombinableCatalog.Dagger();
        gs.Hero.Inventory.Add(secondDagger);
        Require(gs.EquipFromInventory(secondDagger, out _), "Second dagger did not equip off-hand.");

        foreach (Combinable gear in new Combinable[]
        {
            CombinableCatalog.IronHelm(), CombinableCatalog.LeatherCoat(),
            CombinableCatalog.LeatherGloves(), CombinableCatalog.GuardLeggings(),
            CombinableCatalog.TrailBoots(), CombinableCatalog.ShieldGenerator(),
            CombinableCatalog.WardRing(), CombinableCatalog.WardRing()
        })
        {
            gs.Hero.Inventory.Add(gear);
            Require(gs.EquipFromInventory(gear, out string reason), $"Could not equip {gear.Name}: {reason}");
        }
        Require(EquipmentSlots.DisplayOrder.All(slot => gs.Hero.Equipment.ContainsKey(slot)),
            "One or more humanoid equipment slots remained empty.");

        Spell fireball = CombinableCatalog.Fireball();
        gs.Hero.Inventory.Add(fireball);
        Require(gs.EquipFromInventory(fireball, out _), "Spell did not slot on action bar.");
        Require(gs.Hero.Attacks.Any(attack => attack.Id == fireball.Id), "Slotted spell did not become an action.");
        Require(gs.Hero.EquipmentDefenseBonus > 0 && gs.Hero.EquippedWeaponDamage > 0,
            "Equipped gear did not affect derived combat bonuses.");

        var oldSave = new SaveData
        {
            Version = 1, ResumePoint = ResumePoint.DungeonStart, SaveId = "legacy-test",
            HeroName = "Legacy", ClassName = "Warrior", RaceName = "Human", Level = 1,
            MaxHp = 100, CurrentHp = 100, ExperienceToNext = 100,
            Strength = 5, Constitution = 5, Agility = 5, Dexterity = 5,
            Intelligence = 5, Wisdom = 5, Charisma = 5,
            Loadout = new List<Combinable>
            {
                new Weapon { Id = "quick-slash", Name = "Quick Slash" },
                CombinableCatalog.Bow()
            }
        };
        var migrated = new GameState(315, "Legacy", "Warrior", "Human");
        migrated.LoadFrom(oldSave);
        Require(migrated.Hero.Loadout.All(item => item is Spell), "Legacy weapon remained an action item.");
        Require(migrated.Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon { Id: "bow" },
            "Legacy physical weapon was not migrated into equipment.");

        oldSave.ClassName = "Mage Apprentice";
        oldSave.Loadout = new List<Combinable>
        {
            new Spell { Id = "magic-dart", Name = "Mana Dart" },
            new Spell { Id = "arcane-blast", Name = "Arcane Blast" },
            CombinableCatalog.Fireball()
        };
        var migratedMage = new GameState(317, "LegacyMage", "Mage Apprentice", "Human");
        migratedMage.LoadFrom(oldSave);
        Require(migratedMage.Hero.Attacks.Count(attack => attack.Id == "magic-dart") == 1 &&
            migratedMage.Hero.Attacks.Count(attack => attack.Id == "arcane-blast") == 1,
            "Legacy class spells were duplicated during migration.");
        Require(migratedMage.Hero.Loadout.Count == 1 && migratedMage.Hero.Loadout[0].Id == "fireball",
            "A legacy learned spell was not preserved as a slotted spell.");

        var chestGame = new GameState(316, "ChestTester", "Rogue", "Human") { IsRunning = true };
        MazeFeature chest = chestGame.CurrentMaze.Features.First(feature => feature.Type == MazeFeatureType.Chest);
        chestGame.Hero.X = chest.X;
        chestGame.Hero.Y = chest.Y;
        chest.IsLocked = true;
        chest.IsTrapped = false;
        chest.RequiredKeyId = "test-chest-key";
        chestGame.Hero.Inventory.Add(CombinableCatalog.ChestKey(chest.RequiredKeyId));
        chestGame.UpdateNearbyInteractable();
        Require(ReferenceEquals(chestGame.NearbyInteractable, chest), "Chest proximity prompt did not activate.");
        Require(chestGame.UseChestKey(chest) && chestGame.OpenChest(chest), "Key/open chest flow failed.");
        int goldBefore = chestGame.Hero.Gold;
        Require(chestGame.LootChestGold(chest) && chestGame.Hero.Gold > goldBefore,
            "Chest gold could not be looted independently.");

        var corpse = new Enemy { Hp = 0, Gold = 19 };
        Require(chestGame.LootGold(corpse) && corpse.Gold == 0, "Corpse gold could not be looted independently.");
        Console.WriteLine($"PASS: {gs.Hero.Equipment.Count} slots, {gs.Hero.Attacks.Count} actions, gear defense +{gs.Hero.EquipmentDefenseBonus}.");
    }

    public static void RunProgressionDemo()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        Console.WriteLine("=== Slot-based progression ===");
        var game = new GameState(808, "Pathfinder", "Warrior", "Human");
        Hero hero = game.Hero;
        ProgressionSlot warriorSlot = hero.Progression.ClassSlots[0];
        Require(hero.Progression.ClassSlots.Count == 2 && hero.Progression.ProfessionSlots.Count == 1,
            "Starting progression slots were not initialized from data.");
        Require(warriorSlot.Instance is { Name: "Warrior", Level: 1 } &&
            hero.Progression.CharacterLevel == 1, "Legacy class creation bridge did not create Warrior 1.");

        int strengthBefore = hero.Strength;
        int constitutionBefore = hero.Constitution;
        int awarded = ProgressionService.Instance.GrantSharedXp(hero, 100);
        ProgressionAdvanceResult classAdvance = game.AllocateClassXp(warriorSlot.SlotId, 100);
        Require(awarded >= 100 && classAdvance is
            { Success: true, LevelsGained: 1, FreeAttributePointsGranted: 2 },
            "Shared XP did not advance the selected class with the configured reward split.");
        Require(hero.Strength == strengthBefore + 1 && hero.Constitution == constitutionBefore + 1 &&
            hero.UnspentStatPoints == 2, "Warrior automatic and free attributes were not granted correctly.");
        Require(hero.Progression.CharacterLevel == 2 && hero.Level == 2,
            "Overall level did not follow the active class level.");

        int sharedBeforeProfession = hero.Progression.UnallocatedXp;
        ProgressionAdvanceResult withoutMiner = game.RecordProfessionAction("miner");
        Require(!withoutMiner.Success && hero.Progression.Skills.Count == 0 &&
            hero.Progression.UnallocatedXp == sharedBeforeProfession,
            "Mining without Miner incorrectly granted profession, skill, or shared XP.");
        new MineOreActivity("iron-ore", 3, 1).OnFinish(game);
        Require(hero.Resources.GetValueOrDefault("iron-ore") == 3 && hero.Progression.Skills.Count == 0,
            "Baseline mining should produce ore without granting profession progression.");
        Require(game.TryActivateProfession("miner", out string professionError), professionError);

        int overallBeforeProfession = hero.Progression.CharacterLevel;
        int minerStrengthBefore = hero.Strength;
        new MineOreActivity("iron-ore", 3, 1).OnFinish(game);
        new MineOreActivity("iron-ore", 3, 1).OnFinish(game);
        ProgressionInstance miner = hero.Progression.ProfessionSlots[0].Instance!;
        Require(miner.Level == 2 && hero.Progression.Skills["mining"].Level == 2,
            "Miner and Mining did not advance together from direct action XP.");
        Require(hero.Strength == minerStrengthBefore + 1 && hero.Progression.CharacterLevel == overallBeforeProfession,
            "Profession fixed reward or overall-level isolation is incorrect.");
        Require(hero.Progression.UnallocatedXp == sharedBeforeProfession,
            "Profession action XP changed the shared XP pool.");
        miner.Level = 10;
        game.RecordProgressionFact("practice.mining-actions", 7);
        string minerInstanceId = miner.InstanceId;
        ProgressionSpecializationOffer prospector = game.GenerateSpecializationOffers(
                hero.Progression.ProfessionSlots[0].SlotId)
            .First(offer => offer.SpecializationId == "prospector");
        Require(game.AcceptSpecializationOffer(hero.Progression.ProfessionSlots[0].SlotId,
            prospector, out string prospectorError), prospectorError);
        Require(miner is { Level: 10, Specialization: { DefinitionId: "prospector" } } &&
            miner.InstanceId == minerInstanceId,
            "Profession specialization reset or replaced the Miner path.");

        int sharedBeforeRejectedAllocation = hero.Progression.UnallocatedXp;
        ProgressionAdvanceResult professionAllocation = game.AllocateClassXp(
            hero.Progression.ProfessionSlots[0].SlotId, sharedBeforeRejectedAllocation);
        Require(!professionAllocation.Success &&
            hero.Progression.UnallocatedXp == sharedBeforeRejectedAllocation,
            "A profession slot accepted allocated shared XP.");

        warriorSlot.Instance!.Level = 9;
        warriorSlot.Instance.CurrentXp = 0;
        string warriorInstanceId = warriorSlot.Instance.InstanceId;
        hero.Progression.UnallocatedXp += 8100;
        ProgressionAdvanceResult levelTen = game.AllocateClassXp(warriorSlot.SlotId, 8100);
        Require(levelTen.Success && warriorSlot.Instance.Level == 10 &&
            warriorSlot.State == ProgressionSlotState.Active,
            "Level 10 incorrectly ended the path instead of opening additive specialization.");
        ProgressionSpecializationOffer swordsman = game.GenerateSpecializationOffers(warriorSlot.SlotId)
            .First(offer => offer.SpecializationId == "swordsman");
        Require(game.AcceptSpecializationOffer(warriorSlot.SlotId, swordsman, out string specializationError),
            specializationError);
        Require(warriorSlot.Instance is
            { Level: 10, Specialization: { DefinitionId: "swordsman" } } &&
            warriorSlot.Instance.InstanceId == warriorInstanceId,
            "Specialization reset or replaced the level-10 Warrior instance.");

        warriorSlot.Instance.Level = 24;
        warriorSlot.Instance.CurrentXp = 0;
        hero.Progression.UnallocatedXp += 60000;
        int bankBeforeCap = hero.Progression.UnallocatedXp;
        ProgressionAdvanceResult capAdvance = game.AllocateClassXp(warriorSlot.SlotId, 60000);
        Require(capAdvance.Success && warriorSlot.Instance.Level == 25 &&
            warriorSlot.State == ProgressionSlotState.Mastered,
            "Class did not stop cleanly at level 25.");
        Require(hero.Progression.UnallocatedXp == bankBeforeCap - 57600,
            "XP beyond the level-25 capacity was not preserved in the shared pool.");

        var migrated = new GameState(809, "LegacyCap", "Warrior", "Human");
        ProgressionInstance migratedInstance = migrated.Hero.Progression.ClassSlots[0].Instance!;
        migratedInstance.Level = 10;
        migratedInstance.MaxLevel = 10;
        ProgressionService.Instance.Normalize(migrated.Hero);
        Require(migratedInstance.MaxLevel == 25 &&
            migrated.Hero.Progression.ClassSlots[0].State == ProgressionSlotState.Active,
            "A saved level-10 path did not migrate to the new level-25 cap.");

        var duplicateGame = new GameState(810, "Weighted", "Warrior", "Human");
        ProgressionSlot duplicateSlot = duplicateGame.Hero.Progression.ClassSlots[1];
        Require(ProgressionService.Instance.TryPlacePath(duplicateGame.Hero, ProgressionDomain.Class,
                duplicateSlot.SlotId, "warrior", out string duplicateError), duplicateError);
        Require(duplicateGame.Hero.Progression.CharacterLevel == 2,
            "Independent duplicate class instances were not counted separately.");

        var mageAdvance = new GameState(811, "Independent", "Mage Apprentice", "Human");
        ProgressionSlot mageSlot = mageAdvance.Hero.Progression.ClassSlots[0];
        mageSlot.Instance!.Level = 25;
        mageAdvance.RecordProgressionFact("knowledge.independent-spell-construction");
        ProgressionAdvancementOffer mageOffer = mageAdvance.GenerateAdvancementOffers(mageSlot.SlotId)
            .First(offer => offer.ResultDefinitionId == "mage");
        Require(mageAdvance.AcceptAdvancementOffer(mageSlot.SlotId, mageOffer, out string mageError), mageError);
        Require(mageSlot.Instance is { DefinitionId: "mage", Level: 1 } &&
            mageSlot.Instance.Lineage.Any(entry => entry is
                { DefinitionId: "mage-apprentice", LevelAtConsumption: 25 }) &&
            mageAdvance.Hero.Class == "Mage",
            "Single-path advancement did not create Mage 1 with preserved lineage.");

        var convergence = new GameState(812, "Integrated", "Warrior", "Human");
        ProgressionSlot warriorSource = convergence.Hero.Progression.ClassSlots[0];
        ProgressionSlot mageSource = convergence.Hero.Progression.ClassSlots[1];
        Require(ProgressionService.Instance.TryPlacePath(convergence.Hero, ProgressionDomain.Class,
            mageSource.SlotId, "mage-apprentice", out string sourceError), sourceError);
        warriorSource.Instance!.Level = 25;
        mageSource.Instance!.Level = 25;
        convergence.RecordProgressionFact("practice.spell-melee-integration", 3);
        ProgressionAdvancementOffer spellsword = convergence.GenerateAdvancementOffers(warriorSource.SlotId)
            .First(offer => offer.ResultDefinitionId == "spellsword");
        Require(convergence.AcceptAdvancementOffer(
            warriorSource.SlotId, spellsword, out string convergenceError), convergenceError);
        Require(warriorSource.Instance is { DefinitionId: "spellsword", Level: 1 } &&
            mageSource.Instance == null && warriorSource.Instance.Lineage.Count == 2 &&
            convergence.Hero.Progression.CharacterLevel == 1 &&
            convergence.Hero.Attacks.Any(attack => attack.Id == "quick-slash") &&
            convergence.Hero.Attacks.Any(attack => attack.Id == "magic-dart"),
            "Convergence did not condense sources while preserving lineage and learned techniques.");

        string json = System.Text.Json.JsonSerializer.Serialize(hero.Progression);
        ProgressionState? restored = System.Text.Json.JsonSerializer.Deserialize<ProgressionState>(json);
        Require(restored != null && restored.CharacterLevel == hero.Progression.CharacterLevel &&
            restored.ClassSlots[0].Instance?.Specialization?.DefinitionId == "swordsman" &&
            restored.ProfessionSlots[0].Instance is
                { Level: 10, Specialization: { DefinitionId: "prospector" } } &&
            restored.Skills["mining"].Level == 2,
            "Progression state did not survive JSON persistence.");

        Console.WriteLine($"PASS: level-10 specialization, level-25 mastery, Mage advancement, " +
            $"Spellsword convergence, and {miner.Name} practice progression.");
    }

    public static void RunEquipmentFirstCreationDemo()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        Console.WriteLine("=== Equipment-first creation and contextual offers ===");
        StarterLoadoutService loadouts = StarterLoadoutService.Instance;
        var characterData = new CharacterDataService();
        string[] primaryClasses =
        {
            "Wanderer", "Warrior", "Archer", "Rogue", "Priest", "Mage Apprentice",
            "Healer", "Alchemist"
        };
        Require(primaryClasses.All(name => characterData.Classes.ContainsKey(name) &&
                ProgressionDataService.Instance.FindDefinition(name) is { Domain: ProgressionDomain.Class }),
            "The primary class roster is inconsistent between character and progression data.");
        Require(characterData.Classes["Healer"].StartingStats.Values.Sum() == 28 &&
                characterData.Classes["Alchemist"].StartingStats.Values.Sum() == 28,
            "New primary class starting profiles do not match the foundation attribute budget.");

        CharacterCreationSelection soldier = loadouts.SelectionFromKit(
            "NoClassYet", "Human", "soldier");
        var game = new GameState(909, soldier);
        Hero hero = game.Hero;
        Require(hero.Class == "Classless" && hero.Progression.CharacterLevel == 0 &&
            hero.Progression.ClassSlots.All(slot => slot.Instance == null),
            "A starter kit directly assigned a class.");
        Require(hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon { WeaponType: WeaponType.Sword } &&
            hero.Attacks.All(attack => attack.Id != "quick-slash"),
            "Soldier equipment or classless technique isolation is incorrect.");

        ProgressionSlot classSlot = hero.Progression.ClassSlots[0];
        IReadOnlyList<ProgressionOffer> soldierOffers = game.GenerateProgressionOffers(
            ProgressionDomain.Class, classSlot.SlotId);
        Require(soldierOffers.Any(offer => offer.ResultDefinitionId == "warrior") &&
            soldierOffers.Any(offer => offer.ResultDefinitionId == "wanderer") &&
            soldierOffers.All(offer => offer.ResultDefinitionId != "archer"),
            "Soldier equipment did not produce the expected contextual class offers.");
        ProgressionOffer warrior = soldierOffers.First(offer => offer.ResultDefinitionId == "warrior");
        Require(game.AcceptProgressionOffer(classSlot.SlotId, warrior, out string warriorError), warriorError);
        Require(hero.Class == "Warrior" && hero.Progression.CharacterLevel == 1 &&
            hero.Attacks.Any(attack => attack.Id == "quick-slash") &&
            WeaponProficiencyService.IsTrained(hero, (Weapon)hero.Equipment[EquipmentSlot.MainHand]),
            "Accepting Warrior did not apply class identity, techniques, level, and affinity.");
        ProgressionSlot secondClassSlot = hero.Progression.ClassSlots[1];
        Require(game.GenerateProgressionOffers(ProgressionDomain.Class, secondClassSlot.SlotId)
                .Any(offer => offer.ResultDefinitionId == "warrior"),
            "A second independently developed Warrior instance was incorrectly suppressed.");

        var outrider = new GameState(910, loadouts.SelectionFromKit("Context", "Elf", "outrider"));
        ProgressionSlot outriderSlot = outrider.Hero.Progression.ClassSlots[0];
        ProgressionOffer archer = outrider.GenerateProgressionOffers(ProgressionDomain.Class, outriderSlot.SlotId)
            .First(offer => offer.ResultDefinitionId == "archer");
        outrider.Hero.Equipment.Remove(EquipmentSlot.MainHand);
        Require(!outrider.AcceptProgressionOffer(outriderSlot.SlotId, archer, out _),
            "An offer remained acceptable after its equipment evidence was removed.");

        var customMage = new CharacterCreationSelection
        {
            Name = "Custom", RaceName = "Human", KitId = "custom", IsCustom = true,
            ItemIds = new List<string> { "staff", "ice-shard", "leather-coat" }
        };
        var mageGame = new GameState(911, customMage);
        Require(mageGame.GenerateProgressionOffers(ProgressionDomain.Class,
                mageGame.Hero.Progression.ClassSlots[0].SlotId)
            .Any(offer => offer.ResultDefinitionId == "mage-apprentice"),
            "A custom staff-and-spell loadout did not reveal Mage Apprentice.");

        var healerGame = new GameState(914, loadouts.SelectionFromKit("Caregiver", "Human", "traveler"));
        healerGame.RecordProgressionFact("practice.healing-actions", 3);
        ProgressionSlot healerSlot = healerGame.Hero.Progression.ClassSlots[0];
        ProgressionOffer healerOffer = healerGame.GenerateProgressionOffers(
                ProgressionDomain.Class, healerSlot.SlotId)
            .First(offer => offer.ResultDefinitionId == "healer");
        Require(healerGame.AcceptProgressionOffer(healerSlot.SlotId, healerOffer, out string healerError),
            healerError);
        Require(healerGame.Hero is { Class: "Healer", ClassData: not null },
            "Healer offer did not resolve to a complete runtime class profile.");

        var alchemistGame = new GameState(915, loadouts.SelectionFromKit("Reagent", "Human", "traveler"));
        alchemistGame.RecordProgressionFact("knowledge.alchemical-reactions");
        ProgressionSlot alchemistSlot = alchemistGame.Hero.Progression.ClassSlots[0];
        ProgressionOffer alchemistOffer = alchemistGame.GenerateProgressionOffers(
                ProgressionDomain.Class, alchemistSlot.SlotId)
            .First(offer => offer.ResultDefinitionId == "alchemist");
        Require(alchemistGame.AcceptProgressionOffer(
            alchemistSlot.SlotId, alchemistOffer, out string alchemistError), alchemistError);
        Require(alchemistGame.Hero is { Class: "Alchemist", ClassData: not null },
            "Alchemist offer did not resolve to a complete runtime class profile.");

        var minerGame = new GameState(912, loadouts.SelectionFromKit("Worker", "Dwarf", "traveler"));
        ProgressionSlot professionSlot = minerGame.Hero.Progression.ProfessionSlots[0];
        int sharedBeforeMining = minerGame.Hero.Progression.UnallocatedXp;
        new MineOreActivity("iron-ore", 3, 1).OnFinish(minerGame);
        IReadOnlyList<ProgressionOffer> professionOffers = minerGame.GenerateProgressionOffers(
            ProgressionDomain.Profession, professionSlot.SlotId);
        Require(professionOffers.Any(offer => offer.ResultDefinitionId == "miner") &&
            minerGame.Hero.Progression.Skills.Count == 0,
            "Baseline mining did not reveal Miner or incorrectly granted a skill.");
        ProgressionOffer minerOffer = professionOffers.First(offer => offer.ResultDefinitionId == "miner");
        Require(minerGame.AcceptProgressionOffer(professionSlot.SlotId, minerOffer, out string minerError), minerError);
        new MineOreActivity("iron-ore", 3, 1).OnFinish(minerGame);
        Require(professionSlot.Instance is { DefinitionId: "miner", CurrentXp: 25 } &&
            minerGame.Hero.Progression.Skills["mining"].CurrentXp == 25 &&
            minerGame.Hero.Progression.UnallocatedXp == sharedBeforeMining,
            "Accepted Miner did not receive isolated direct profession and skill XP.");

        game.RestartGame();
        Require(game.Hero.Class == "Warrior" && game.Hero.Progression.CharacterLevel == 1 &&
            game.Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon { WeaponType: WeaponType.Sword },
            "Restart did not retain equipment-first identity and accepted foundation class.");

        SaveService.Save(game);
        SaveData? saved = SaveService.Load(game.SaveId);
        // Version 5 added the world split (SaveData.WorldId) and the per-character kill count.
        Require(saved?.CreationSelection is { KitId: "soldier" } && saved.Version == 5,
            "Equipment-first creation choices were not persisted.");
        GameState restored = GameState.FromSave(913, saved!);
        Require(restored.Hero.Class == "Warrior" && restored.Hero.Progression.CharacterLevel == 1 &&
            restored.CreationSelection?.ItemIds.Contains("sword") == true,
            "Equipment-first save migration did not restore class and loadout identity.");
        SaveService.Delete(game.SaveId);

        Console.WriteLine($"PASS: {primaryClasses.Length} primary classes, classless start, " +
            $"{soldierOffers.Count} contextual offers; Healer, Alchemist, and Miner evidence resolved.");
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
        ProgressionService.Instance.GrantSharedXp(gs.Hero, 300); // real progress, so "preserved on exit" is meaningful
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
        gs.UpdateNearbyInteractable();
        gs.UseSafeRoomShrine();

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
        ProgressionService.Instance.GrantSharedXp(gs1.Hero, 500);
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
        gs1.UpdateNearbyInteractable();
        gs1.UseSafeRoomShrine();

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
            && gs1.Hero.Inventory.Count == gs2.Hero.Inventory.Count
            && gs2.Hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon { Id: "sword" };
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

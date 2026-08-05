using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>Loads equipment-first creation choices and turns stable item IDs into starter gear.</summary>
public sealed class StarterLoadoutService
{
    private static StarterLoadoutService? _instance;
    public static StarterLoadoutService Instance => _instance ??= new StarterLoadoutService();

    public StarterLoadoutCatalog Catalog { get; }

    public StarterLoadoutService()
    {
        Catalog = LoadCatalog();
    }

    public CharacterCreationSelection SelectionFromKit(string name, string raceName, string kitId)
    {
        StarterKitDefinition kit = Catalog.Kits.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, kitId, StringComparison.OrdinalIgnoreCase))
            ?? Catalog.Kits.First();
        return new CharacterCreationSelection
        {
            Name = name,
            RaceName = raceName,
            KitId = kit.Id,
            ItemIds = new List<string>(kit.ItemIds)
        };
    }

    public void ApplyToHero(Hero hero, IEnumerable<string> itemIds)
    {
        hero.Equipment = new Dictionary<EquipmentSlot, Combinable>();
        hero.Inventory = new List<Combinable>();
        hero.Loadout = new List<Combinable>();

        foreach (string itemId in itemIds)
        {
            Combinable? item = CreateItem(itemId);
            if (item == null) continue;
            switch (item)
            {
                case Spell when hero.Loadout.Count < hero.HotbarCapacity:
                    hero.Loadout.Add(item);
                    break;
                case Armor armor when !hero.Equipment.ContainsKey(armor.Slot):
                    hero.Equipment[armor.Slot] = armor;
                    break;
                case Weapon weapon when TryEquipWeapon(hero, weapon):
                    break;
                case Item accessory when TryEquipAccessory(hero, accessory):
                    break;
                default:
                    hero.Inventory.Add(item);
                    break;
            }
        }
    }

    public Combinable? CreateItem(string itemId) => itemId.ToLowerInvariant() switch
    {
        "sword" => CombinableCatalog.Sword(),
        "dagger" => CombinableCatalog.Dagger(),
        "bow" => CombinableCatalog.Bow(),
        "staff" => CombinableCatalog.Staff(),
        "iron-helm" => CombinableCatalog.IronHelm(),
        "leather-coat" => CombinableCatalog.LeatherCoat(),
        "leather-gloves" => CombinableCatalog.LeatherGloves(),
        "guard-leggings" => CombinableCatalog.GuardLeggings(),
        "trail-boots" => CombinableCatalog.TrailBoots(),
        "shield-generator" => CombinableCatalog.ShieldGenerator(),
        "holy-symbol" => CombinableCatalog.HolySymbol(),
        "ward-ring" => CombinableCatalog.WardRing(),
        "health-potion" => CombinableCatalog.HealthPotion(),
        "fireball" => CombinableCatalog.Fireball(),
        "ice-shard" => CombinableCatalog.IceShard(),
        _ => null
    };

    private static bool TryEquipWeapon(Hero hero, Weapon weapon)
    {
        if (hero.Equipment.GetValueOrDefault(EquipmentSlot.MainHand) is Weapon main)
        {
            if (main.HandsRequired > 1 || weapon.HandsRequired > 1 ||
                hero.Equipment.ContainsKey(EquipmentSlot.OffHand)) return false;
            hero.Equipment[EquipmentSlot.OffHand] = weapon;
            return true;
        }
        if (hero.Equipment.ContainsKey(EquipmentSlot.MainHand) ||
            hero.Equipment.ContainsKey(EquipmentSlot.OffHand)) return false;
        hero.Equipment[EquipmentSlot.MainHand] = weapon;
        return true;
    }

    private static bool TryEquipAccessory(Hero hero, Item item)
    {
        if (!item.EquipSlot.HasValue) return false;
        EquipmentSlot slot = item.EquipSlot.Value;
        if (slot is EquipmentSlot.RingLeft or EquipmentSlot.RingRight)
        {
            if (!hero.Equipment.ContainsKey(EquipmentSlot.RingLeft)) slot = EquipmentSlot.RingLeft;
            else if (!hero.Equipment.ContainsKey(EquipmentSlot.RingRight)) slot = EquipmentSlot.RingRight;
            else return false;
        }
        if (hero.Equipment.ContainsKey(slot)) return false;
        hero.Equipment[slot] = item;
        return true;
    }

    private static StarterLoadoutCatalog LoadCatalog()
    {
        try
        {
            string path = GamePaths.Content("Data", "Progression", "starter-loadouts.json");
            if (File.Exists(path))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                StarterLoadoutCatalog? catalog = JsonSerializer.Deserialize<StarterLoadoutCatalog>(
                    File.ReadAllText(path), options);
                if (catalog is { Kits.Count: > 0, CustomGroups.Count: > 0 }) return catalog;
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"Failed to load starter loadouts, using defaults: {ex.Message}");
        }

        return new StarterLoadoutCatalog
        {
            Kits = new List<StarterKitDefinition>
            {
                new()
                {
                    Id = "traveler", Name = "Traveler's Kit",
                    Description = "Balanced travel gear and basic supplies.",
                    ItemIds = new List<string> { "leather-coat", "trail-boots", "health-potion" }
                }
            },
            CustomGroups = new List<StarterLoadoutGroup>
            {
                new()
                {
                    Id = "weapon", Name = "Weapon",
                    Options = new List<StarterLoadoutOption>
                    {
                        new() { Id = "unarmed", Name = "Unarmed" },
                        new() { Id = "sword", Name = "Sword", ItemIds = new List<string> { "sword" } }
                    }
                }
            }
        };
    }
}

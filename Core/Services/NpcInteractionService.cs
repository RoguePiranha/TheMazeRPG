using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheMazeRPG.Core.Models;

namespace TheMazeRPG.Core.Services;

/// <summary>
/// What pressing E on a townsperson does (Planning note 09 §4 and §4.4 — the services layer on
/// the schedules-first population): Talk barks keyed by occupation and time of day, the per-NPC
/// Opinion relationship (Charisma is the rate of becoming known and liked), merchant stalls that
/// buy AND sell, the trainer teaching spells behind the affinity tier gate, and the priest's
/// healing. Deliberately v1: one bark line per talk, no dialogue trees; crime and the combat
/// shadow are the other half of PR 9 and land separately.
/// </summary>
public static class NpcInteractionService
{
    // --- Opinion (relationships v1 — owner ruling 2026-08-05) ---

    /// <summary>Strangers don't start at zero: a town this size knows your face by day two.</summary>
    public const int StrangerOpinion = 10;

    public const int TalkOpinionGain = 1;
    public const int TradeOpinionGain = 2;
    public const int TrainOpinionGain = 3;
    public const int HealOpinionGain = 2;

    public static int GetOpinion(Hero hero, Npc npc) =>
        hero.NpcOpinions.GetValueOrDefault(npc.Id, StrangerOpinion);

    /// <summary>Charisma scales the growth (× (1 + 0.05·effCha)) — the rate of becoming liked,
    /// never a popularity score in itself.</summary>
    public static void AddOpinion(Hero hero, Npc npc, int baseGain)
    {
        int gain = Math.Max(1, (int)MathF.Round(baseGain * (1f + hero.EffectiveCharisma * 0.05f)));
        hero.NpcOpinions[npc.Id] = Math.Clamp(GetOpinion(hero, npc) + gain, 0, 100);
    }

    /// <summary>Stranger &lt;20, Acquaintance &lt;50, Friend &lt;80, Close 80+. Friend warms the
    /// bark pool; the tiers feed the legacy memory system when note 12's memories start forming.</summary>
    public static string OpinionTier(int opinion) =>
        opinion < 20 ? "Stranger" : opinion < 50 ? "Acquaintance" : opinion < 80 ? "Friend" : "Close";

    // --- Barks ---

    private static Dictionary<string, Dictionary<string, string[]>>? _barks;

    private static Dictionary<string, Dictionary<string, string[]>> Barks()
    {
        if (_barks != null) return _barks;
        try
        {
            var path = GamePaths.Content("Data", "NPCs", "barks.json");
            if (File.Exists(path))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _barks = JsonSerializer
                    .Deserialize<Dictionary<string, Dictionary<string, string[]>>>(
                        File.ReadAllText(path), options);
            }
        }
        catch (Exception ex)
        {
            GameLog.Debug($"barks.json failed to load: {ex.Message}");
        }
        return _barks ??= new Dictionary<string, Dictionary<string, string[]>>();
    }

    /// <summary>Test hook: drop the cached bark table (mirrors the other data services).</summary>
    public static void ResetCachesForTesting() => _barks = null;

    /// <summary>
    /// One bark line for this person at this hour: their occupation's pool for the phase, the
    /// friendly pool mixed in once they consider the hero a friend, the generic pool as the
    /// fallback. Selection rotates deterministically per person and day — no shared RNG.
    /// </summary>
    public static string BarkLine(Npc npc, DayPhase phase, int opinion, int day)
    {
        var barks = Barks();
        string occupation = npc.Occupation.ToString().ToLowerInvariant();
        string phaseKey = phase.ToString().ToLowerInvariant();

        var pool = new List<string>();
        if (barks.TryGetValue(occupation, out var own))
        {
            if (own.TryGetValue(phaseKey, out var lines)) pool.AddRange(lines);
            if (opinion >= 50 && own.TryGetValue("friendly", out var friendly)) pool.AddRange(friendly);
        }
        if (pool.Count == 0 && barks.TryGetValue("generic", out var generic))
        {
            if (generic.TryGetValue(phaseKey, out var lines)) pool.AddRange(lines);
            if (opinion >= 50 && generic.TryGetValue("friendly", out var friendly)) pool.AddRange(friendly);
        }
        if (pool.Count == 0) return "...";

        return pool[Math.Abs(npc.Salt * 31 + day * 7 + (int)phase) % pool.Count];
    }

    /// <summary>Talk: one line, logged in the person's voice. The first chat of a day warms their
    /// opinion a little; repeats don't (no farming a friendship by holding E).</summary>
    public static string Talk(GameState gs, Npc npc)
    {
        int day = gs.Clock.Day;
        var line = BarkLine(npc, gs.Clock.PhaseAt(npc.JitterMinutes), GetOpinion(gs.Hero, npc), day);
        if (gs.Hero.NpcLastTalkDay.GetValueOrDefault(npc.Id, -1) != day)
        {
            gs.Hero.NpcLastTalkDay[npc.Id] = day;
            AddOpinion(gs.Hero, npc, TalkOpinionGain);
        }
        gs.LogMessage($"{npc.Name}: “{line}”", MessageKind.System);
        return line;
    }

    // --- Merchant (note 09 §4.4: rotating stock, the first real gold SINK) ---

    /// <summary>Catalog factories a stall can stock, keyed by their own item ids.</summary>
    private static readonly Dictionary<string, Func<Combinable>> StockFactories =
        new Func<Combinable>[]
        {
            CombinableCatalog.Sword, CombinableCatalog.Dagger, CombinableCatalog.Bow,
            CombinableCatalog.Fireball, CombinableCatalog.IceShard, CombinableCatalog.ShieldGenerator,
            CombinableCatalog.IronHelm, CombinableCatalog.LeatherCoat, CombinableCatalog.LeatherGloves,
            CombinableCatalog.GuardLeggings, CombinableCatalog.TrailBoots, CombinableCatalog.WardRing,
            CombinableCatalog.HealthPotion, CombinableCatalog.Torch, CombinableCatalog.NightSight
        }.ToDictionary(make => make().Id, make => make);

    private const int StockSlots = 4;

    /// <summary>
    /// Today's wares at this merchant: a rotating slice of the catalog, picked deterministically
    /// from the merchant and the calendar — different merchants stock different goods, and the
    /// same stall restocks overnight. Priced for this hero (see <see cref="BuyPrice"/>).
    /// </summary>
    public static List<(Combinable Item, int Price)> MerchantStock(GameState gs, Npc npc)
    {
        var ids = StockFactories.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var stock = new List<(Combinable, int)>();
        int start = Math.Abs(npc.Salt * 13 + gs.Clock.Day * 5) % ids.Count;
        for (int slot = 0; slot < StockSlots; slot++)
        {
            var item = StockFactories[ids[(start + slot * 3) % ids.Count]]();
            stock.Add((item, BuyPrice(gs.Hero, npc, item)));
        }
        return stock;
    }

    /// <summary>Buy price: rarity × 10 × 1.5 against the ×10 sell — the margin is the sink —
    /// shaved by Charisma and by how much this merchant likes you, floored at 75% of list
    /// (owner ruling 2026-08-05: Charisma finally reaches merchant prices). One hard rule on
    /// top of the curves: a merchant never sells below 110% of what they'd pay the same hero
    /// for the same item — at high Charisma the spec's buy floor (0.75×1.5) dips under the sell
    /// cap (×1.25), which would be an infinite-gold loop, and no shopkeeper is that charmed.</summary>
    public static int BuyPrice(Hero hero, Npc npc, Combinable item)
    {
        float baseValue = CombinationEngine.RarityPoints(item.Rarity) * 10f;
        float shave = Math.Max(0.75f, 1f - 0.015f * hero.EffectiveCharisma - 0.001f * GetOpinion(hero, npc));
        float sellValue = baseValue * MathF.Min(1.25f, 1f + 0.01f * hero.EffectiveCharisma);
        return Math.Max(1, (int)MathF.Round(MathF.Max(baseValue * 1.5f * shave, sellValue * 1.1f)));
    }

    /// <summary>Buy today's stock item by id. The purchase lands through the normal loot pipeline
    /// (AcquireLoot), so a bought fireball behaves exactly like a found one.</summary>
    public static bool Buy(GameState gs, Npc npc, string itemId)
    {
        var offer = MerchantStock(gs, npc).FirstOrDefault(entry => entry.Item.Id == itemId);
        if (offer.Item == null) return false;
        if (gs.Hero.Gold < offer.Price)
        {
            gs.LogMessage($"{npc.Name}: “That's {offer.Price} gold, friend. Come back richer.”",
                MessageKind.System);
            return false;
        }

        gs.Hero.Gold -= offer.Price;
        gs.AcquireLoot(offer.Item);
        AddOpinion(gs.Hero, npc, TradeOpinionGain);
        gs.LogMessage($"Bought {offer.Item.Name} for {offer.Price} gold.", MessageKind.Loot);
        return true;
    }

    // --- Trainer (the dormant affinity tier gate's first real consumer) ---

    public sealed record TrainingOffer(
        string Id, string Name, MagicElement Element, int Tier, int Price, bool Known, bool Gated,
        string Requirement);

    /// <summary>Teachable roster: the existing catalog spells as Tier-1s. Tier-0 charms join when
    /// note 07's spell data lands — the gate logic is tier-general already.</summary>
    private static readonly (string Id, Func<Combinable> Make, int Tier)[] TrainerRoster =
    {
        ("fireball", CombinableCatalog.Fireball, 1),
        ("ice-shard", CombinableCatalog.IceShard, 1),
        ("mana-bolt", () => CombinableCatalog.BuildUnlock("mana-bolt")!, 1)
    };

    private const int GoldPerRarityPointTraining = 30;

    public static List<TrainingOffer> TrainingOffers(GameState gs)
    {
        var offers = new List<TrainingOffer>();
        foreach (var (id, make, tier) in TrainerRoster)
        {
            var spell = make();
            var element = MagicElements.For(spell is Spell s ? s.ToAttack() : new Attack { Id = spell.Id, Name = spell.Name });
            bool known = gs.Hero.Loadout.Concat(gs.Hero.Inventory).Any(c => c.Id == id);
            bool canLearn = AffinityService.CanLearn(gs.Hero.Affinities, element, tier);
            int price = CombinationEngine.RarityPoints(spell.Rarity) * GoldPerRarityPointTraining;
            string requirement = canLearn
                ? ""
                : $"requires {element} affinity {RequiredAffinityFor(tier)}";
            offers.Add(new TrainingOffer(id, spell.Name, element, tier, price, known, !canLearn, requirement));
        }
        return offers;
    }

    /// <summary>The affinity a tier needs (inverse of AffinityService.LearnableTier's bands).</summary>
    private static int RequiredAffinityFor(int tier) => tier switch
    {
        <= 0 => 0, 1 => 20, 2 => 45, 3 => 70, _ => 90
    };

    /// <summary>Train a spell: gold and the affinity gate both have to clear. The learned spell
    /// arrives through the loot pipeline like any other Combinable.</summary>
    public static bool Train(GameState gs, Npc npc, string spellId)
    {
        var offer = TrainingOffers(gs).FirstOrDefault(o => o.Id == spellId);
        if (offer == null || offer.Known) return false;
        if (offer.Gated)
        {
            gs.LogMessage($"{npc.Name}: “Not yet. Your {offer.Element} affinity isn't ready for it " +
                          $"({offer.Requirement}).”", MessageKind.System);
            return false;
        }
        if (gs.Hero.Gold < offer.Price)
        {
            gs.LogMessage($"{npc.Name}: “Training costs {offer.Price} gold. Discipline is not free.”",
                MessageKind.System);
            return false;
        }

        var spell = TrainerRoster.First(entry => entry.Id == spellId).Make();
        gs.Hero.Gold -= offer.Price;
        gs.AcquireLoot(spell);
        AddOpinion(gs.Hero, npc, TrainOpinionGain);
        gs.LogMessage($"Learned {offer.Name} for {offer.Price} gold.", MessageKind.LevelUp);
        return true;
    }

    // --- Priest ---

    /// <summary>Full heal (and status cleanse, when statuses exist) for 10 + 2×level gold.</summary>
    public static int HealCost(Hero hero) => 10 + 2 * hero.Level;

    public static bool PriestHeal(GameState gs, Npc npc)
    {
        if (gs.Hero.CurrentHp >= gs.Hero.MaxHp)
        {
            gs.LogMessage($"{npc.Name}: “You're whole already. Go in peace.”", MessageKind.System);
            return false;
        }
        int cost = HealCost(gs.Hero);
        if (gs.Hero.Gold < cost)
        {
            gs.LogMessage($"{npc.Name}: “The rite costs {cost} gold. The divine doesn't haggle.”",
                MessageKind.System);
            return false;
        }

        gs.Hero.Gold -= cost;
        gs.Hero.CurrentHp = gs.Hero.MaxHp;
        // Status cleanse joins here when the status system (note 05) lands.
        AddOpinion(gs.Hero, npc, HealOpinionGain);
        gs.LogMessage($"Healed in full for {cost} gold.", MessageKind.LevelUp);
        return true;
    }
}

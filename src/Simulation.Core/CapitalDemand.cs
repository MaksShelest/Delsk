using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private DeterministicRandom? _demandRandom;
    private int _commonPhase;
    private ImmutableArray<CommodityCycle> _commodityCycles = [];
    private ImmutableArray<FlowView> _demandBaseline = [];
    private readonly List<DemandPeriodView> _demandPeriods = [];
    private ImmutableArray<MarketPricePoint> PublicCapitalPrices(bool history) => [.. _demandPeriods
        .TakeLast(history ? 16 : 1).SelectMany(p => p.Goods.Select(g => new MarketPricePoint(g.ItemId, g.UnitPriceMinor, p.Tick)))];
    private ImmutableArray<FlowView> CurrentFlows() => [.. _flows.Values.OrderBy(f => f.Id, StringComparer.Ordinal)
        .Select(f => new FlowView(f.Id, f.Initial, f.Harvested, f.RecipeInput, f.Produced, f.Exported, f.ConstructionInput,f.Lost))];
    private static long Physical(FlowView f) => checked(f.Initial + f.Harvested + f.Produced - f.RecipeInput - f.ConstructionInput - f.Exported-f.Lost);
    private void InitializeCapitalDemand()
    {
        if (_config.CapitalDemand is not { } settings) return;
        _demandRandom = new(unchecked(_config.Seed ^ 0xD3A4AD18UL));
        _commonPhase = _demandRandom.Index(settings.CommonWaveTicks);
        _commodityCycles = [.. _items.Keys.Order(StringComparer.Ordinal).Select(item =>
            new CommodityCycle(item, _demandRandom.Index(settings.ItemWaveTicks), _demandRandom.Index(settings.ItemWaveTicks)))];
        _demandBaseline = CurrentFlows(); PublishCapitalDemand();
    }
    private static int Wave(long tick, int period, int phase, int minimum, int maximum)
    {
        long position = (tick + phase) % period;
        return minimum + (int)(Math.Min(position, period - position) * 2L * (maximum - minimum) / period);
    }
    private long MarketPrice(ItemDefinition item, long tick, DeterministicRandom random)
    {
        var settings = _config.CapitalDemand!;
        var cycle = _commodityCycles.Single(c => c.ItemId == item.Id);
        int common = Wave(tick, settings.CommonWaveTicks, _commonPhase, EconomyRules.CommonPriceMinimumBasisPoints, EconomyRules.CommonPriceMaximumBasisPoints);
        int commodity = Wave(tick, settings.ItemWaveTicks, cycle.PricePhase, EconomyRules.ItemPriceMinimumBasisPoints, EconomyRules.ItemPriceMaximumBasisPoints);
        int noise = EconomyRules.NoiseMinimumBasisPoints + random.Index(EconomyRules.NoiseMaximumBasisPoints - EconomyRules.NoiseMinimumBasisPoints + 1);
        return Math.Max(1, checked((long)((Int128)item.BasePriceMinor * common * commodity * noise * settings.PriceScaleBasisPoints / 10000000000000000L)));
    }
    private void AdvanceCapitalDemand()
    {
        if (_config.CapitalDemand is { } settings && Tick % settings.PeriodTicks == 0) PublishCapitalDemand();
    }
    private void PublishCapitalDemand()
    {
        var settings = _config.CapitalDemand!;
        var flows = CurrentFlows(); var previous = _demandPeriods.LastOrDefault();
        var metrics = flows.Select(f => {
            var old = _demandBaseline.Single(o => o.ItemId == f.ItemId);
            return new DemandCommodityView(f.ItemId, f.Harvested - old.Harvested, f.Produced - old.Produced,
                checked(f.RecipeInput + f.ConstructionInput - old.RecipeInput - old.ConstructionInput),
                f.Exported - old.Exported, checked(Physical(f) - Physical(old)), 0, 0, f.Lost-old.Lost);
        }).ToArray();
        long gross = metrics.Aggregate(0L, (sum, m) => checked(sum + checked((m.Harvested + m.Produced) * _items[m.ItemId].BasePriceMinor)));
        long consumed = metrics.Aggregate(0L, (sum, m) => checked(sum + checked(m.Consumed * _items[m.ItemId].BasePriceMinor)));
        long stockChange = metrics.Aggregate(0L, (sum, m) => checked(sum + checked(m.StockChange * _items[m.ItemId].BasePriceMinor)));
        Int128 smoothNumerator = (Int128)(previous?.SmoothedOutputBaseMinor ?? 0) + gross + (previous?.SmoothingRemainder ?? 0);
        long smoothed = checked((long)(smoothNumerator / 2)); int carry = (int)(smoothNumerator % 2);
        long target = Math.Max(settings.BootstrapBaseBudgetMinor, checked((long)((Int128)smoothed * settings.DemandNumerator / settings.DemandDenominator)));
        var raw = _config.ResourceNodes.Select(n => n.ItemId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        long[] quantities = metrics.Select(m => raw.Contains(m.ItemId) ? 1L : 0L).ToArray();
        long remaining = checked(target - metrics.Select((m, i) => checked(quantities[i] * _items[m.ItemId].BasePriceMinor)).Sum());
        long[] weights = metrics.Select(m => (long)Wave(Tick, settings.ItemWaveTicks,
            _commodityCycles.Single(c => c.ItemId == m.ItemId).DemandPhase, EconomyRules.DemandWeightMinimumBasisPoints, EconomyRules.DemandWeightMaximumBasisPoints)).ToArray();
        long totalWeight = weights.Sum();
        for (int i = 0; i < metrics.Length; i++)
            quantities[i] = checked(quantities[i] + (long)((Int128)remaining * weights[i] / totalWeight / _items[metrics[i].ItemId].BasePriceMinor));
        long spent = metrics.Select((m, i) => checked(quantities[i] * _items[m.ItemId].BasePriceMinor)).Sum();
        long residual = checked(target - spent);
        // Allocate rounding residue deterministically, then fill with the cheapest unit.
        foreach (int i in Enumerable.Range(0, metrics.Length).OrderByDescending(i => (Int128)remaining * weights[i] % totalWeight).ThenBy(i => i))
        {
            long cost = _items[metrics[i].ItemId].BasePriceMinor;
            if (cost <= residual) { ++quantities[i]; residual -= cost; }
        }
        int cheapest = Enumerable.Range(0, metrics.Length).OrderBy(i => _items[metrics[i].ItemId].BasePriceMinor).ThenBy(i => i).First();
        quantities[cheapest] = checked(quantities[cheapest] + residual / _items[metrics[cheapest].ItemId].BasePriceMinor);
        spent = metrics.Select((m, i) => checked(quantities[i] * _items[m.ItemId].BasePriceMinor)).Sum();
        var orders = new List<CapitalOffer>(); long next = _nextId, monetary = 0;
        for (int i = 0; i < metrics.Length; i++)
        {
            long price = MarketPrice(_items[metrics[i].ItemId], Tick, _demandRandom!);
            metrics[i] = metrics[i] with { DemandQuantity = quantities[i], UnitPriceMinor = price };
            if (quantities[i] == 0) continue;
            long id = next; next = checked(next + 1);
            var order = new CapitalOffer(new(id, metrics[i].ItemId, quantities[i], price, settings.Terminal), $"capital-offer:{id}")
                { CreatedTick = Tick, ExpiresAtTick = checked(Tick + settings.OrderTtlTicks) };
            monetary = checked(monetary + checked(quantities[i] * price)); orders.Add(order);
        }
        // Refunds from closed future contracts may already be available. Fund only the shortfall.
        long funding = Math.Max(0, checked(monetary - _economy.Accounts[_capital].Available));
        var tx = _economy.Begin(Tick);
        if (funding > 0) tx.CreditExternal(_capital, funding, "CapitalDemandBudget");
        foreach (var o in orders) tx.HoldMoney(_capital, o.BudgetHold, checked(o.Definition.Quantity * o.Definition.UnitPriceMinor));
        tx.Commit(); _nextId = next;
        foreach (var o in orders) _offers.Add(o.Definition.Id, o);
        _demandPeriods.Add(new(Tick / settings.PeriodTicks, Tick, gross, consumed, stockChange, smoothed, carry,
            target, spent, monetary, funding, [.. metrics]));
        _demandBaseline = flows;
        Emit("CapitalDemandPublished", quantity: Tick / settings.PeriodTicks, money: funding);
    }
    private void ExpireCapitalOrders()
    {
        foreach (var o in _offers.Values.Where(o => o.ExpiresAtTick == Tick && o.Remaining > 0).OrderBy(o => o.Definition.Id))
        {
            if (o.Requests.Count + o.SupplyRequests.Count != 0) throw new InvalidOperationException("Capital window survived expiry");
            long budget = checked(o.Remaining * o.Definition.UnitPriceMinor);
            var tx = _economy.Begin(Tick); tx.ReleaseMoney(_capital, o.BudgetHold);
            tx.DebitExternal(_capital, budget, "CapitalDemandRetired"); tx.Commit();
            o.ReleasedQuantity = o.Remaining; o.Remaining = 0; o.Cutoff = null;
            Emit("CapitalDemandExpired", entity: o.Definition.Id, quantity: o.ReleasedQuantity, money: budget);
        }
    }
    private CapitalDemandView? CapitalDemandState() => _demandRandom is null ? null : new(_demandRandom.State,
        _commonPhase, _commodityCycles, _demandBaseline, [.. _demandPeriods],
        [.. _offers.Values.OrderBy(o => o.Definition.Id).Select(o => new CapitalOrderRecord(o.Definition,
            o.CreatedTick, o.ExpiresAtTick!.Value, o.ReleasedQuantity))]);
    private void RestoreCapitalDemand(CapitalDemandView? saved)
    {
        if ((_config.CapitalDemand is null) != (saved is null)) throw new CheckpointException("InvalidCapitalDemandState");
        if (saved is null) return;
        _demandRandom!.RestoreState(saved.RandomState); _commonPhase = saved.CommonPhase;
        _commodityCycles = saved.Cycles; _demandBaseline = saved.Baseline;
        _demandPeriods.Clear(); _demandPeriods.AddRange(saved.Periods); _offers.Clear();
        foreach (var v in saved.Orders) _offers.Add(v.Definition.Id, new(v.Definition, $"capital-offer:{v.Definition.Id}")
            { CreatedTick = v.CreatedTick, ExpiresAtTick = v.ExpiresAtTick, ReleasedQuantity = v.ReleasedQuantity });
    }
}

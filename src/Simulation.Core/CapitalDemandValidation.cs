namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateCapitalDemand(ScenarioDefinition s, GridMap map)
    {
        if (s.CapitalDemand is not { } d) return;
        if (s.CapitalOffers.Length != 0 || !map.Walkable(d.Terminal) ||
            (s.Plots ?? []).Any(p => p.Position == d.Terminal && !p.IsCorridor) ||
            d.PeriodTicks < 1 || d.PeriodTicks > 1000000 || d.OrderTtlTicks < s.TradeWindowTicks || d.OrderTtlTicks > 1000000 ||
            d.CommonWaveTicks < 2 || d.CommonWaveTicks > 1000000 || d.ItemWaveTicks < 2 || d.ItemWaveTicks > 1000000 ||
            d.DemandNumerator < 1 || d.DemandNumerator > 100 || d.DemandDenominator < 1 || d.DemandDenominator > 100 ||
            d.PriceScaleBasisPoints < 1000 || d.PriceScaleBasisPoints > 30000 || d.BootstrapBaseBudgetMinor <= 0 ||
            d.BootstrapBaseBudgetMinor > long.MaxValue / 16 || s.Items.Any(i => i.BasePriceMinor > long.MaxValue / 16) ||
            s.ResourceNodes.Length == 0)
            throw new ArgumentException("Invalid capital demand settings");
        try
        {
            long minimum = s.ResourceNodes.Select(n => n.ItemId).Distinct(StringComparer.Ordinal)
                .Sum(id => s.Items.Single(i => i.Id == id).BasePriceMinor);
            if (d.BootstrapBaseBudgetMinor < minimum) throw new ArgumentException("Bootstrap demand cannot buy each raw resource");
            _ = checked(s.MaxTicks + d.OrderTtlTicks);
        }
        catch (OverflowException ex) { throw new ArgumentException("Capital demand arithmetic overflow", ex); }
    }
}

public sealed partial class SimulationEngine
{
    private void ValidateCapitalDemandInvariants()
    {
        if (_config.CapitalDemand is not { } settings)
        {
            if (_demandRandom is not null) throw new InvalidOperationException("Unexpected demand state");
            return;
        }
        var items = _items.Keys.Order(StringComparer.Ordinal).ToArray();
        if (_demandRandom is null || _demandPeriods.Count != Tick / settings.PeriodTicks + 1 ||
            _commodityCycles.IsDefault || !_commodityCycles.Select(c => c.ItemId).SequenceEqual(items) ||
            _demandBaseline.IsDefault || !_demandBaseline.Select(f => f.ItemId).SequenceEqual(items))
            throw new InvalidOperationException("Invalid demand periods or baseline");
        var random = new DeterministicRandom(unchecked(_config.Seed ^ 0xD3A4AD18UL));
        if (_commonPhase != random.Index(settings.CommonWaveTicks)) throw new InvalidOperationException("Invalid common price phase");
        foreach (var cycle in _commodityCycles)
            if (cycle.PricePhase != random.Index(settings.ItemWaveTicks) || cycle.DemandPhase != random.Index(settings.ItemWaveTicks))
                throw new InvalidOperationException("Invalid commodity phase");
        long smooth = 0; int remainder = 0;
        foreach (var p in _demandPeriods)
        {
            if (p.Goods.IsDefault || !p.Goods.Select(g => g.ItemId).SequenceEqual(items) || p.Tick != p.Period * settings.PeriodTicks ||
                p.Period < 0 || p.Tick > Tick || _demandPeriods[(int)p.Period] != p ||
                p.Goods.Any(g => g.Imported<0 || g.Harvested < 0 || g.Produced < 0 || g.Consumed < 0 || g.Exported < 0 || g.DemandQuantity < 0 || g.Lost<0 || g.ConstructionRecovered<0 ||
                    g.StockChange != checked(g.Imported + g.Harvested + g.Produced - g.Consumed - g.Exported - g.Lost + g.ConstructionRecovered)) ||
                p.GrossOutputBaseMinor != p.Goods.Sum(g => checked((g.Harvested + g.Produced) * _items[g.ItemId].BasePriceMinor)) ||
                p.InternalConsumptionBaseMinor != p.Goods.Sum(g => checked(g.Consumed * _items[g.ItemId].BasePriceMinor)) ||
                p.StockChangeBaseMinor != p.Goods.Sum(g => checked(g.StockChange * _items[g.ItemId].BasePriceMinor)))
                throw new InvalidOperationException("Invalid city output accounting");
            Int128 numerator = (Int128)smooth + p.GrossOutputBaseMinor + remainder;
            smooth = checked((long)(numerator / 2)); remainder = (int)(numerator % 2);
            if (p.SmoothedOutputBaseMinor != smooth || p.SmoothingRemainder != remainder ||
                p.TargetBaseMinor != Math.Max(settings.BootstrapBaseBudgetMinor, checked((long)((Int128)smooth * settings.DemandNumerator / settings.DemandDenominator))) ||
                p.OrderedBaseMinor != p.Goods.Sum(g => checked(g.DemandQuantity * _items[g.ItemId].BasePriceMinor)) ||
                p.OrderedBaseMinor > p.TargetBaseMinor || p.TargetBaseMinor - p.OrderedBaseMinor >= _items.Values.Min(i => i.BasePriceMinor) ||
                p.MonetaryBudgetMinor != p.Goods.Sum(g => checked(g.DemandQuantity * g.UnitPriceMinor)) || p.ExternalFundingMinor < 0 ||
                p.ExternalFundingMinor > p.MonetaryBudgetMinor)
                throw new InvalidOperationException("Invalid smoothed demand budget");
            foreach (var g in p.Goods)
            {
                if (g.UnitPriceMinor != MarketPrice(_items[g.ItemId], p.Tick, random))
                    throw new InvalidOperationException("Invalid fixed market price");
                var orders = _offers.Values.Where(o => o.CreatedTick == p.Tick && o.Definition.ItemId == g.ItemId).ToArray();
                if (orders.Length != (g.DemandQuantity == 0 ? 0 : 1) || orders.Any(o => o.Definition.Quantity != g.DemandQuantity || o.Definition.UnitPriceMinor != g.UnitPriceMinor))
                    throw new InvalidOperationException("Generated order differs from published demand");
            }
        }
        if (random.State != _demandRandom.State || _economy.Ledger.Where(l => l.Reason == "CapitalDemandBudget").Sum(l => l.AmountMinor) != _demandPeriods.Sum(p => p.ExternalFundingMinor) ||
            _economy.Ledger.Where(l => l.Reason == "CapitalDemandRetired").Sum(l => l.AmountMinor) != _offers.Values.Sum(o => checked(o.ReleasedQuantity * o.Definition.UnitPriceMinor)))
            throw new InvalidOperationException("Demand funding/random state mismatch");
        foreach (var f in _demandBaseline)
            if (f.Harvested != _demandPeriods.Sum(p => p.Goods.Single(g => g.ItemId == f.ItemId).Harvested) ||
                f.Imported!=_demandPeriods.Sum(p=>p.Goods.Single(g=>g.ItemId==f.ItemId).Imported) || f.Imported>ImportQuantity(f.ItemId) ||
                f.Produced != _demandPeriods.Sum(p => p.Goods.Single(g => g.ItemId == f.ItemId).Produced) ||
                f.RecipeInput + f.ConstructionInput != _demandPeriods.Sum(p => p.Goods.Single(g => g.ItemId == f.ItemId).Consumed) ||
                f.Exported != _demandPeriods.Sum(p => p.Goods.Single(g => g.ItemId == f.ItemId).Exported) ||
                f.Harvested > _flows[f.ItemId].Harvested || f.Produced > _flows[f.ItemId].Produced || f.RecipeInput > _flows[f.ItemId].RecipeInput ||
                f.ConstructionInput > _flows[f.ItemId].ConstructionInput || f.Exported > _flows[f.ItemId].Exported ||
                f.Lost != _demandPeriods.Sum(p=>p.Goods.Single(g=>g.ItemId==f.ItemId).Lost) || f.Lost>_flows[f.ItemId].Lost || f.ConstructionRecovered!=_demandPeriods.Sum(p=>p.Goods.Single(g=>g.ItemId==f.ItemId).ConstructionRecovered) || f.ConstructionRecovered>_flows[f.ItemId].ConstructionRecovered)
                throw new InvalidOperationException("Demand baseline does not match measured periods");
        foreach (var o in _offers.Values)
            if (o.Definition.Id < 1000 || o.Definition.Position != settings.Terminal || o.CreatedTick < 0 || o.CreatedTick > Tick ||
                o.CreatedTick % settings.PeriodTicks != 0 || o.ExpiresAtTick != o.CreatedTick + settings.OrderTtlTicks ||
                o.ReleasedQuantity < 0 || (o.ExpiresAtTick <= Tick && (o.Remaining != 0 || o.Requests.Count + o.SupplyRequests.Count > 0)) ||
                (o.ReleasedQuantity > 0 && o.ExpiresAtTick > Tick) || o.Cutoff > o.ExpiresAtTick)
                throw new InvalidOperationException("Invalid generated order lifecycle");
    }
}

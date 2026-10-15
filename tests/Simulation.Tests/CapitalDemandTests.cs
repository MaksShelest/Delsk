using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class CapitalDemandTests
{
    private static ScenarioDefinition Dynamic(ScenarioDefinition? basis = null, CapitalDemandSettings? settings = null)
        => (basis ?? Fixtures.Cycle()) with { CapitalOffers = [], CapitalDemand = settings ?? new(new(0, 0), PeriodTicks: 20, BootstrapBaseBudgetMinor: 500, OrderTtlTicks: 60) };
    private static GameSession Session(ScenarioDefinition? d = null) => new(new SimulationEngine(d ?? Dynamic()));
    private static CapitalDemandView Demand(GameSession s) => s.Engine.Inspect().CapitalDemand!;
    private static void Execute(GameSession s, GameAction a, long actor = 1) => Assert.Equal("Accepted", LandContentFixtures.Reason(s, a, actor));
    private static CapitalOfferView Raw(GameSession s) => s.Engine.Observe(new(1)).CapitalOffers.Last(o => o.ItemId == "wood" && o.RemainingQuantity > 0);
    [Fact]
    public void NoProductionPublishesBootstrapOnlyAtPeriodBoundariesAndNeverEachTick()
    {
        var s = Session(); long funding = s.Engine.Inspect().ExternalMoneyInMinor; ulong random = Demand(s).RandomState;
        Assert.Single(Demand(s).Periods); Assert.Equal(500, Demand(s).Periods[0].TargetBaseMinor); Assert.True(Raw(s).RemainingQuantity >= 1);
        for (int tick = 1; tick < 20; tick++) { s.Step(); Assert.Single(Demand(s).Periods); Assert.Equal(funding, s.Engine.Inspect().ExternalMoneyInMinor); Assert.Equal(random, Demand(s).RandomState); }
        s.Step(); Assert.Equal(2, Demand(s).Periods.Length); Assert.Equal(0, Demand(s).Periods[1].GrossOutputBaseMinor);
        Assert.Equal(500, Demand(s).Periods[1].TargetBaseMinor);
        Assert.Equal(2, s.Engine.EventsSince(0).Count(e => e.Type == "CapitalDemandPublished"));
    }
    [Theory]
    [InlineData(10000)] [InlineData(20000)]
    public void GrossOutputUsesFixedBasePriceAndElevenTenthsRegardlessOfMarketPrice(int priceScale)
    {
        var s = Session(Dynamic(settings: new(new(0, 0), PeriodTicks: 20, BootstrapBaseBudgetMinor: 500, PriceScaleBasisPoints: priceScale)));
        Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(s); s.AdvanceTo(20);
        var p = Demand(s).Periods[1]; Assert.Equal(1000, p.GrossOutputBaseMinor); Assert.Equal(500, p.SmoothedOutputBaseMinor);
        Assert.Equal(550, p.TargetBaseMinor); Assert.Equal(500, p.OrderedBaseMinor); Assert.Equal(1000, p.StockChangeBaseMinor);
        var control = Session(Dynamic()); Execute(control, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(control); control.AdvanceTo(20);
        Assert.Equal(Demand(control).Periods[1].GrossOutputBaseMinor, p.GrossOutputBaseMinor);
        if (priceScale == 20000) Assert.True(p.Goods.Single(g => g.ItemId == "wood").UnitPriceMinor > Demand(control).Periods[1].Goods.Single(g => g.ItemId == "wood").UnitPriceMinor);
    }
    [Fact]
    public void RawAndFinishedOutputConsumptionAndInventoryChangeAreSeparate()
    {
        var s = Session(Dynamic(settings: new(new(20, 0), PeriodTicks: 50, BootstrapBaseBudgetMinor: 500)));
        Fixtures.Supply(s, 4); var e = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        Execute(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        while (s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches < 2) s.Step();
        Execute(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false)); s.AdvanceTo(50);
        var p = Demand(s).Periods[1]; Assert.Equal(8000, p.GrossOutputBaseMinor); Assert.Equal(2000, p.InternalConsumptionBaseMinor);
        Assert.Equal(6000, p.StockChangeBaseMinor); Assert.Equal(4000, p.SmoothedOutputBaseMinor); Assert.Equal(4400, p.TargetBaseMinor);
        Execute(s, new(ActionKind.Transfer, Source: e.OutputId, Destination: s.Engine.Observe(new(1)).Participant.InventoryId, ItemId: "plank", Quantity: 2));
        s.AdvanceTo(100); Assert.Equal(0, Demand(s).Periods[2].GrossOutputBaseMinor); Assert.Equal(0, Demand(s).Periods[2].StockChangeBaseMinor);
    }
    [Fact]
    public void WipConsumesRawOnceAndDoesNotCountAsFinishedOutput()
    {
        var d = Fixtures.Cycle(); d = d with { Recipes = d.Recipes.Select(r => r with { FullStaffTicks = 100 }).ToArray() };
        var s = Session(Dynamic(d, new(new(20, 0), PeriodTicks: 50, BootstrapBaseBudgetMinor: 500)));
        Fixtures.Supply(s, 2); Execute(s, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true)); s.AdvanceTo(50);
        var p = Demand(s).Periods[1]; Assert.Equal(1000, p.GrossOutputBaseMinor); Assert.Equal(1000, p.InternalConsumptionBaseMinor);
        Assert.Equal(0, p.StockChangeBaseMinor); Assert.NotEqual(0, s.Engine.Observe(new(1)).OwnEnterprises.Single().BatchId);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(100); loaded.AdvanceTo(100);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(0, Demand(s).Periods[2].GrossOutputBaseMinor);
    }
    [Fact]
    public void SmoothingCarriesOddRemainderRatherThanLosingIt()
    {
        var d = Fixtures.Cycle(); d = d with { Items = d.Items.Select(i => i.Id == "wood" ? i with { BasePriceMinor = 501 } : i).ToArray() };
        var s = Session(Dynamic(d, new(new(0, 0), PeriodTicks: 20, BootstrapBaseBudgetMinor: 501)));
        Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)); Fixtures.UntilIdle(s); s.AdvanceTo(40);
        Assert.Equal(250, Demand(s).Periods[1].SmoothedOutputBaseMinor); Assert.Equal(1, Demand(s).Periods[1].SmoothingRemainder);
        Assert.Equal(125, Demand(s).Periods[2].SmoothedOutputBaseMinor); Assert.Equal(1, Demand(s).Periods[2].SmoothingRemainder);
    }
    [Theory]
    [InlineData(15, 1000)] [InlineData(16, 1000)] [InlineData(17, 0)]
    public void CompletedHarvestAtPeriodBoundaryIsIncludedBeforeDemandPublication(int start, long firstGross)
    {
        var s = Session(); s.AdvanceTo(start - 1);
        Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); s.AdvanceTo(40);
        Assert.Equal(firstGross, Demand(s).Periods[1].GrossOutputBaseMinor);
        Assert.Equal(1000 - firstGross, Demand(s).Periods[2].GrossOutputBaseMinor);
    }
    [Fact]
    public void SameSeedRepeatsWavesQuantitiesPricesAndBudgetsWhileCommodityMixChanges()
    {
        var d = Dynamic(settings: new(new(0, 0), PeriodTicks: 20, BootstrapBaseBudgetMinor: 20000, ItemWaveTicks: 80));
        var s = Session(d); var other = Session(d); s.AdvanceTo(160); other.AdvanceTo(160);
        Assert.Equal(s.Engine.ComputeStateHash(), other.Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(Demand(s)), JsonSerializer.Serialize(Demand(other)));
        Assert.True(Demand(s).Periods.Select(p => string.Join(',', p.Goods.Select(g => g.DemandQuantity))).Distinct().Count() > 1);
        Assert.All(Demand(s).Periods, p => {
            Assert.Equal(20000, p.TargetBaseMinor); Assert.InRange(p.TargetBaseMinor - p.OrderedBaseMinor, 0, 499);
            Assert.True(p.Goods.Single(g => g.ItemId == "wood").DemandQuantity >= 1);
            foreach (var g in p.Goods) Assert.InRange(g.UnitPriceMinor, d.Items.Single(i => i.Id == g.ItemId).BasePriceMinor * 7056 / 10000,
                d.Items.Single(i => i.Id == g.ItemId).BasePriceMinor * 13464 / 10000);
        });
        var different = Session(d with { Seed = d.Seed + 1 }); different.AdvanceTo(160);
        Assert.NotEqual(JsonSerializer.Serialize(Demand(s).Periods), JsonSerializer.Serialize(Demand(different).Periods));
    }
    [Fact]
    public void FutureWavesPrivatePhasesAndPrngNeverEnterControllerOrParticipantObservation()
    {
        var s = Session(); s.AdvanceTo(31); string hash = s.Engine.ComputeStateHash();
        var controller = JsonNode.Parse(JsonSerializer.Serialize(s.Engine.ObserveController(new(1)), SimulationJson.Options))!;
        var participant = JsonNode.Parse(JsonSerializer.Serialize(s.Engine.Observe(new(1)), SimulationJson.Options))!;
        foreach (var node in new[] { controller, participant }) {
            var names = Names(node).ToHashSet();
            Assert.DoesNotContain("seed", names); Assert.DoesNotContain("randomState", names);
            Assert.DoesNotContain("capitalDemand", names); Assert.DoesNotContain("cycles", names);
            Assert.DoesNotContain("pricePhase", names); Assert.DoesNotContain("demandPhase", names);
        }
        Assert.All(s.Engine.ObserveController(new(1)).CapitalOffers, o => Assert.True(o.CreatedTick <= s.Engine.Tick));
        Assert.All(s.Engine.ObserveController(new(1)).CapitalPriceHistory, p => Assert.True(p.PublishedAtTick <= s.Engine.Tick));
        Assert.Equal(hash, s.Engine.ComputeStateHash());
        static IEnumerable<string> Names(JsonNode node) {
            if (node is JsonObject obj) foreach (var p in obj) { yield return p.Key; if (p.Value is { } value) foreach (var name in Names(value)) yield return name; }
            else if (node is JsonArray array) foreach (var value in array) if (value is not null) foreach (var name in Names(value)) yield return name;
        }
    }
    [Fact]
    public void PublishedPricesExistEvenWhenBudgetCannotBuyExpensiveCommodity()
    {
        var s = Session(); s.AdvanceTo(31); var o = s.Engine.ObserveController(new(1));
        Assert.DoesNotContain(o.CapitalOffers, offer => offer.ItemId == "plank");
        Assert.Equal(2, o.CapitalPrices.Length);
        Assert.Contains(o.CapitalPrices, price => price.ItemId == "plank" && price.PublishedAtTick == 20 && price.UnitPriceMinor > 0);
        Assert.Equal(4, o.CapitalPriceHistory.Length);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(o.CapitalPrices, loaded.Engine.ObserveController(new(1)).CapitalPrices);
        Assert.Equal(o.CapitalPriceHistory, loaded.Engine.ObserveController(new(1)).CapitalPriceHistory);
    }
    [Fact]
    public void ZeroCashCanHarvestAndSellAfterSeveralZeroOutputPeriods()
    {
        var s = Session(Dynamic(Fixtures.Cycle(cash: 0))); s.AdvanceTo(61);
        Assert.All(Demand(s).Periods, p => Assert.Equal(0, p.GrossOutputBaseMinor));
        Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)); Fixtures.UntilIdle(s);
        var o = Raw(s); Execute(s, new(ActionKind.SellAvailableToCapital, OfferId: o.Id, ItemId: "wood", Quantity: 1));
        s.AdvanceTo(s.Engine.Tick + 5); Assert.Equal(o.UnitPriceMinor, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(1, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Exported);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InclusiveOrderExpiryProcessesWindowBeforeRetiringUnusedBudget(bool supply)
    {
        var d = Dynamic(settings: new(new(0, 0), PeriodTicks: 60, BootstrapBaseBudgetMinor: 1000, OrderTtlTicks: 20));
        var s = Session(d); var o = Raw(s); Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)); Fixtures.UntilIdle(s);
        s.AdvanceTo(19); Execute(s, supply ? new(ActionKind.AcceptDeliveryQuantity, OfferId: o.Id, Quantity: 1) :
            new(ActionKind.SellAvailableToCapital, OfferId: o.Id, ItemId: "wood", Quantity: 1));
        var final = s.Engine.Inspect().CapitalOffers.Single(a => a.Id == o.Id);
        Assert.Equal(0, final.RemainingQuantity); Assert.Equal(o.OriginalQuantity - 1, final.ReleasedQuantity);
        if (supply) Assert.Single(s.Engine.Inspect().DeliveryContracts);
        else Assert.Equal(o.UnitPriceMinor, s.Engine.Inspect().Settlements.Single().PaidMinor);
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, new(ActionKind.SellAvailableToCapital, OfferId: o.Id, ItemId: "wood", Quantity: 1)));
    }
    [Fact]
    public void SpotAndFutureShareOneBudgetAndExpiredContractRefundFundsNextPeriod()
    {
        var d = Dynamic(settings: new(new(0, 0), PeriodTicks: 20, BootstrapBaseBudgetMinor: 20000, OrderTtlTicks: 40));
        d = d with { Participants = d.Participants.Select(p => p.Id == 2 ? p with { CashMinor = 1000 } : p).ToArray(), Delivery = new(ContractTtlTicks: 8) };
        var s = Session(d); Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(s);
        var o = Raw(s); long initialEmission = s.Engine.Inspect().ExternalMoneyInMinor;
        s.Send(new(1), new(ActionKind.SellAvailableToCapital, OfferId: o.Id, ItemId: "wood", Quantity: 1));
        s.Send(new(2), new(ActionKind.AcceptDeliveryQuantity, OfferId: o.Id, Quantity: 2)); s.Step(); s.AdvanceTo(s.Engine.Tick + 5);
        var contract = Assert.Single(s.Engine.Inspect().DeliveryContracts); Assert.Equal(2, contract.Quantity);
        Assert.Equal(initialEmission, s.Engine.Inspect().ExternalMoneyInMinor);
        Assert.Equal(o.OriginalQuantity - 3, s.Engine.Inspect().CapitalOffers.Single(a => a.Id == o.Id).RemainingQuantity);
        s.AdvanceTo(20); var next = Demand(s).Periods[1];
        Assert.Equal("Expired", s.Engine.Inspect().DeliveryContracts.Single().Status);
        Assert.Equal(next.MonetaryBudgetMinor - 2 * o.UnitPriceMinor, next.ExternalFundingMinor);
        Assert.Equal(initialEmission + next.ExternalFundingMinor, s.Engine.Inspect().ExternalMoneyInMinor);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(80); loaded.AdvanceTo(80);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(Demand(s).Periods.Sum(p => p.ExternalFundingMinor), s.Engine.Inspect().ExternalMoneyInMinor);
    }
    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(19)] [InlineData(20)] [InlineData(21)] [InlineData(39)] [InlineData(40)] [InlineData(57)]
    public void SaveInsidePeriodHarvestWindowBoundaryAndExpiryContinuesEveryHash(int cut)
    {
        var s = Session(); Execute(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); s.AdvanceTo(cut);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < 100) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0)); Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger());
    }
    [Theory]
    [InlineData("random")] [InlineData("phase")] [InlineData("smoothing")] [InlineData("budget")] [InlineData("order")]
    public void CorruptDemandRandomPhasesPeriodsOrOrderBudgetsAreRejected(string field)
    {
        var s = Session(); s.AdvanceTo(31); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; var demand = json["capitalDemand"]!;
        if (field == "random") demand["randomState"] = 1UL;
        else if (field == "phase") demand["commonPhase"] = 999999;
        else if (field == "smoothing") demand["periods"]![1]!["smoothedOutputBaseMinor"] = 10;
        else if (field == "budget") demand["periods"]![1]!["targetBaseMinor"] = 99999;
        else demand["orders"]![0]!["definition"]!["quantity"] = 99999;
        var error = Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
        Assert.NotEqual("StateHashMismatch", error.ReasonCode);
    }
    [Theory]
    [InlineData("period")] [InlineData("bootstrap")] [InlineData("scale")] [InlineData("double")] [InlineData("terminal")]
    public void InvalidDemandConfigurationFailsBeforeFunding(string field)
    {
        var d = Dynamic(); d = field switch {
            "period" => d with { CapitalDemand = d.CapitalDemand! with { PeriodTicks = 0 } },
            "bootstrap" => d with { CapitalDemand = d.CapitalDemand! with { BootstrapBaseBudgetMinor = 1 } },
            "scale" => d with { CapitalDemand = d.CapitalDemand! with { PriceScaleBasisPoints = 0 } },
            "double" => d with { CapitalOffers = Fixtures.Cycle().CapitalOffers },
            _ => d with { CapitalDemand = d.CapitalDemand! with { Terminal = new(-1, 0) } } };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d));
    }
}

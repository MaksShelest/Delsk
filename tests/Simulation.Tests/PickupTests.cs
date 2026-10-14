using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PickupTests
{
    private static ScenarioDefinition Scenario(int ttl = 120, int fine = 500, int capacity = 20)
    {
        using var stream = typeof(PickupTests).Assembly.GetManifestResourceStream("Fixture.pickup-lifecycle.json")!;
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        return d with { Market = new(PickupTtlTicks: ttl, PickupFineBasisPoints: fine),
            Participants = d.Participants.Select(p => p.Id == 2 ? p with { InventoryCapacity = capacity } : p).ToArray() };
    }
    private static GameSession Purchased(int ttl = 120, int fine = 500, int capacity = 20, long price = 500)
    {
        var s = new GameSession(new SimulationEngine(Scenario(ttl, fine, capacity)));
        LandContentFixtures.Harvest(s, "wood", 10); LandContentFixtures.Move(s, new(2, 0));
        var own = s.Engine.Observe(new(1));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 10,
            Source: own.Participant.InventoryId, Destination: own.OwnBuildings.Single().InputId));
        Fixtures.Command(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 10, UnitPriceMinor: price));
        long offer = s.Engine.Inspect().MarketOffers.Single().Id;
        Fixtures.Command(s, new(ActionKind.SubmitBuyRequest, MarketOfferId: offer, Quantity: 10, MaximumPriceMinor: price), 2);
        s.AdvanceTo(s.Engine.Inspect().MarketOffers.Single().CutoffTick);
        return s;
    }
    private static MarketAllocationView Purchase(GameSession s) => s.Engine.Inspect().MarketAllocations.Single();
    private static long Cash(GameSession s, long actor) => s.Engine.Observe(new(actor)).Account.BalanceMinor;
    private static void Move(GameSession s, Cell position)
    { Fixtures.Command(s, new(ActionKind.MoveTo, Target: position), 2); while (s.Engine.Observe(new(2)).Participant.Busy) s.Step(); }
    private static string Collect(GameSession s, long q, long actor = 2)
        => LandContentFixtures.Reason(s, new(ActionKind.CollectPurchase, PurchaseId: Purchase(s).Id, Quantity: q), actor);
    private static void Unload(GameSession s)
    {
        var o = s.Engine.Observe(new(2));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: o.OwnInventories.Single(i => i.Id == o.Participant.InventoryId).Stock.Single().Quantity,
            Source: o.Participant.InventoryId, Destination: o.OwnBuildings.Single().InputId), 2);
    }
    [Fact]
    public void FullPurchaseCanBeCollectedInFourTripsWithLimitedCarrier()
    {
        var s = Purchased(capacity: 3);
        foreach (long quantity in new long[] { 3, 3, 3, 1 })
        {
            Move(s, new(2, 0)); Assert.Equal("Accepted", Collect(s, quantity));
            Move(s, new(5, 0)); Unload(s);
        }
        Assert.Equal("Fulfilled", Purchase(s).Status); Assert.Equal(10, Purchase(s).CollectedQuantity);
        Assert.Equal(5000, Purchase(s).PaidMinor); Assert.Equal(0, Purchase(s).RemainingQuantity);
        s.AdvanceTo(Purchase(s).DeadlineTick); Assert.Equal(0, Purchase(s).FineMinor);
        Assert.Equal(15000, Cash(s, 1)); Assert.Equal(5000, Cash(s, 2));
    }
    [Fact]
    public void QueuedCollectionRestoresAndTheSameReceiptHasOnePayment()
    {
        var s = Purchased(); Move(s, new(2, 0));
        string id = s.Send(new(2), new(ActionKind.CollectPurchase, PurchaseId: Purchase(s).Id, Quantity: 6));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.Step(); loaded.Step();
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(ResultStatus.Accepted, loaded.Engine.Result(id)!.Status);
        Assert.Equal(3000, Purchase(loaded).PaidMinor);
        Assert.Single(loaded.Engine.Ledger(), l => l.Reason == "PickupPayment");
    }
    [Fact]
    public void PickupProgressIsVisibleOnlyToItsPartiesAndResearcher()
    {
        var d = Scenario(); d = d with { Participants = [.. d.Participants,
            d.Participants[1] with { Id = 3, Position = new(5, 1) }] };
        var s = new GameSession(new SimulationEngine(d)); ScriptedScenarios.PickupLifecycle(s, d);
        Assert.Single(s.Engine.ObserveController(new(1)).OwnMarketAllocations);
        Assert.Single(s.Engine.ObserveController(new(2)).OwnMarketAllocations);
        Assert.Empty(s.Engine.ObserveController(new(3)).OwnMarketAllocations);
        Assert.Empty(s.Engine.Observe(new(3)).OwnMarketAllocations);
        Assert.Single(s.Engine.Inspect().MarketAllocations);
    }
    [Fact]
    public void PreviousWorldSchemaIsRejectedExplicitly()
    {
        var s = Purchased(); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; json["schemaVersion"] = 2;
        Assert.Equal("UnknownWorldSchema", Assert.Throws<CheckpointException>(() =>
            SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
    [Fact]
    public void FullCollectionSettlesAtLoadingAndExpiryCannotRevokeGoodsInTransit()
    {
        var s = Purchased(ttl: 6); Move(s, new(2, 0)); Assert.Equal("Accepted", Collect(s, 10));
        Assert.Equal("Fulfilled", Purchase(s).Status); Assert.Equal(5000, Purchase(s).PaidMinor);
        Assert.Equal(15000, Cash(s, 1)); Assert.Equal(5000, Cash(s, 2));
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(5, 0)), 2);
        Assert.True(s.Engine.Observe(new(2)).Participant.Busy);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end = Purchase(s).DeadlineTick + 3; s.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(10, loaded.Engine.Observe(new(2)).OwnInventories.Single(i => i.Id == loaded.Engine.Observe(new(2)).Participant.InventoryId).Stock.Single().Quantity);
        Assert.Equal(0, Purchase(s).FineMinor); Assert.Equal(0, Purchase(s).RefundedMinor);
    }
    [Fact]
    public void TwoPhysicalTripsPaySixUnitsAndExpireOnlyFourWithoutDuplicatingGoods()
    {
        var s = Purchased(capacity: 3); Move(s, new(2, 0)); Assert.Equal("Accepted", Collect(s, 3));
        Assert.Equal("NoCapacity", Collect(s, 1)); Assert.Equal(7, Purchase(s).RemainingQuantity);
        Move(s, new(5, 0)); Unload(s); Move(s, new(2, 0)); Assert.Equal("Accepted", Collect(s, 3));
        Assert.Equal("PartiallyCollected", Purchase(s).Status); Assert.Equal(13000, Cash(s, 1)); Assert.Equal(5000, Cash(s, 2));
        s.AdvanceTo(Purchase(s).DeadlineTick);
        Assert.Equal("Expired", Purchase(s).Status); Assert.Equal(6, Purchase(s).CollectedQuantity); Assert.Equal(4, Purchase(s).ReleasedQuantity);
        Assert.Equal(100, Purchase(s).FineMinor); Assert.Equal(1900, Purchase(s).RefundedMinor);
        Assert.Equal(13100, Cash(s, 1)); Assert.Equal(6900, Cash(s, 2));
        Assert.Equal(20000, s.Engine.Inspect().Accounts.Sum(a => a.BalanceMinor));
        Assert.Equal(10, s.Engine.Inspect().Inventories.Sum(i => i.Stock.Sum(stock => stock.Quantity)));
        var seller = s.Engine.Observe(new(1)); Assert.Equal(4, seller.OwnInventories.Single(i => i.Id == Purchase(s).SourceInventory).Stock.Single().Quantity);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 4, UnitPriceMinor: 500)));
    }
    [Theory]
    [InlineData(0, 0, 5000)] [InlineData(500, 250, 4750)] [InlineData(10000, 5000, 0)]
    public void ZeroCollectionRefundsRemainingEscrowWithConfiguredFine(int rate, long fine, long refund)
    {
        var s = Purchased(fine: rate); s.AdvanceTo(Purchase(s).DeadlineTick);
        Assert.Equal(10, Purchase(s).ReleasedQuantity); Assert.Equal(fine, Purchase(s).FineMinor); Assert.Equal(refund, Purchase(s).RefundedMinor);
        Assert.Equal(10000 + fine, Cash(s, 1)); Assert.Equal(5000 + refund, Cash(s, 2));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(s.Engine.Tick + 5); Assert.Equal(ledger, s.Engine.Ledger());
        Assert.Equal("PurchaseClosed", Collect(s, 1));
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public void PickupDeadlineIsInclusiveAndNextTickIsClosed(int offset)
    {
        var s = Purchased(); Move(s, new(2, 0)); s.AdvanceTo(Purchase(s).DeadlineTick + offset - 1);
        Assert.Equal(offset <= 0 ? "Accepted" : "PurchaseClosed", Collect(s, 6));
        s.AdvanceTo(Math.Max(s.Engine.Tick, Purchase(s).DeadlineTick));
        Assert.Equal(offset <= 0 ? 6 : 0, Purchase(s).CollectedQuantity);
        Assert.Equal(offset <= 0 ? 100 : 250, Purchase(s).FineMinor);
    }
    [Fact]
    public void ArrivalOnDeadlineCanCollectBeforePhaseSevenExpiry()
    {
        var s = Purchased(); long deadline = Purchase(s).DeadlineTick;
        s.AdvanceTo(deadline - 4); Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(2, 0)), 2);
        s.AdvanceTo(deadline - 1); Assert.True(s.Engine.Observe(new(2)).Participant.Busy);
        Assert.Equal("Accepted", Collect(s, 10)); Assert.Equal("Fulfilled", Purchase(s).Status);
        Assert.DoesNotContain(s.Engine.EventsSince(0), e => e.Type == "PurchaseExpired");
    }
    [Fact]
    public void WrongBuyerLocationBusyAndQuantityCannotConsumeOrPayClaim()
    {
        var s = Purchased(); var before = Purchase(s);
        Assert.Equal("NotAvailable", Collect(s, 1, 1)); Assert.Equal("NotAtLocation", Collect(s, 1));
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(2, 0)), 2); Assert.Equal("ParticipantBusy", Collect(s, 1));
        while (s.Engine.Observe(new(2)).Participant.Busy) s.Step();
        Assert.Equal("InvalidQuantity", Collect(s, 0)); Assert.Equal("InvalidQuantity", Collect(s, 11));
        Assert.Equal(before, Purchase(s)); Assert.Equal(10000, Cash(s, 1)); Assert.Equal(5000, Cash(s, 2));
    }
    [Fact]
    public void RepeatEnvelopeAfterLoadDoesNotRepeatPaymentAndFreshOversizedCommandFails()
    {
        var s = Purchased(); Move(s, new(2, 0)); string id = Fixtures.Command(s, new(ActionKind.CollectPurchase, PurchaseId: Purchase(s).Id, Quantity: 6), 2);
        var c = s.CommandTape.Single(x => x.Command.CommandId == id).Command;
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); var before = loaded.Engine.ComputeStateHash();
        Assert.Equal(ResultStatus.Accepted, loaded.Engine.Enqueue(new(2), c)!.Status); Assert.Equal(before, loaded.Engine.ComputeStateHash());
        Assert.Equal("CommandIdConflict", loaded.Engine.Enqueue(new(2), c with { Action = c.Action with { Quantity = 4 } })!.ReasonCode);
        Assert.Equal("InvalidQuantity", Collect(loaded, 5)); Assert.Equal(3000, Purchase(loaded).PaidMinor);
    }
    [Theory]
    [InlineData(31)] [InlineData(35)] [InlineData(36)] [InlineData(40)] [InlineData(46)] [InlineData(47)] [InlineData(48)] [InlineData(50)]
    public void DirectLoadAndRecordedReplayMatchEachTickAcrossTripsAndExpiry(int cut)
    {
        var d = Scenario(ttl: 17, capacity: 3); var recording = new GameSession(new SimulationEngine(d));
        ScriptedScenarios.PickupLifecycle(recording, d);
        var continuous = new GameSession(new SimulationEngine(d)); continuous.ConfigurePlayback(recording.CommandTape, recording.Engine.Tick, "pickup");
        continuous.AdvanceTo(cut); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(continuous));
        while (continuous.Engine.Tick < recording.Engine.Tick)
        {
            continuous.Step(); loaded.Step(); Assert.Equal(continuous.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        }
        Assert.Equal(recording.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(recording.Engine.EventsSince(0), loaded.Engine.EventsSince(0)); Assert.Equal(recording.Engine.Ledger(), loaded.Engine.Ledger());
    }
    [Theory]
    [InlineData("collectedQuantity", 1)] [InlineData("releasedQuantity", 1)] [InlineData("deadlineTick", 999)]
    [InlineData("paidMinor", 500)] [InlineData("fineMinor", 1)] [InlineData("refundedMinor", 1)]
    public void CorruptPickupCountersDeadlineAndSettlementAreRejected(string field, long value)
    {
        var s = Purchased(); JsonNode json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["marketAllocations"]![0]![field] = value;
        string reason = Assert.Throws<CheckpointException>(() =>
            SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode;
        Assert.Contains(reason, new[] { "InvalidWorldCheckpoint", "OrphanStockReservation" });
    }
    [Fact]
    public void FineRoundsDownToMinorUnitAndDoesNotOverflowIntermediateProduct()
    {
        var s = Purchased(fine: 333, price: 1); s.AdvanceTo(Purchase(s).DeadlineTick);
        Assert.Equal(0, Purchase(s).FineMinor); Assert.Equal(10, Purchase(s).RefundedMinor);
        // Product for the fine is wider than long; realistic account totals still fit long.
        var d = Scenario() with { Participants = Scenario().Participants.Select(p => p with { CashMinor = p.Id == 2 ? long.MaxValue / 2 : 0 }).ToArray() };
        var rich = new GameSession(new SimulationEngine(d));
        LandContentFixtures.Harvest(rich, "wood", 1); LandContentFixtures.Move(rich, new(2, 0));
        var own = rich.Engine.Observe(new(1)); Fixtures.Command(rich, new(ActionKind.Transfer, ItemId: "wood", Quantity: 1, Source: own.Participant.InventoryId, Destination: own.OwnBuildings.Single().InputId));
        Fixtures.Command(rich, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 1, UnitPriceMinor: long.MaxValue / 2));
        var offer = rich.Engine.Inspect().MarketOffers.Single(); Fixtures.Command(rich, new(ActionKind.SubmitBuyRequest, MarketOfferId: offer.Id, Quantity: 1, MaximumPriceMinor: long.MaxValue / 2), 2);
        rich.AdvanceTo(offer.CutoffTick); rich.AdvanceTo(Purchase(rich).DeadlineTick);
        Assert.Equal((long.MaxValue / 2) / 20, Purchase(rich).FineMinor);
    }
    [Theory]
    [InlineData(0, 500)] [InlineData(1, -1)] [InlineData(1, 10001)]
    public void InvalidPickupSettingsFailBeforeWorldCreation(int ttl, int fine)
        => Assert.Throws<ArgumentException>(() => new SimulationEngine(Scenario(ttl, fine)));
}

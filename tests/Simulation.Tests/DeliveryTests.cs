using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class DeliveryTests
{
    private static ScenarioDefinition Scenario(int ttl = 120, int capacity = 80, long supplierCash = 10000, bool capital = false)
    {
        using var stream = typeof(DeliveryTests).Assembly.GetManifestResourceStream("Fixture.delivery-lifecycle.json")!;
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        return d with { Delivery = new(ContractTtlTicks: ttl),
            Participants = d.Participants.Select(p => p.Id == 1 ? p with { InventoryCapacity = 20, CashMinor = supplierCash } : p).ToArray(),
            BuildingTypes = d.BuildingTypes!.Select(t => t with { Levels = t.Levels.Select(l => l with { InputCapacity = capacity }).ToArray() }).ToArray(),
            CapitalOffers = capital ? [new(1, "wood", 10, 500, new(5, 1))] : [] };
    }
    private static GameSession Session(ScenarioDefinition? d = null) => new(new SimulationEngine(d ?? Scenario()));
    private static long Offer(GameSession s, long q = 10, long price = 500)
    {
        var shop = s.Engine.Observe(new(2)).OwnBuildings.Single();
        Assert.Equal("Accepted", Reason(s, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "wood", Quantity: q, UnitPriceMinor: price), 2));
        return s.Engine.Inspect().BuyOffers.Last().Id;
    }
    private static string Reason(GameSession s, GameAction a, long actor = 1) => LandContentFixtures.Reason(s, a, actor);
    private static string Request(GameSession s, long offer, long q, long actor = 1, int window = 1)
        => Reason(s, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: q, OfferWindow: window), actor);
    private static DeliveryContractView Contract(GameSession s) => s.Engine.Inspect().DeliveryContracts.Single();
    private static GameSession Contracted(bool capital = false, int ttl = 120, long supplierCash = 10000)
    {
        var s = Session(Scenario(ttl: ttl, supplierCash: supplierCash, capital: capital));
        if (capital) Assert.Equal("Accepted", Reason(s, new(ActionKind.AcceptDeliveryQuantity, OfferId: 1, Quantity: 10)));
        else { long o = Offer(s); Assert.Equal("Accepted", Request(s, o, 10)); }
        long close = capital ? s.Engine.Observe(new(1)).CapitalOffers.Single().CutoffTick!.Value : s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value;
        s.AdvanceTo(close); return s;
    }
    private static string Deliver(GameSession s, long quantity, long actor = 1) => Reason(s, new(ActionKind.DeliverContract, ContractId: Contract(s).Id, Quantity: quantity), actor);
    private static void Bring(GameSession s, long q)
    { LandContentFixtures.Harvest(s, "wood", q); LandContentFixtures.Move(s, Contract(s).Position); }
    private static long Cash(GameSession s, long actor) => s.Engine.Observe(new(actor)).Account.BalanceMinor;
    [Fact]
    public void FuturePromiseUsesEscrowBondAndFullDestinationReservationWithoutCreatingStock()
    {
        var s = Contracted(); var d = Contract(s);
        Assert.Empty(s.Engine.Inspect().Inventories.SelectMany(i => i.Stock));
        Assert.Equal(9750, Cash(s, 1)); Assert.Equal(5000, Cash(s, 2));
        Assert.Equal(5000, s.Engine.Inspect().Accounts.Single(a => a.Id == d.EscrowAccount).BalanceMinor);
        Assert.Equal(250, s.Engine.Inspect().Accounts.Single(a => a.Id == d.BondAccount).BalanceMinor);
        Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == d.DestinationInventory).ReservedCapacity);
        Assert.Equal(10, d.UnprovidedQuantity); Assert.Equal(0, d.InTransitQuantity); Assert.Equal(0, d.PreparedQuantity);
        LandContentFixtures.Move(s, d.Position); Assert.Equal("NoStock", Deliver(s, 1));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteDeliveryPaysGoodsAndReturnsEntireBond(bool capital)
    {
        var s = Contracted(capital); Bring(s, 10); Assert.Equal("Accepted", Deliver(s, 10)); var d = Contract(s);
        Assert.Equal("Fulfilled", d.Status); Assert.Equal(5000, d.PaidMinor); Assert.Equal(250, d.BondReturnedMinor);
        Assert.Equal(15000, Cash(s, 1)); Assert.Equal(0, d.RemainingQuantity);
        Assert.Equal(capital ? 10 : 0, s.Engine.Inspect().Flows.Single().Exported);
        Assert.Equal(capital ? 0 : 10, s.Engine.Inspect().Inventories.Sum(i => i.Stock.Sum(stock => stock.Quantity)));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(d.DeadlineTick + 1); Assert.Equal(ledger, s.Engine.Ledger());
        Assert.Equal("ContractClosed", Deliver(s, 1));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PartialReceiptRefundsOnlyMissingGoodsAndFineComesFromBond(bool capital)
    {
        var s = Contracted(capital); Bring(s, 6); Assert.Equal("Accepted", Deliver(s, 6));
        Assert.Equal("PartiallyDelivered", Contract(s).Status); Assert.Equal(12750, Cash(s, 1));
        Assert.Equal(250, s.Engine.Inspect().Accounts.Single(a => a.Id == Contract(s).BondAccount).BalanceMinor);
        s.AdvanceTo(Contract(s).DeadlineTick); var d = Contract(s);
        Assert.Equal(4, d.UnfulfilledQuantity); Assert.Equal(2000, d.RefundedMinor); Assert.Equal(100, d.FineMinor); Assert.Equal(150, d.BondReturnedMinor);
        Assert.Equal(12900, Cash(s, 1));
        if (!capital) Assert.Equal(7100, Cash(s, 2));
        else Assert.Equal(100, s.Engine.Inspect().Accounts.Single(a => a.Kind == "CapitalCompensation").BalanceMinor);
        Assert.Equal(0, s.Engine.Inspect().Inventories.Sum(i => i.ReservedCapacity));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(s.Engine.Tick + 2); Assert.Equal(ledger, s.Engine.Ledger());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroDeliveryReturnsFullPurchasePriceAndForfeitsFundedBond(bool capital)
    {
        var s = Contracted(capital); s.AdvanceTo(Contract(s).DeadlineTick);
        Assert.Equal(5000, Contract(s).RefundedMinor); Assert.Equal(250, Contract(s).FineMinor); Assert.Equal(0, Contract(s).BondReturnedMinor);
        Assert.Equal(9750, Cash(s, 1)); if (!capital) Assert.Equal(10250, Cash(s, 2));
        Assert.Empty(s.Engine.Inspect().Inventories.SelectMany(i => i.Stock));
    }
    [Fact]
    public void ScarcityIsProportionalAndReversingSubmissionOrderDoesNotPreferFirstSupplier()
    {
        var d = Scenario(); d = d with { Participants = [.. d.Participants,
            d.Participants[0] with { Id = 3 }, d.Participants[0] with { Id = 4 }] };
        GameSession Run(bool reverse)
        {
            var s = Session(d); long o = Offer(s, 15);
            var bids = new[] { (1L, 6L), (3L, 8L), (4L, 3L) };
            foreach (var (actor, quantity) in reverse ? bids.Reverse() : bids)
                s.Send(new(actor), new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: o, Quantity: quantity));
            s.Step(); s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value); return s;
        }
        var a = Run(false); var b = Run(true);
        Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash());
        var contracts = a.Engine.Inspect().DeliveryContracts;
        Assert.Equal(5, contracts.Single(c => c.Supplier.Value == 1).Quantity);
        Assert.Equal(7, contracts.Single(c => c.Supplier.Value == 3).Quantity);
        Assert.Equal(3, contracts.Single(c => c.Supplier.Value == 4).Quantity);
        Assert.Equal(375, contracts.Sum(c => c.BondMinor));
    }
    [Fact]
    public void FailedRequestReplacementPreservesOriginalBondAndWithdrawalReleasesIt()
    {
        var s = Session(Scenario(supplierCash: 100)); long o = Offer(s);
        Assert.Equal("Accepted", Request(s, o, 2)); Assert.Equal("NoMoney", Request(s, o, 10));
        Assert.Equal(2, s.Engine.Observe(new(1)).OwnSupplyRequests.Single().Quantity); Assert.Equal(50, s.Engine.Observe(new(1)).Account.ReservedMinor);
        Assert.Equal("Accepted", Reason(s, new(ActionKind.WithdrawSupplyRequest, BuyOfferId: o)));
        Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor); Assert.Empty(s.Engine.Observe(new(1)).OwnSupplyRequests);
    }
    [Fact]
    public void NoDestinationSpaceRejectsTheWholeClosingBatchAndReleasesSupplierBonds()
    {
        var s = Session(Scenario(capacity: 5)); long o = Offer(s); Assert.Equal("Accepted", Request(s, o, 10));
        s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Assert.Empty(s.Engine.Inspect().DeliveryContracts); Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor);
        Assert.Equal(5000, s.Engine.Observe(new(2)).Account.ReservedMinor); Assert.Equal(2, s.Engine.Inspect().BuyOffers.Single().Window);
        Assert.Equal("StaleOfferWindow", Request(s, o, 5)); Assert.Equal("Accepted", Request(s, o, 5, window: 2));
        s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value); Assert.Equal(5, Contract(s).Quantity);
    }
    [Fact]
    public void CancellingRemainderDoesNotCancelAlreadyConcludedContract()
    {
        var s = Session(); long o = Offer(s); Request(s, o, 6); s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Assert.Equal(2000, s.Engine.Observe(new(2)).Account.ReservedMinor);
        Assert.Equal("Accepted", Reason(s, new(ActionKind.CancelBuyOffer, BuyOfferId: o), 2));
        Assert.Equal(0, s.Engine.Observe(new(2)).Account.ReservedMinor); Assert.Equal("Active", Contract(s).Status);
        Bring(s, 6); Assert.Equal("Accepted", Deliver(s, 6)); Assert.Equal(6, Contract(s).DeliveredQuantity);
    }
    [Fact]
    public void OfferExpiryReleasesUnusedBudgetWhileActiveContractsKeepTheirOwnHolds()
    {
        var d = Scenario() with { Delivery = new(ContractTtlTicks: 120, OfferTtlTicks: 10) }; var s = Session(d); long o = Offer(s);
        Request(s, o, 6); s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().ExpiresAtTick);
        Assert.Equal("Expired", s.Engine.Inspect().BuyOffers.Single().Status); Assert.Equal(4, s.Engine.Inspect().BuyOffers.Single().ReleasedQuantity);
        Assert.Equal("Active", Contract(s).Status); Assert.Equal(6, s.Engine.Inspect().Inventories.Single(i => i.Id == Contract(s).DestinationInventory).ReservedCapacity);
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public void DeadlineIncludesReceiptsOnThatTickAndRejectsNextTick(int offset)
    {
        var s = Contracted(); Bring(s, 10); s.AdvanceTo(Contract(s).DeadlineTick + offset - 1);
        Assert.Equal(offset <= 0 ? "Accepted" : "ContractClosed", Deliver(s, 6));
        s.AdvanceTo(Math.Max(s.Engine.Tick, Contract(s).DeadlineTick));
        Assert.Equal(offset <= 0 ? 6 : 0, Contract(s).DeliveredQuantity); Assert.Equal(offset <= 0 ? 100 : 250, Contract(s).FineMinor);
        Assert.Equal(offset <= 0 ? 4 : 10, s.Engine.Observe(new(1)).OwnInventories.Single(i => i.Id == s.Engine.Observe(new(1)).Participant.InventoryId).Stock.Single().Quantity);
    }
    [Fact]
    public void WrongSupplierPositionBusyAndOversizedReceiptDoNotPayOrConsumeGoods()
    {
        var s = Contracted(); Assert.Equal("NotAvailable", Deliver(s, 1, 2)); Assert.Equal("NotAtLocation", Deliver(s, 1));
        LandContentFixtures.Harvest(s, "wood", 10); Fixtures.Command(s, new(ActionKind.MoveTo, Target: Contract(s).Position));
        Assert.Equal("ParticipantBusy", Deliver(s, 1)); Fixtures.UntilIdle(s);
        Assert.Equal("InvalidQuantity", Deliver(s, 0)); Assert.Equal("InvalidQuantity", Deliver(s, 11));
        Assert.Equal(0, Contract(s).PaidMinor); Assert.Equal(10, Contract(s).RemainingQuantity);
    }
    [Fact]
    public void PendingReceiptAndDedupSurviveDirectLoad()
    {
        var s = Contracted(); Bring(s, 10); string id = s.Send(new(1), new(ActionKind.DeliverContract, ContractId: Contract(s).Id, Quantity: 6));
        var command = s.CommandTape.Single(c => c.Command.CommandId == id).Command;
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.Step(); loaded.Step();
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); var before = loaded.Engine.ComputeStateHash();
        Assert.Equal(ResultStatus.Accepted, loaded.Engine.Enqueue(new(1), command)!.Status); Assert.Equal(before, loaded.Engine.ComputeStateHash());
        Assert.Equal("CommandIdConflict", loaded.Engine.Enqueue(new(1), command with { Action = command.Action with { Quantity = 4 } })!.ReasonCode);
        Assert.Single(loaded.Engine.Ledger(), l => l.Reason == "DeliveryPayment");
    }
    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(7)] [InlineData(14)] [InlineData(17)] [InlineData(21)]
    [InlineData(34)] [InlineData(37)] [InlineData(41)] [InlineData(46)] [InlineData(47)]
    public void ScriptedTripsAndExpiryContinueIdenticallyAfterSaveEveryTick(int cut)
    {
        using var stream = typeof(DeliveryTests).Assembly.GetManifestResourceStream("Fixture.delivery-lifecycle.json")!;
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        var recording = Session(d); ScriptedScenarios.DeliveryLifecycle(recording, d);
        var s = Session(d); s.ConfigurePlayback(recording.CommandTape, recording.Engine.Tick, "delivery"); s.AdvanceTo(cut);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < recording.Engine.Tick) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(recording.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(recording.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(recording.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("deliveredQuantity", 1)] [InlineData("bondMinor", 1)] [InlineData("paidMinor", 500)]
    [InlineData("deadlineTick", 999)] [InlineData("refundedMinor", 1)]
    public void CorruptDeliveryStateIsStructurallyRejected(string field, long value)
    {
        var s = Contracted(); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; json["deliveryContracts"]![0]![field] = value;
        string reason = Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode;
        Assert.Contains(reason, new[] { "InvalidWorldCheckpoint", "OrphanCapacityReservation" });
    }
    [Fact]
    public void ContractAndCollateralAreVisibleOnlyToPartiesAndResearcher()
    {
        var d = Scenario(); d = d with { Participants = [.. d.Participants, d.Participants[0] with { Id = 3 }] };
        var s = Session(d); long o = Offer(s); Request(s, o, 10); s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Assert.Single(s.Engine.ObserveController(new(1)).OwnDeliveryContracts); Assert.Single(s.Engine.ObserveController(new(2)).OwnDeliveryContracts);
        Assert.Empty(s.Engine.ObserveController(new(3)).OwnDeliveryContracts); Assert.Empty(s.Engine.ObserveController(new(3)).OwnSupplyRequests);
        Assert.Single(s.Engine.Inspect().DeliveryContracts);
    }
    [Fact]
    public void CapitalSpotAndFutureContractsShareOneOrderAndBudget()
    {
        var d = Scenario(capital: true); d = d with { Participants = [.. d.Participants, d.Participants[0] with { Id = 3 }],
            CapitalOffers = [new(1, "wood", 10, 500, new(0, 0))] };
        var s = Session(d); LandContentFixtures.Harvest(s, "wood", 3);
        s.Send(new(3), new(ActionKind.AcceptDeliveryQuantity, OfferId: 1, Quantity: 8));
        s.Send(new(1), new(ActionKind.SellAvailableToCapital, OfferId: 1, ItemId: "wood", Quantity: 3)); s.Step();
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); long cut = s.Engine.Observe(new(1)).CapitalOffers.Single().CutoffTick!.Value;
        s.AdvanceTo(cut); loaded.AdvanceTo(cut); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(3, s.Engine.Inspect().Settlements.Single().Accepted); Assert.Equal(7, Contract(s).Quantity);
        Assert.Equal(0, s.Engine.Observe(new(1)).CapitalOffers.Single().RemainingQuantity);
        Assert.Equal(5000, s.Engine.Inspect().ExternalMoneyInMinor); Assert.Equal(3, s.Engine.Inspect().Flows.Single().Exported);
        Assert.Equal(3500, s.Engine.Inspect().Accounts.Single(a => a.Id == Contract(s).EscrowAccount).BalanceMinor);
        Assert.Equal(35000, s.Engine.Inspect().Accounts.Sum(a => a.BalanceMinor));
    }
    [Fact]
    public void SameActorCannotRequestSpotAndFutureQuotaInOneCapitalWindow()
    {
        var s = Session(Scenario(capital: true)); LandContentFixtures.Harvest(s, "wood", 1);
        Assert.Equal("Accepted", Reason(s, new(ActionKind.AcceptDeliveryQuantity, OfferId: 1, Quantity: 1)));
        Assert.Equal("ExistingDeliveryRequest", Reason(s, new(ActionKind.SellAvailableToCapital, OfferId: 1, ItemId: "wood", Quantity: 1)));
    }
    [Fact]
    public void ArrivalOnDeadlineReceivesGoodsBeforeExpiryAndReleasesBond()
    {
        var s = Contracted(); LandContentFixtures.Harvest(s, "wood", 10); long deadline = Contract(s).DeadlineTick;
        s.AdvanceTo(deadline - 6); Fixtures.Command(s, new(ActionKind.MoveTo, Target: Contract(s).Position));
        s.AdvanceTo(deadline - 1); Assert.True(s.Engine.Observe(new(1)).Participant.Busy);
        Assert.Equal("Accepted", Deliver(s, 10)); Assert.Equal("Fulfilled", Contract(s).Status);
        Assert.DoesNotContain(s.Engine.EventsSince(0), e => e.Type == "DeliveryExpired");
    }
    [Fact]
    public void DestinationCapacityBelongsToContractAndCannotBeConsumedByOtherTransfers()
    {
        var s = Session(Scenario(capacity: 10)); long offer = Offer(s); Request(s, offer, 10);
        s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(0, 0)), 2); Fixtures.UntilIdle(s, 2);
        Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1), 2); Fixtures.UntilIdle(s, 2);
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(5, 0)), 2); Fixtures.UntilIdle(s, 2);
        var o = s.Engine.Observe(new(2));
        Assert.Equal("NoCapacity", Reason(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 1,
            Source: o.Participant.InventoryId, Destination: o.OwnBuildings.Single().InputId), 2));
        Bring(s, 6); Assert.Equal("Accepted", Deliver(s, 6));
        var destination = s.Engine.Inspect().Inventories.Single(i => i.Id == Contract(s).DestinationInventory);
        Assert.Equal(6, destination.OccupiedVolume); Assert.Equal(4, destination.ReservedCapacity);
    }
    [Fact]
    public void SmallZeroBondAndRoundedFineDoNotCreateInvalidZeroPayments()
    {
        var s = Session(Scenario(supplierCash: 0)); long o = Offer(s, q: 1, price: 1);
        Assert.Equal("Accepted", Request(s, o, 1)); s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);
        Assert.Equal(0, Contract(s).BondMinor); s.AdvanceTo(Contract(s).DeadlineTick);
        Assert.Equal(1, Contract(s).RefundedMinor); Assert.Equal(0, Contract(s).FineMinor);
        Assert.DoesNotContain(s.Engine.Ledger(), l => l.AmountMinor <= 0);
    }
    [Fact]
    public void OfferPublicationChecksBuyerMoneyDestinationAndContractOverflow()
    {
        var s = Session(); var shop = s.Engine.Observe(new(2)).OwnBuildings.Single();
        Assert.Equal("NotAvailable", Reason(s, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "wood", Quantity: 1, UnitPriceMinor: 500)));
        Assert.Equal("NoMoney", Reason(s, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "wood", Quantity: 21, UnitPriceMinor: 500), 2));
        Assert.Equal("ArithmeticOverflow", Reason(s, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "wood", Quantity: 2, UnitPriceMinor: long.MaxValue), 2));
        Assert.Empty(s.Engine.Inspect().BuyOffers); Assert.Equal(0, s.Engine.Observe(new(2)).Account.ReservedMinor);
        long o = Offer(s); Assert.Equal("OwnOffer", Request(s, o, 1, actor: 2));
    }
    [Theory]
    [InlineData(0, 500, 180)] [InlineData(180, -1, 180)] [InlineData(180, 10001, 180)] [InlineData(180, 500, 0)]
    public void InvalidDeliverySettingsFailBeforeWorldCreation(int ttl, int fine, int offerTtl)
        => Assert.Throws<ArgumentException>(() => new SimulationEngine(Scenario() with { Delivery = new(ttl, fine, offerTtl) }));
}

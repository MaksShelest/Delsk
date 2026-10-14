using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MarketTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(MarketTests).Assembly.GetManifestResourceStream("Fixture.market-window.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Stocked(ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        LandContentFixtures.Harvest(s, "wood", 15); LandContentFixtures.Move(s, new(2, 0));
        var o = s.Engine.Observe(new(1)); var shop = o.OwnBuildings.Single();
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 15, Source: o.Participant.InventoryId, Destination: shop.InputId));
        return s;
    }
    private static long Offer(GameSession s, long quantity = 15)
    {
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: quantity, UnitPriceMinor: 500)));
        return s.Engine.Inspect().MarketOffers.Last().Id;
    }
    private static string Bid(GameSession s, long offer, long actor, long quantity, long max = 1000)
        => s.Send(new(actor), new(ActionKind.SubmitBuyRequest, MarketOfferId: offer, Quantity: quantity, MaximumPriceMinor: max));
    private static void ThreeBids(GameSession s, long offer, bool reverse = false)
    {
        var bids = new[] { (Actor: 2L, Quantity: 6L), (Actor: 3L, Quantity: 8L), (Actor: 4L, Quantity: 3L) };
        var ids = (reverse ? bids.Reverse() : bids).Select(b => Bid(s, offer, b.Actor, b.Quantity)).ToArray(); s.Step();
        Assert.All(ids, id => Assert.Equal(ResultStatus.Accepted, s.Engine.Result(id)!.Status));
    }
    private static MarketOfferView View(GameSession s, long offer) => s.Engine.Inspect().MarketOffers.Single(o => o.Id == offer);
    private static long Cash(GameSession s, long actor) => s.Engine.Observe(new(actor)).Account.BalanceMinor;
    private static string Raise(GameSession s, long offer, long actor, long price, int round = 1)
        => LandContentFixtures.Reason(s, new(ActionKind.RaiseBid, MarketOfferId: offer, MarketRound: round, UnitPriceMinor: price), actor);

    [Fact]
    public void ScarcityIncludesEveryBidAndPaysOwnFinalPriceIntoEscrowWithoutTeleport()
    {
        var s = Stocked(); long offer = Offer(s); ThreeBids(s, offer); s.AdvanceTo(View(s, offer).CutoffTick);
        Assert.Equal(MarketOfferStatus.Auction, View(s, offer).Status); Assert.Empty(s.Engine.Inspect().MarketAllocations);
        Assert.Equal(10000, Cash(s, 1));
        Assert.Equal("Accepted", Raise(s, offer, 3, 600)); Assert.Equal("Accepted", Raise(s, offer, 4, 700));
        s.AdvanceTo(View(s, offer).CutoffTick); Assert.Equal(2, View(s, offer).Round);
        s.AdvanceTo(View(s, offer).CutoffTick); var w = s.Engine.Inspect(); var allocations = w.MarketAllocations;
        Assert.Equal(4, allocations.Single(a => a.Buyer.Value == 2).Quantity);
        Assert.Equal(8, allocations.Single(a => a.Buyer.Value == 3).Quantity);
        Assert.Equal(3, allocations.Single(a => a.Buyer.Value == 4).Quantity);
        Assert.Equal(8900, allocations.Sum(a => a.Quantity * a.UnitPriceMinor));
        Assert.Equal(10000, Cash(s, 1)); Assert.Equal(8000, Cash(s, 2)); Assert.Equal(5200, Cash(s, 3)); Assert.Equal(7900, Cash(s, 4));
        Assert.Equal(8900, w.Accounts.Where(a => a.Kind == "ContractEscrow").Sum(a => a.BalanceMinor));
        Assert.All(allocations, a => Assert.Equal("AwaitingPickup", a.Status));
        Assert.Equal(15, w.Inventories.Single(i => i.Id == allocations[0].SourceInventory).Stock.Single().Quantity);
        Assert.Equal(15, w.Inventories.Single(i => i.Id == allocations[0].SourceInventory).Stock.Single().Reserved);
        Assert.All(new long[] { 2, 3, 4 }, id => Assert.Empty(s.Engine.Observe(new(id)).OwnInventories.Single().Stock));
        Assert.Equal(40000, w.Accounts.Sum(a => a.BalanceMinor));
        Assert.All(w.Accounts, a => Assert.Equal(0, a.ReservedMinor));
    }
    [Fact]
    public void SufficientStockAllocatesAllAtBasePriceAndReleasesUnsoldLot()
    {
        var s = Stocked(); long o = Offer(s); Bid(s, o, 2, 6); Bid(s, o, 3, 8); s.Step(); s.AdvanceTo(View(s, o).CutoffTick);
        Assert.Equal(MarketOfferStatus.Closed, View(s, o).Status); Assert.Equal(0, View(s, o).Round);
        Assert.Equal(14, View(s, o).AllocatedQuantity); Assert.All(s.Engine.Inspect().MarketAllocations, a => Assert.Equal(500, a.UnitPriceMinor));
        var shop = s.Engine.Observe(new(1)).OwnBuildings.Single(); var stock = s.Engine.Observe(new(1)).OwnInventories.Single(i => i.Id == shop.InputId).Stock.Single();
        Assert.Equal(15, stock.Quantity); Assert.Equal(14, stock.Reserved);
    }
    [Fact]
    public void EmptyWindowReleasesLotWithoutPayments()
    {
        var s = Stocked(); long o = Offer(s); s.AdvanceTo(View(s, o).CutoffTick);
        Assert.Empty(s.Engine.Inspect().MarketAllocations); Assert.Equal(0, View(s, o).AllocatedQuantity);
        Assert.All(s.Engine.Observe(new(1)).OwnInventories.SelectMany(i => i.Stock), stock => Assert.Equal(0, stock.Reserved));
        Assert.Empty(s.Engine.Ledger());
    }
    [Fact]
    public void CompetingOffersCannotReserveTheSameStockTwice()
    {
        var s = Stocked(); Offer(s, 10);
        Assert.Equal("NoStock", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 6, UnitPriceMinor: 500)));
        Assert.Single(s.Engine.Inspect().MarketOffers);
    }
    [Fact]
    public void CashCannotBePromisedInTwoWindowsAndFailedReplacementKeepsOriginal()
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p.Id == 2 ? p with { CashMinor = 3000 } : p).ToArray() };
        var s = Stocked(d); long a = Offer(s, 10), b = Offer(s, 5); string first = Bid(s, a, 2, 6); s.Step();
        Assert.Equal(ResultStatus.Accepted, s.Engine.Result(first)!.Status);
        string second = Bid(s, b, 2, 1); s.Step(); Assert.Equal("NoMoney", s.Engine.Result(second)!.ReasonCode);
        string edit = Bid(s, a, 2, 7); s.Step(); Assert.Equal("NoMoney", s.Engine.Result(edit)!.ReasonCode);
        Assert.Equal(6, s.Engine.Observe(new(2)).OwnMarketBids.Single().Quantity);
        Assert.Equal(3000, s.Engine.Observe(new(2)).Account.ReservedMinor);
    }
    [Fact]
    public void ReplacingRequestDoesNotGiveExtraLotteryTicketsAndWithdrawReleasesMoney()
    {
        var s = Stocked(); long o = Offer(s); Bid(s, o, 2, 6); s.Step(); Bid(s, o, 2, 4); s.Step();
        Assert.Single(View(s, o).Bids); Assert.Equal(4, View(s, o).Bids[0].Quantity);
        Assert.Equal(2000, s.Engine.Observe(new(2)).Account.ReservedMinor);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.WithdrawBid, MarketOfferId: o), 2));
        Assert.Empty(View(s, o).Bids); Assert.Equal(0, s.Engine.Observe(new(2)).Account.ReservedMinor);
    }
    [Fact]
    public void RaiseFailurePreservesOldBidAndHold()
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p.Id == 3 ? p with { CashMinor = 5000 } : p).ToArray() };
        var s = Stocked(d); long o = Offer(s); ThreeBids(s, o); s.AdvanceTo(View(s, o).CutoffTick);
        Assert.Equal("NoMoney", Raise(s, o, 3, 700)); Assert.Equal("BidLimitExceeded", Raise(s, o, 3, 1100));
        Assert.Equal("RaiseTooSmall", Raise(s, o, 3, 500));
        Assert.Equal(500, s.Engine.Observe(new(3)).OwnMarketBids.Single().CurrentPriceMinor);
        Assert.Equal(4000, s.Engine.Observe(new(3)).Account.ReservedMinor);
    }
    [Fact]
    public void AuctionRejectsNewMembersAndStaleRoundsAndWithdrawnMemberCannotRejoin()
    {
        var s = Stocked(); long o = Offer(s, 5); Bid(s, o, 2, 4); Bid(s, o, 3, 4); s.Step(); s.AdvanceTo(View(s, o).CutoffTick);
        string late = Bid(s, o, 4, 1); s.Step(); Assert.Equal("MarketAlreadyInAuction", s.Engine.Result(late)!.ReasonCode);
        Assert.Equal("StaleMarketRound", Raise(s, o, 2, 600, round: 0));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.WithdrawBid, MarketOfferId: o, MarketRound: 1), 2));
        string rejoin = Bid(s, o, 2, 1); s.Step(); Assert.Equal("MarketAlreadyInAuction", s.Engine.Result(rejoin)!.ReasonCode);
        s.AdvanceTo(View(s, o).CutoffTick); Assert.Equal("StaleMarketRound", Raise(s, o, 3, 600, round: 1));
    }
    [Fact]
    public void CutoffIsInclusiveAndNextTickCannotEnterClosedWindow()
    {
        var s = Stocked(); long o = Offer(s); long cutoff = View(s, o).CutoffTick; s.AdvanceTo(cutoff - 1);
        string id = Bid(s, o, 2, 6); s.Step(); Assert.Equal(ResultStatus.Accepted, s.Engine.Result(id)!.Status);
        Assert.Single(s.Engine.Inspect().MarketAllocations);
        string late = Bid(s, o, 3, 1); s.Step(); Assert.Equal("NotAvailable", s.Engine.Result(late)!.ReasonCode);
    }
    [Fact]
    public void FinalRoundCutoffRaiseCountsButFollowingRaiseCannotChangeAllocation()
    {
        var s = Stocked(); long o = Offer(s); ThreeBids(s, o); s.AdvanceTo(View(s, o).CutoffTick);
        s.AdvanceTo(View(s, o).CutoffTick); long close = View(s, o).CutoffTick; s.AdvanceTo(close - 1);
        Assert.Equal("Accepted", Raise(s, o, 4, 900, round: 2));
        Assert.Equal(900, s.Engine.Inspect().MarketAllocations.Single(a => a.Buyer.Value == 4).UnitPriceMinor);
        Assert.Equal("NotAvailable", Raise(s, o, 2, 1000, round: 2));
    }
    [Fact]
    public void LimitIsPrivateWhilePublicRaisedPriceIsVisible()
    {
        var s = Stocked(); long o = Offer(s); Bid(s, o, 2, 6, 731); s.Step();
        var self = s.Engine.ObserveController(new(2)); var foreign = s.Engine.ObserveController(new(3));
        Assert.Equal(731, self.OwnMarketBids.Single().MaximumPriceMinor); Assert.Empty(foreign.OwnMarketBids);
        Assert.DoesNotContain("731", JsonSerializer.Serialize(foreign, SimulationJson.Options));
        Assert.Equal(500, foreign.MarketOffers.Single().Bids.Single().CurrentPriceMinor);
        Assert.Empty(s.Engine.ObserveController(new(4)).OwnMarketAllocations);
    }
    [Fact]
    public void ForeignShopAndSelfPurchaseAreRejectedWithoutChangingReservations()
    {
        var s = Stocked();
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 1, UnitPriceMinor: 500), 2));
        long o = Offer(s); string self = Bid(s, o, 1, 1); s.Step(); Assert.Equal("OwnOffer", s.Engine.Result(self)!.ReasonCode);
        Assert.Empty(s.Engine.Inspect().MarketBids);
    }
    [Fact]
    public void ClosedAllocatedStockCannotBeLoadedOrOfferedAgainAndCloseDoesNotPayTwice()
    {
        var s = Stocked(); long o = Offer(s); Bid(s, o, 2, 15); s.Step(); s.AdvanceTo(View(s, o).CutoffTick);
        var seller = s.Engine.Observe(new(1));
        Assert.Equal("NoStock", LandContentFixtures.Reason(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 1,
            Source: seller.OwnBuildings.Single().InputId, Destination: seller.Participant.InventoryId)));
        Assert.Equal("NoStock", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer, Building: new(1), ItemId: "wood", Quantity: 1, UnitPriceMinor: 500)));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(s.Engine.Tick + 10); Assert.Equal(ledger, s.Engine.Ledger()); Assert.Single(s.Engine.Inspect().MarketAllocations);
    }
    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(7)] [InlineData(12)] [InlineData(16)]
    public void SavingWindowRoundAndAllocationRestoresExactly(int offset)
    {
        var s = Stocked(); long o = Offer(s); long published = s.Engine.Tick; ThreeBids(s, o);
        s.AdvanceTo(Math.Max(s.Engine.Tick, published + offset));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); long end = published + 25;
        s.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Fact]
    public void SameSeedAndInputsReversedAcrossParticipantsProduceSameAllocation()
    {
        var a = Stocked(); var b = Stocked(); long oa = Offer(a), ob = Offer(b); ThreeBids(a, oa); ThreeBids(b, ob, reverse: true);
        a.AdvanceTo(a.Engine.Tick + 20); b.AdvanceTo(b.Engine.Tick + 20);
        Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash()); Assert.Equal(a.Engine.Inspect().MarketAllocations, b.Engine.Inspect().MarketAllocations);
    }
    [Fact]
    public void EqualPricesDoNotAlwaysPreferLowestParticipantIdAcrossSeeds()
    {
        var first = new HashSet<long>();
        foreach (ulong seed in Enumerable.Range(1, 16).Select(i => (ulong)i))
        {
            var s = Stocked(Scenario() with { Seed = seed }); long o = Offer(s, 1);
            Bid(s, o, 2, 1); Bid(s, o, 3, 1); Bid(s, o, 4, 1); s.Step(); s.AdvanceTo(s.Engine.Tick + 20);
            first.Add(s.Engine.Inspect().MarketAllocations.Single().Buyer.Value);
        }
        Assert.True(first.Count > 1); // Detect fixed-ID priority, not a statistically exact distribution.
    }
    [Fact]
    public void InvalidSettingsAndPriceOverflowAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SimulationEngine(Scenario() with { Market = new(AuctionRounds: 0) }));
        var s = Stocked(); Assert.Equal("ArithmeticOverflow", LandContentFixtures.Reason(s, new(ActionKind.CreateSellOffer,
            Building: new(1), ItemId: "wood", Quantity: 15, UnitPriceMinor: long.MaxValue)));
        Assert.Empty(s.Engine.Inspect().MarketOffers);
    }
    [Fact]
    public void ConfiguredSingleRoundAndMinimumRaiseAreHonored()
    {
        var s = Stocked(Scenario() with { Market = new(AuctionRoundTicks: 3, AuctionRounds: 1, MinimumRaiseMinor: 10) });
        long o = Offer(s); ThreeBids(s, o); long start = View(s, o).CutoffTick; s.AdvanceTo(start);
        Assert.Equal(new MarketSettings(3, 1, 10), s.Engine.ObserveController(new(2)).MarketRules);
        Assert.Equal(start + 3, View(s, o).CutoffTick); Assert.Equal("RaiseTooSmall", Raise(s, o, 2, 501));
        Assert.Equal("Accepted", Raise(s, o, 2, 510)); s.AdvanceTo(start + 3);
        Assert.Equal(MarketOfferStatus.Closed, View(s, o).Status); Assert.Equal(1, View(s, o).Round);
        Assert.Equal(510, s.Engine.Inspect().MarketAllocations.Single(a => a.Buyer.Value == 2).UnitPriceMinor);
    }
    [Fact]
    public void PendingRaiseSurvivesLoadAndDuplicateEnvelopeCannotPayTwice()
    {
        var s = Stocked(); long o = Offer(s); ThreeBids(s, o); s.AdvanceTo(View(s, o).CutoffTick);
        string id = s.Send(new(2), new(ActionKind.RaiseBid, MarketOfferId: o, MarketRound: 1, UnitPriceMinor: 650));
        var command = s.CommandTape.Single(c => c.Command.CommandId == id).Command;
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); long end = s.Engine.Tick + 15;
        s.AdvanceTo(end); loaded.AdvanceTo(end); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        string before = loaded.Engine.ComputeStateHash(); var ledger = loaded.Engine.Ledger();
        Assert.Equal(ResultStatus.Accepted, loaded.Engine.Enqueue(new(2), command)!.Status);
        Assert.Equal(before, loaded.Engine.ComputeStateHash()); Assert.Equal(ledger, loaded.Engine.Ledger());
        Assert.Equal("CommandIdConflict", loaded.Engine.Enqueue(new(2), command with { Action = command.Action with { UnitPriceMinor = 700 } })!.ReasonCode);
        Assert.Equal(before, loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void CorruptPickupEscrowReferenceIsRejectedOnDirectLoad()
    {
        var s = Stocked(); long o = Offer(s); Bid(s, o, 2, 15); s.Step(); s.AdvanceTo(View(s, o).CutoffTick);
        var json = System.Text.Json.Nodes.JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["marketAllocations"]![0]!["escrowAccount"]!["value"] = s.Engine.Observe(new(2)).Account.Id.Value;
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }

}
